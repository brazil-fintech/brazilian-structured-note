import { useEffect, useState } from 'react';
import { api, ApiUnreachableError, type AssetListItem, type FigureSummary } from '../api/client';
import { ui } from '../engine/texts';

interface Props {
  culture: string;
  figures: FigureSummary[];
  onCreate: () => void;
  onEdit: (asset: AssetListItem) => void;
  /** Opens the CETIP upload files this asset produces. */
  onFiles: (asset: AssetListItem) => void;
  /**
   * The list reached no API at all. Said upwards rather than shown here: the page answers that
   * one with the form that names an API, and two banners repeating "failed to fetch" only make
   * the screen look broken twice.
   */
  onUnreachable?: () => void;
}

/**
 * The landing screen. The reference-date filter is the primary one: an asset is listed when
 * it is live on that date, i.e. issued on or before it and not yet matured.
 */
export function AssetList({ culture, figures, onCreate, onEdit, onFiles, onUnreachable }: Props) {
  const [referenceDate, setReferenceDate] = useState(() => new Date().toISOString().slice(0, 10));
  const [figureCode, setFigureCode] = useState('');
  const [search, setSearch] = useState('');
  const [items, setItems] = useState<AssetListItem[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);

    // Typing in the search box should not fire a request per keystroke.
    const timer = setTimeout(() => {
      api.listAssets({ referenceDate, figureCode: figureCode || undefined, search: search || undefined })
        .then((response) => {
          if (cancelled) return;
          setItems(response.items);
          setTotal(response.total);
        })
        .catch((e: Error) => {
          if (cancelled) return;
          if (e instanceof ApiUnreachableError) { onUnreachable?.(); return; }
          setError(e.message);
        })
        .finally(() => { if (!cancelled) setLoading(false); });
    }, 250);

    return () => { cancelled = true; clearTimeout(timer); };
  }, [referenceDate, figureCode, search, onUnreachable]);

  return (
    <div className="list">
      <header className="list__header">
        <h2 className="list__title">{ui.assets(culture)}</h2>
        <button type="button" className="button button--primary" onClick={onCreate}>
          + {ui.newAsset(culture)}
        </button>
      </header>

      <div className="filters">
        <label className="filter">
          <span className="filter__label">{ui.referenceDate(culture)}</span>
          <input
            type="date"
            className="field__input"
            value={referenceDate}
            onChange={(e) => setReferenceDate(e.target.value)}
          />
        </label>

        <label className="filter">
          <span className="filter__label">{ui.figure(culture)}</span>
          <select className="field__input" value={figureCode} onChange={(e) => setFigureCode(e.target.value)}>
            <option value="">{ui.allFigures(culture)}</option>
            {figures.map((figure) => (
              <option key={figure.code} value={figure.code}>
                {figure.commercialName ?? figure.name} ({figure.code})
              </option>
            ))}
          </select>
        </label>

        <label className="filter filter--grow">
          <span className="filter__label">{ui.search(culture)}</span>
          <input
            type="search"
            className="field__input"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
        </label>
      </div>

      {error && <div className="banner banner--error">{error}</div>}

      <div className="table-wrap">
        <table className="table">
          <thead>
            <tr>
              <th>{ui.name(culture)}</th>
              <th>{ui.figure(culture)}</th>
              <th>{ui.underlying(culture)}</th>
              <th>{ui.modality(culture)}</th>
              <th>{ui.issue(culture)}</th>
              <th>{ui.maturity(culture)}</th>
              <th className="table__number">{ui.notional(culture)}</th>
              <th>{ui.status(culture)}</th>
              <th aria-label="actions" />
            </tr>
          </thead>
          <tbody>
            {loading && (
              <tr><td colSpan={9} className="table__empty">{ui.loading(culture)}</td></tr>
            )}
            {!loading && items.length === 0 && (
              <tr><td colSpan={9} className="table__empty">{ui.noAssets(culture)}</td></tr>
            )}
            {!loading && items.map((asset) => (
              <tr key={asset.id}>
                <td>
                  <div className="table__primary">{asset.commercialName}</div>
                  <div className="table__secondary">{asset.instrumentCode ?? asset.isinCode ?? '—'}</div>
                </td>
                <td>
                  <div className="table__primary">{asset.figureName ?? asset.figureCode}</div>
                  <div className="table__secondary">{asset.figureCode}</div>
                </td>
                <td>{asset.underlying ?? asset.underlyingClass ?? '—'}</td>
                <td>{asset.modality ?? '—'}</td>
                <td>{asset.issueDate}</td>
                <td>{asset.maturityDate}</td>
                <td className="table__number">{formatMoney(asset.notionalAmount, culture)}</td>
                <td><span className={`status status--${asset.status.toLowerCase()}`}>{asset.status}</span></td>
                <td className="table__actions">
                  <button type="button" className="button button--ghost" onClick={() => onEdit(asset)}>
                    {ui.edit(culture)}
                  </button>
                  <button type="button" className="button button--ghost" onClick={() => onFiles(asset)}>
                    {ui.b3Files(culture)}
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <p className="list__footer">{total} {ui.assets(culture).toLowerCase()}</p>
    </div>
  );
}

function formatMoney(value: number | undefined, culture: string): string {
  if (value === undefined || value === null) return '—';
  return new Intl.NumberFormat(culture.startsWith('en') ? 'en-US' : 'pt-BR', {
    style: 'currency',
    currency: 'BRL',
    maximumFractionDigits: 2,
  }).format(value);
}
