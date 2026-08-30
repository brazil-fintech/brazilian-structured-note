import { useCallback, useEffect, useState } from 'react';
import { api, type AssetListItem, type FigureSummary } from './api/client';
import { AssetForm } from './components/AssetForm';
import { AssetList } from './components/AssetList';
import { FigurePicker } from './components/FigurePicker';
import type { FigureTemplate, InstanceValues } from './engine/types';
import { ui } from './engine/texts';

type View =
  | { kind: 'list' }
  | { kind: 'pick' }
  | { kind: 'edit'; template: FigureTemplate; assetId?: string; values?: InstanceValues; rowVersion?: string };

export default function App() {
  const [culture, setCulture] = useState<'pt-BR' | 'en-GB'>('pt-BR');
  const [figures, setFigures] = useState<FigureSummary[]>([]);
  const [view, setView] = useState<View>({ kind: 'list' });
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    api.listFigures()
      .then(setFigures)
      .catch((e: Error) => setError(e.message));
  }, []);

  const openNew = useCallback(async (figure: FigureSummary) => {
    setBusy(true);
    setError(null);
    try {
      const template = await api.getTemplate(figure.code);
      setView({ kind: 'edit', template });
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }, []);

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
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
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
        {error && <div className="banner banner--error">{error}</div>}
        {busy && <div className="banner">{ui.loading(culture)}</div>}

        {view.kind === 'list' && (
          <AssetList
            culture={culture}
            figures={figures}
            onCreate={() => setView({ kind: 'pick' })}
            onEdit={(asset) => { void openExisting(asset); }}
          />
        )}

        {view.kind === 'pick' && (
          <FigurePicker
            figures={figures}
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
            onSaved={() => setView({ kind: 'list' })}
          />
        )}
      </main>
    </div>
  );
}
