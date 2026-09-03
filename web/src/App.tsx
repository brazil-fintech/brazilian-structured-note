import { useCallback, useEffect, useState } from 'react';
import { api, ApiUnreachableError, type AssetListItem, type FigureCatalogueEntry, type FigureCoverage, type FigureSummary } from './api/client';
import { ApiConnection } from './components/ApiConnection';
import { AssetForm } from './components/AssetForm';
import { AssetList } from './components/AssetList';
import { ClearingFiles } from './components/ClearingFiles';
import { FigurePicker } from './components/FigurePicker';
import { readPath } from './engine/paths';
import type { FigureTemplate, InstanceValues } from './engine/types';
import { ui } from './engine/texts';

type View =
  | { kind: 'list' }
  | { kind: 'pick' }
  | { kind: 'edit'; template: FigureTemplate; assetId?: string; values?: InstanceValues; rowVersion?: string }
  // Where a booked certificate becomes the files B3 is sent. Reached straight after a creation,
  // and from the row of any asset already on the list.
  | { kind: 'files'; assetId: string; assetName?: string; figureCode?: string; justSaved: boolean };

export default function App() {
  const [culture, setCulture] = useState<'pt-BR' | 'en-GB'>('pt-BR');
  const [figures, setFigures] = useState<FigureSummary[]>([]);
  // The list filter offers what can be booked; the picker shows B3's whole catalogue.
  const [catalogue, setCatalogue] = useState<FigureCatalogueEntry[]>([]);
  const [coverage, setCoverage] = useState<FigureCoverage>({ published: 0, configured: 0, bookable: 0 });
  const [view, setView] = useState<View>({ kind: 'list' });
  const [error, setError] = useState<string | null>(null);
  // A call that never reached an API is not a message among others: nothing on the screen can
  // work until it is fixed, and on the published copy fixing it means naming an API.
  const [unreachable, setUnreachable] = useState(false);
  const [busy, setBusy] = useState(false);

  // Stable: the list keeps it in an effect's dependencies, and a new identity per render would
  // refire its query on every keystroke elsewhere on the page.
  const noteUnreachable = useCallback(() => setUnreachable(true), []);

  const noteFailure = useCallback((e: unknown) => {
    if (e instanceof ApiUnreachableError) { setUnreachable(true); return; }
    setError((e as Error).message);
  }, []);

  useEffect(() => {
    api.listFigures()
      .then(setFigures)
      .catch(noteFailure);

    api.listFigureCatalogue()
      .then((result) => { setCatalogue(result.figures); setCoverage(result.coverage); })
      .catch(noteFailure);
  }, [noteFailure]);

  const openNew = useCallback(async (figure: FigureCatalogueEntry) => {
    setBusy(true);
    setError(null);
    try {
      const template = await api.getTemplate(figure.code);
      setView({ kind: 'edit', template });
    } catch (e) {
      noteFailure(e);
    } finally {
      setBusy(false);
    }
  }, [noteFailure]);

  const openExisting = useCallback(async (asset: AssetListItem) => {
    setBusy(true);
    setError(null);
    try {
      const [detail, template] = await Promise.all([
        api.getAsset(asset.id),
        api.getTemplate(asset.figureCode),
      ]);
      setView({
        kind: 'edit',
        template,
        assetId: detail.id,
        values: detail.values,
        rowVersion: detail.rowVersion,
      });
    } catch (e) {
      noteFailure(e);
    } finally {
      setBusy(false);
    }
  }, [noteFailure]);

  // Booking a new certificate is not the end of it: the registration still has to be written and
  // sent, so a creation lands on the screen that writes it. Editing one already booked goes back
  // to the list as before — its files are a row action away.
  const afterSave = useCallback((
    assetId: string, template: FigureTemplate, values: InstanceValues, wasNew: boolean,
  ) => {
    if (!wasNew) { setView({ kind: 'list' }); return; }

    const name = readPath(values, 'common.commercialName');
    setView({
      kind: 'files',
      assetId,
      assetName: typeof name === 'string' && name.length > 0 ? name : undefined,
      figureCode: template.figureCode,
      justSaved: true,
    });
  }, []);

  return (
    <div className="app">
      <header className="app__header">
        <div className="app__brand">
          <span className="app__logo" aria-hidden="true">COE</span>
          <div>
            <div className="app__name">Certificado de Operações Estruturadas</div>
            <div className="app__tagline">
              {culture === 'pt-BR'
                ? 'Registro de ativos por figura de payoff B3'
                : 'Asset booking by B3 payoff figure'}
            </div>
          </div>
        </div>
        <button
          type="button"
          className="button button--ghost"
          onClick={() => setCulture(culture === 'pt-BR' ? 'en-GB' : 'pt-BR')}
        >
          {culture === 'pt-BR' ? 'EN' : 'PT'}
        </button>
      </header>

      <main className="app__main">
        {unreachable && <ApiConnection culture={culture} />}
        {!unreachable && error && <div className="banner banner--error">{error}</div>}
        {busy && <div className="banner">{ui.loading(culture)}</div>}

        {view.kind === 'list' && (
          <AssetList
            culture={culture}
            figures={figures}
            onUnreachable={noteUnreachable}
            onCreate={() => setView({ kind: 'pick' })}
            onEdit={(asset) => { void openExisting(asset); }}
            onFiles={(asset) => setView({
              kind: 'files',
              assetId: asset.id,
              assetName: asset.commercialName,
              figureCode: asset.figureCode,
              justSaved: false,
            })}
          />
        )}

        {view.kind === 'pick' && (
          <FigurePicker
            figures={catalogue}
            coverage={coverage}
            culture={culture}
            onPick={(figure) => { void openNew(figure); }}
            onCancel={() => setView({ kind: 'list' })}
          />
        )}

        {view.kind === 'edit' && (
          <AssetForm
            key={`${view.template.figureCode}:${view.assetId ?? 'new'}`}
            template={view.template}
            assetId={view.assetId}
            initialValues={view.values}
            initialRowVersion={view.rowVersion}
            culture={culture}
            onCancel={() => setView({ kind: 'list' })}
            onSaved={(assetId, values) => afterSave(assetId, view.template, values, view.assetId === undefined)}
          />
        )}

        {view.kind === 'files' && (
          <ClearingFiles
            assetId={view.assetId}
            assetName={view.assetName}
            figureCode={view.figureCode}
            culture={culture}
            justSaved={view.justSaved}
            onBack={() => setView({ kind: 'list' })}
          />
        )}
      </main>
    </div>
  );
}
