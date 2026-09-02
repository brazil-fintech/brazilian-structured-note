import { useCallback, useEffect, useRef, useState } from 'react';
import {
  ApiError, api,
  type ClearingFile, type ClearingParams, type ClearingResponse,
  type StoredClearingFile, type StoredClearingSet,
} from '../api/client';
import { ui } from '../engine/texts';

interface Props {
  assetId: string;
  /** What the header names the certificate; the list row and the form both know it. */
  assetName?: string;
  figureCode?: string;
  culture: string;
  /** Shown once, right after a save, so the screen says why it opened. */
  justSaved?: boolean;
  onBack: () => void;
}

/**
 * The registration as B3 receives it. Everything on screen comes from `GET /api/assets/{id}/clearing`,
 * which writes the files from the stored values — the two inputs here are the issuer's short name
 * and the date the upload is stamped with, neither of which is a property of the certificate.
 *
 * The preview is the file, character for character, so a desk can read it against the manual's
 * page. The download does not reuse it: it asks the API for the bytes, because CETIP reads a
 * single-byte encoding and re-encoding the preview in the browser would shift every field after
 * the first accented character of a commercial name.
 *
 * Previewing and keeping are separate actions, as they are on the API. Opening the screen reads;
 * "gerar e arquivar" writes, and what it writes is the files themselves — so what B3 was sent
 * stays answerable after the asset is edited, the template moves on, or the issuer's short name
 * changes, none of which the same certificate would produce the same file under.
 */
export function ClearingFiles({ assetId, assetName, figureCode, culture, justSaved, onBack }: Props) {
  const [participant, setParticipant] = useState('');
  const [date, setDate] = useState(() => new Date().toISOString().slice(0, 10));
  const [result, setResult] = useState<ClearingResponse | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [downloading, setDownloading] = useState<string | null>(null);
  const [copied, setCopied] = useState<string | null>(null);
  const [saved, setSaved] = useState<StoredClearingSet[]>([]);
  const [storing, setStoring] = useState(false);
  const [justStored, setJustStored] = useState(false);

  const params = useCallback((): ClearingParams => ({ participant, date }), [participant, date]);

  const generate = useCallback(async () => {
    setBusy(true);
    setError(null);
    try {
      setResult(await api.clearingFiles(assetId, { participant, date }));
    } catch (e) {
      // A 400 here is the readable half of the contract: a value that will not fit its field, or
      // a participant name nobody configured. Both are the desk's to fix, so show what it said.
      setResult(null);
      setError(e instanceof ApiError ? e.message : (e as Error).message);
    } finally {
      setBusy(false);
    }
  }, [assetId, participant, date]);

  // The screen exists to show the files, so it asks for them as it opens rather than making the
  // first thing on it a button. The generation reads the stored values and writes nothing, so a
  // repeated call costs a request and changes no state at B3 or here.
  const generated = useRef(false);
  useEffect(() => {
    if (generated.current) return;
    generated.current = true;
    void generate();
  }, [generate]);

  const reloadSaved = useCallback(async () => {
    try {
      setSaved(await api.savedClearingSets(assetId));
    } catch (e) {
      setError((e as Error).message);
    }
  }, [assetId]);

  useEffect(() => { void reloadSaved(); }, [reloadSaved]);

  // The write. It generates server-side again rather than sending the preview back up: what is
  // kept has to be what the API produced, not what a browser round-tripped through JSON.
  const store = useCallback(async () => {
    setStoring(true);
    setError(null);
    setJustStored(false);
    try {
      const set = await api.saveClearingFiles(assetId, { participant, date });
      setSaved((current) => [set, ...current]);
      setJustStored(true);
      await generate();
    } catch (e) {
      setError(e instanceof ApiError ? e.message : (e as Error).message);
    } finally {
      setStoring(false);
    }
  }, [assetId, participant, date, generate]);

  const downloadStored = useCallback(async (file: StoredClearingFile) => {
    setDownloading(file.id);
    setError(null);
    try {
      saveBlob(await api.savedClearingFileBlob(assetId, file.id), file.fileName);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setDownloading(null);
    }
  }, [assetId]);

  const download = useCallback(async (file: ClearingFile) => {
    setDownloading(file.operation);
    setError(null);
    try {
      const blob = await api.clearingFileBlob(assetId, file.operation, params());
      saveBlob(blob, file.fileName);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setDownloading(null);
    }
  }, [assetId, params]);

  const downloadAll = useCallback(async () => {
    for (const file of result?.files ?? []) await download(file);
  }, [result, download]);

  const copy = useCallback(async (file: ClearingFile) => {
    try {
      await navigator.clipboard.writeText(file.content);
      setCopied(file.operation);
      setTimeout(() => setCopied(null), 2000);
    } catch {
      // A browser that withholds the clipboard is not an error worth a banner: the content is
      // on screen and selectable.
    }
  }, []);

  return (
    <div className="clearing">
      <header className="list__header">
        <div>
          <h2 className="list__title">{ui.b3FilesTitle(culture)}</h2>
          <p className="clearing__subtitle">
            {assetName && <span className="table__primary">{assetName}</span>}
            {figureCode && <span className="badge">{figureCode}</span>}
          </p>
          <p className="clearing__help">{ui.b3FilesHelp(culture)}</p>
        </div>
        <button type="button" className="button button--ghost" onClick={onBack}>
          {ui.backToList(culture)}
        </button>
      </header>

      {justSaved && <div className="banner">{ui.assetSaved(culture)}</div>}
      {justStored && <div className="banner">{ui.filesSaved(culture)}</div>}
      {error && <div className="banner banner--error">{error}</div>}

      <section className="panel">
        <h3 className="panel__title">{ui.generate(culture)}</h3>
        <div className="section">
          <div className="filters">
            <label className="filter filter--grow">
              <span className="filter__label">{ui.participant(culture)}</span>
              <input
                type="text"
                className="field__input"
                value={participant}
                maxLength={30}
                placeholder={ui.participantHelp(culture)}
                onChange={(e) => { setParticipant(e.target.value); setJustStored(false); }}
              />
            </label>

            <label className="filter">
              <span className="filter__label">{ui.fileDate(culture)}</span>
              <input
                type="date"
                className="field__input"
                value={date}
                onChange={(e) => { setDate(e.target.value); setJustStored(false); }}
              />
            </label>

            <div className="clearing__actions">
              <button
                type="button"
                className="button button--primary"
                disabled={busy}
                onClick={() => { void generate(); }}
              >
                {busy ? ui.generating(culture) : result ? ui.regenerate(culture) : ui.generate(culture)}
              </button>
              <button
                type="button"
                className="button"
                disabled={storing || busy}
                onClick={() => { void store(); }}
              >
                {storing ? ui.savingFiles(culture) : ui.saveFiles(culture)}
              </button>
              {result && result.files.length > 0 && (
                <button
                  type="button"
                  className="button"
                  disabled={downloading !== null}
                  onClick={() => { void downloadAll(); }}
                >
                  {ui.downloadAll(culture)}
                </button>
              )}
            </div>
          </div>
        </div>
      </section>

      {!result && !busy && !error && <p className="clearing__empty">{ui.noFilesYet(culture)}</p>}

      {result?.files.map((file) => (
        <section className="panel" key={file.operation}>
          <div className="clearing__file-head">
            <div>
              <div className="table__primary">{file.layout}</div>
              <div className="table__secondary">{file.fileName}</div>
            </div>
            <div className="clearing__file-meta">
              <span className="chip">{ui.operation(culture)} {file.operation}</span>
              <span className="chip chip--muted">{ui.records(culture, file.records)}</span>
              <button
                type="button"
                className="button button--ghost"
                onClick={() => { void copy(file); }}
              >
                {copied === file.operation ? ui.copied(culture) : ui.copyContent(culture)}
              </button>
              <button
                type="button"
                className="button"
                disabled={downloading === file.operation}
                onClick={() => { void download(file); }}
              >
                {ui.download(culture)}
              </button>
            </div>
          </div>
          {/* The lines as they go out. Fixed width, so column 1 of the preview is column 1 of
              the record and a field can be counted off against the manual. */}
          <pre className="clearing__preview">{file.content}</pre>
        </section>
      ))}

      {result && result.notes.length > 0 && (
        <section className="panel">
          <h3 className="panel__title">{ui.notes(culture)}</h3>
          <ul className="clearing__notes">
            {result.notes.map((note) => <li key={note}>{note}</li>)}
          </ul>
        </section>
      )}

      {/* What is in the database. A download here reads the stored bytes, so it hands over the
          file that was kept rather than one written again from the asset as it stands now. */}
      <section className="panel">
        <h3 className="panel__title">{ui.savedSets(culture)}</h3>
        {saved.length === 0
          ? <p className="clearing__empty clearing__empty--inset">{ui.noSavedSets(culture)}</p>
          : (
            <ul className="clearing__saved">
              {saved.map((set) => (
                <li className="clearing__saved-set" key={set.id}>
                  <div className="clearing__saved-head">
                    <span className="table__primary">
                      {ui.generatedAt(culture)} {formatStamp(set.generatedUtc, culture)}
                    </span>
                    <span className="table__secondary">
                      {set.participantName} · {ui.fileDate(culture).toLowerCase()} {set.fileDate}
                      {set.generatedBy && ` · ${ui.generatedBy(culture)} ${set.generatedBy}`}
                      {` · v${set.templateVersion}`}
                    </span>
                  </div>
                  <ul className="clearing__saved-files">
                    {set.files.map((file) => (
                      <li key={file.id}>
                        <span className="chip">{file.operation}</span>
                        <span className="clearing__saved-name">{file.fileName}</span>
                        <span className="table__secondary">
                          {ui.records(culture, file.records)} · {ui.bytes(culture, file.bytes)}
                        </span>
                        <button
                          type="button"
                          className="button button--ghost"
                          disabled={downloading === file.id}
                          onClick={() => { void downloadStored(file); }}
                        >
                          {ui.download(culture)}
                        </button>
                      </li>
                    ))}
                  </ul>
                </li>
              ))}
            </ul>
          )}
      </section>
    </div>
  );
}

/** The generation stamp, in the reader's own locale rather than as an ISO string. */
function formatStamp(iso: string, culture: string): string {
  const at = new Date(iso);
  return Number.isNaN(at.getTime())
    ? iso
    : at.toLocaleString(culture.startsWith('en') ? 'en-GB' : 'pt-BR');
}

/** Hands the bytes to the browser under the name B3 expects, then releases the object URL. */
function saveBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}
