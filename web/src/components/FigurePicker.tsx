import { useMemo, useState } from 'react';
import type { FigureCatalogueEntry } from '../api/client';
import { ui } from '../engine/texts';

interface Props {
  figures: FigureCatalogueEntry[];
  coverage: { published: number; configured: number; bookable: number };
  culture: string;
  onPick: (figure: FigureCatalogueEntry) => void;
  onCancel: () => void;
}

/**
 * Step one of booking: pick the figure.
 *
 * The list is B3's whole catalogue, not only the figures this platform can book. A figure with
 * no compiled template is still shown — greyed out, unclickable, and labelled — because the
 * alternative is a screen that silently omits figures the desk can see in B3's own catalogue and
 * gives no way to tell "not offered here" from "does not exist".
 */
export function FigurePicker({ figures, coverage, culture, onPick, onCancel }: Props) {
  const [query, setQuery] = useState('');
  const [bookableOnly, setBookableOnly] = useState(false);

  const filtered = useMemo(() => {
    const term = query.trim().toLowerCase();
    return figures.filter((figure) => {
      if (bookableOnly && !figure.bookable) return false;
      if (!term) return true;
      return (
        figure.code.toLowerCase().includes(term) ||
        figure.name.toLowerCase().includes(term) ||
        (figure.b3Name ?? '').toLowerCase().includes(term) ||
        (figure.commercialName ?? '').toLowerCase().includes(term)
      );
    });
  }, [figures, query, bookableOnly]);

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

      <div className="picker__controls">
        <input
          type="search"
          className="field__input picker__search"
          placeholder={`${ui.chooseFigure(culture)}…`}
          value={query}
          onChange={(e) => setQuery(e.target.value)}
        />
        <label className="picker__toggle">
          <input
            type="checkbox"
            checked={bookableOnly}
            onChange={(e) => setBookableOnly(e.target.checked)}
          />
          {ui.bookableOnly(culture)}
        </label>
        <span className="picker__coverage">
          {ui.figureCoverage(culture, coverage.bookable, coverage.published)}
        </span>
      </div>

      <div className="picker__grid">
        {filtered.map((figure) => (
          <button
            key={figure.code}
            type="button"
            className={`figure-card${figure.bookable ? '' : ' figure-card--unavailable'}`}
            disabled={!figure.bookable}
            title={figure.bookable ? undefined : ui.availability(culture, figure.availability)}
            onClick={() => onPick(figure)}
          >
            <div className="figure-card__head">
              <span className="badge">{figure.code}</span>
              {figure.modalities.map((modality) => (
                <span key={modality} className={`chip chip--${modality.toLowerCase()}`}>{modality}</span>
              ))}
              {!figure.bookable && (
                <span className="chip chip--muted">{ui.availability(culture, figure.availability)}</span>
              )}
            </div>
            {/* B3's registered name names the card; a house label is secondary. */}
            <div className="figure-card__title">{figure.b3Name ?? figure.name}</div>
            {figure.commercialName && figure.commercialName !== (figure.b3Name ?? figure.name) && (
              <div className="figure-card__subtitle">{figure.commercialName}</div>
            )}
            {figure.bookable && figure.description && (
              <p className="figure-card__description">{figure.description}</p>
            )}
            {!figure.bookable && (
              <p className="figure-card__description">
                {figure.lastError
                  ? ui.figureQuarantined(culture)
                  : ui.figureNotConfigured(culture)}
              </p>
            )}
          </button>
        ))}
      </div>
    </div>
  );
}
