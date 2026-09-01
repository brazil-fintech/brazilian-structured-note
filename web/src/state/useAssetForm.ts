import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { api } from '../api/client';
import { applyComputed, buildDefaults, emptyRow } from '../engine/instance';
import { readPath, sectionOf, writePath } from '../engine/paths';
import type {
  FigureTemplate, InstanceValues, Json, TemplateSection, ValidationMessage,
} from '../engine/types';
import { deduplicate, validate } from '../engine/validate';

const SERVER_DEBOUNCE_MS = 400;

export interface UseAssetFormOptions {
  template: FigureTemplate;
  assetId?: string | null;
  initialValues?: InstanceValues;
  initialRowVersion?: string;
  culture?: string;
  onSaved?: (assetId: string) => void;
}

export interface AssetFormState {
  values: InstanceValues;
  setValue: (path: string, value: Json) => void;
  touch: (path: string) => void;
  addRow: (section: TemplateSection) => void;
  removeRow: (section: TemplateSection, index: number) => void;

  /** Messages for one concrete path, client and server merged. */
  messagesFor: (path: string) => ValidationMessage[];
  messagesInSection: (sectionKey: string) => ValidationMessage[];
  allMessages: ValidationMessage[];
  errorCount: number;
  warningCount: number;

  checking: boolean;
  saving: boolean;
  saveError: string | null;
  conflict: string | null;
  showAll: boolean;
  submit: (acceptWarnings?: boolean) => Promise<boolean>;
}

/**
 * Owns the booking form: the instance document, the client-side checks that run on every
 * keystroke, and the debounced server pass that answers the checks the browser cannot make.
 *
 * The two sets are kept apart on purpose. Client messages are recomputed from scratch on every
 * change, so they never linger. Server messages are keyed by path and replaced only for the paths
 * the API reports it was authoritative about — otherwise a narrow "field" pass would wipe findings
 * about parts of the form it never looked at. That set covers the targets of every rule the pass
 * considered, not only the ones that complained, which is what lets an edit replace the previous
 * answer about a field rather than stack another copy of it.
 *
 * The merged view is deduplicated, because a rule marked `execution: "both"` is answered by the
 * browser and by the API and would otherwise say the same thing twice.
 */
export function useAssetForm(options: UseAssetFormOptions): AssetFormState {
  const { template, assetId, culture = 'pt-BR' } = options;

  const [values, setValues] = useState<InstanceValues>(() =>
    options.initialValues
      ? applyComputed(template, options.initialValues)
      : buildDefaults(template));

  const [touched, setTouched] = useState<Set<string>>(() => new Set());
  const [showAll, setShowAll] = useState(false);
  const [serverMessages, setServerMessages] = useState<Record<string, ValidationMessage[]>>({});
  const [checking, setChecking] = useState(false);
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [conflict, setConflict] = useState<string | null>(null);
  const [rowVersion, setRowVersion] = useState<string | undefined>(options.initialRowVersion);

  const pendingPaths = useRef<Set<string>>(new Set());
  const debounceTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const inFlight = useRef<AbortController | null>(null);
  const latestValues = useRef(values);
  latestValues.current = values;

  // ----- client-side pass ---------------------------------------------------------

  const clientMessages = useMemo(() => {
    const result = validate(template, values, { scope: showAll ? 'submit' : 'form', culture });
    return result.messages;
  }, [template, values, showAll, culture]);

  // ----- server-side pass ---------------------------------------------------------

  const runServerValidation = useCallback(async () => {
    const paths = [...pendingPaths.current];
    pendingPaths.current.clear();
    if (paths.length === 0) return;

    inFlight.current?.abort();
    const controller = new AbortController();
    inFlight.current = controller;
    setChecking(true);

    try {
      const response = await api.validate({
        figureCode: template.figureCode,
        values: latestValues.current,
        changedPaths: paths,
        assetId: assetId ?? undefined,
        scope: 'field',
        culture,
      }, controller.signal);

      setServerMessages((previous) => {
        const next = { ...previous };
        // Clear every path this pass is authoritative about — the attributes it checked and the
        // targets of the rules it looked at — then add back only what it found. A pass answers
        // about paths other than the one that changed, so clearing just the requested paths let
        // each edit stack another copy of the same finding.
        for (const path of response.evaluatedPaths) delete next[path];
        for (const path of paths) delete next[path];
        for (const message of response.messages) {
          (next[message.path] ??= []).push(message);
        }
        return next;
      });
    } catch (error) {
      // A cancelled request is the normal case while typing; anything else is left to the
      // save, which validates in full and reports properly.
      if ((error as Error)?.name !== 'AbortError') {
        console.warn('Background validation failed', error);
      }
    } finally {
      if (inFlight.current === controller) {
        inFlight.current = null;
        setChecking(false);
      }
    }
  }, [template.figureCode, assetId, culture]);

  const scheduleServerValidation = useCallback((path: string) => {
    pendingPaths.current.add(path);
    if (debounceTimer.current) clearTimeout(debounceTimer.current);
    debounceTimer.current = setTimeout(() => { void runServerValidation(); }, SERVER_DEBOUNCE_MS);
  }, [runServerValidation]);

  useEffect(() => () => {
    if (debounceTimer.current) clearTimeout(debounceTimer.current);
    inFlight.current?.abort();
  }, []);

  // ----- editing ------------------------------------------------------------------

  const setValue = useCallback((path: string, value: Json) => {
    setValues((previous) => applyComputed(template, writePath(previous, path, value)));
    setTouched((previous) => new Set(previous).add(path));
    setConflict(null);
    scheduleServerValidation(path);
  }, [template, scheduleServerValidation]);

  const touch = useCallback((path: string) => {
    setTouched((previous) => (previous.has(path) ? previous : new Set(previous).add(path)));
  }, []);

  const addRow = useCallback((section: TemplateSection) => {
    setValues((previous) => {
      const rows = Array.isArray(previous[section.key]) ? [...(previous[section.key] as Json[])] : [];
      rows.push(emptyRow(section));
      return applyComputed(template, { ...previous, [section.key]: rows });
    });
    scheduleServerValidation(section.key);
  }, [template, scheduleServerValidation]);

  const removeRow = useCallback((section: TemplateSection, index: number) => {
    setValues((previous) => {
      const rows = Array.isArray(previous[section.key]) ? [...(previous[section.key] as Json[])] : [];
      rows.splice(index, 1);
      return applyComputed(template, { ...previous, [section.key]: rows });
    });
    // Row indices shift on removal, so messages pinned to this section are no longer trustworthy.
    setServerMessages((previous) => {
      const next: Record<string, ValidationMessage[]> = {};
      for (const [path, messages] of Object.entries(previous)) {
        if (sectionOf(path) !== section.key) next[path] = messages;
      }
      return next;
    });
    scheduleServerValidation(section.key);
  }, [template, scheduleServerValidation]);

  // ----- merged view --------------------------------------------------------------

  const allMessages = useMemo(() => {
    const server = Object.values(serverMessages).flat();
    // The API's answer first, so that when a rule marked "both" is reported by the browser and
    // the API alike, the one that survives is the authoritative one.
    const merged = deduplicate([...server, ...clientMessages]);
    if (showAll) return merged;
    // Before the first save attempt, only speak about attributes the user has visited —
    // a blank form covered in red is noise, not feedback.
    return merged.filter((m) => m.origin !== 'field' || touched.has(m.path) || m.severity !== 'error');
  }, [clientMessages, serverMessages, showAll, touched]);

  const byPath = useMemo(() => {
    const map = new Map<string, ValidationMessage[]>();
    for (const message of allMessages) {
      const list = map.get(message.path);
      if (list) list.push(message);
      else map.set(message.path, [message]);
    }
    return map;
  }, [allMessages]);

  const messagesFor = useCallback((path: string) => byPath.get(path) ?? [], [byPath]);

  const messagesInSection = useCallback((sectionKey: string) =>
    allMessages.filter((m) => sectionOf(m.path) === sectionKey), [allMessages]);

  const errorCount = useMemo(() => allMessages.filter((m) => m.severity === 'error').length, [allMessages]);
  const warningCount = useMemo(() => allMessages.filter((m) => m.severity === 'warning').length, [allMessages]);

  // ----- save ---------------------------------------------------------------------

  const submit = useCallback(async (acceptWarnings = false) => {
    setShowAll(true);
    setSaving(true);
    setSaveError(null);
    setConflict(null);

    try {
      const response = await api.saveAsset(assetId ?? null, {
        figureCode: template.figureCode,
        values: latestValues.current,
        rowVersion,
        acceptWarnings,
        culture,
      });

      if (response.conflict) {
        setConflict(response.conflict);
        return false;
      }

      if (!response.saved) {
        // The API is the authority: replace the server-side view with what it just reported.
        const grouped: Record<string, ValidationMessage[]> = {};
        for (const message of response.messages) (grouped[message.path] ??= []).push(message);
        setServerMessages(grouped);
        return false;
      }

      setRowVersion(response.rowVersion);
      setServerMessages({});
      if (response.assetId) options.onSaved?.(response.assetId);
      return true;
    } catch (error) {
      setSaveError((error as Error).message);
      return false;
    } finally {
      setSaving(false);
    }
  }, [assetId, template.figureCode, rowVersion, culture, options]);

  return {
    values,
    setValue,
    touch,
    addRow,
    removeRow,
    messagesFor,
    messagesInSection,
    allMessages,
    errorCount,
    warningCount,
    checking,
    saving,
    saveError,
    conflict,
    showAll,
    submit,
  };
}

export { readPath };
