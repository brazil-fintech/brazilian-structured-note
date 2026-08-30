import { useMemo, useState } from 'react';
import type { FigureSummary } from '../api/client';
import { ui } from '../engine/texts';

interface Props {
  figures: FigureSummary[];
  culture: string;
  onPick: (figure: FigureSummary) => void;
  onCancel: () => void;
}

/**
 * Step one of booking: pick the figure. The list is whatever the ingestion worker has enabled,
 * so a figure B3 publishes shows up here without a front-end release.
 */
export function FigurePicker({ figures, culture, onPick, onCancel }: Props) {
  const [query, setQuery] = useState('');

  const filtered = useMemo(() => {
    const term = query.trim().toLowerCase();
    if (!term) return figures;
    return figures.filter((figure) =>
      figure.code.toLowerCase().includes(term) ||
      figure.name.toLowerCase().includes(term) ||
      (figure.commercialName ?? '').toLowerCase().includes(term));
  }, [figures, query]);

  return (
    <div className="picker">
      <header className="list__header">
        <div>
          <h2 className="list__title">{ui.chooseFigure(culture)}</h2>
          <p className="picker__help">{ui.chooseFigureHelp(culture)}</p>
        </div>
        <button type="button" className="button button--ghost" onClick={onCancel}>
          {ui.cancel(culture)}
        </button>
      </header>

      <input
        type="search"
        className="field__input picker__search"
        placeholder={`${ui.chooseFigure(culture)}…`}
        value={query}
        onChange={(e) => setQuery(e.target.value)}
      />

      <div className="picker__grid">
        {filtered.map((figure) => (
          <button key={figure.code} type="button" className="figure-card" onClick={() => onPick(figure)}>
            <div className="figure-card__head">
              <span className="badge">{figure.code}</span>
              {figure.modalities.map((modality) => (
                <span key={modality} className={`chip chip--${modality.toLowerCase()}`}>{modality}</span>
              ))}
            </div>
            <div className="figure-card__title">{figure.commercialName ?? figure.name}</div>
            <div className="figure-card__subtitle">{figure.name}</div>
            {figure.description && <p className="figure-card__description">{figure.description}</p>}
          </button>
        ))}
      </div>
    </div>
  );
}
