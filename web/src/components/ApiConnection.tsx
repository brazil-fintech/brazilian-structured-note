import { useState } from 'react';
import { apiBaseUrl, apiBaseUrlSource, forgetApiBaseUrl, rememberApiBaseUrl } from '../api/config';
import { ui } from '../engine/texts';

interface Props {
  culture: string;
}

/**
 * Shown when the first calls of a page load never reached an API.
 *
 * The published copy on GitHub Pages is the screen and nothing else, so this is the one failure
 * a visitor can fix from the page itself: the base URL is a setting, and typing one here is the
 * same thing as arriving with `?api=` on the URL. A reload is what applies it — the client reads
 * the base once, at module load, and half a screen filled from the old base would be worse.
 */
export function ApiConnection({ culture }: Props) {
  const [value, setValue] = useState(apiBaseUrlSource === 'fallback' ? '' : apiBaseUrl);
  const unconfigured = apiBaseUrlSource === 'fallback';

  const connect = () => {
    if (!value.trim()) return;
    rememberApiBaseUrl(value);
    window.location.reload();
  };

  return (
    <div className="banner banner--error api-connection">
      <p className="api-connection__title">
        {unconfigured ? ui.apiUnconfiguredTitle(culture) : ui.apiUnreachable(culture, apiBaseUrl)}
      </p>
      <p className="api-connection__help">
        {unconfigured ? ui.apiUnconfigured(culture) : ui.apiUnreachableHelp(culture)}
      </p>

      <form
        className="api-connection__form"
        onSubmit={(event) => { event.preventDefault(); connect(); }}
      >
        <label className="filter filter--grow">
          <span className="filter__label">{ui.apiBaseUrlLabel(culture)}</span>
          <input
            type="url"
            className="field__input"
            placeholder="https://host/api"
            value={value}
            onChange={(event) => setValue(event.target.value)}
          />
        </label>
        <button type="submit" className="button button--primary" disabled={!value.trim()}>
          {ui.apiConnect(culture)}
        </button>
        <button type="button" className="button button--ghost" onClick={() => window.location.reload()}>
          {ui.apiRetry(culture)}
        </button>
        {/* Only worth offering once something is remembered — otherwise it is already the default. */}
        {apiBaseUrlSource === 'stored' && (
          <button
            type="button"
            className="button button--ghost"
            onClick={() => { forgetApiBaseUrl(); window.location.reload(); }}
          >
            {ui.apiUseDefault(culture)}
          </button>
        )}
      </form>
    </div>
  );
}
