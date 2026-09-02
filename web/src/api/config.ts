/**
 * Where the app sends its calls.
 *
 * Decided when the page loads rather than when the bundle is built, so one published copy —
 * the container image, the GitHub Pages copy — serves a desk running the API next door and a
 * desk running it somewhere else. In order: `?api=` on the URL (remembered afterwards, which
 * is how a visitor points the hosted page at their own instance), what a previous visit
 * remembered, the `config.js` the container writes, the build-time `VITE_API_BASE_URL`, and
 * finally `/api` on this origin — what the dev server proxies and what nginx proxies.
 */

export interface CoeRuntimeConfig {
  apiBaseUrl?: string;
}

declare global {
  interface Window {
    __COE_CONFIG__?: CoeRuntimeConfig;
  }
}

export const API_BASE_URL_STORAGE_KEY = 'coe.apiBaseUrl';

export interface ApiBaseUrlSources {
  /** `?api=` on the current URL. */
  query?: string | null;
  /** What a previous visit remembered. */
  stored?: string | null;
  /** `window.__COE_CONFIG__.apiBaseUrl`, written into config.js at deploy time. */
  runtime?: string | null;
  /** `VITE_API_BASE_URL`, baked in at build time. */
  built?: string | null;
}

export function pickApiBaseUrl(sources: ApiBaseUrlSources): string {
  for (const candidate of [sources.query, sources.stored, sources.runtime, sources.built]) {
    const value = candidate?.trim();
    // A trailing slash would double up against the paths the client appends.
    if (value) return value.replace(/\/+$/, '');
  }
  return '/api';
}

function fromQuery(): string | null {
  if (typeof window === 'undefined') return null;
  return new URLSearchParams(window.location.search).get('api');
}

function remembered(): string | null {
  // Private browsing and blocked site data both throw rather than return nothing.
  try {
    return window.localStorage.getItem(API_BASE_URL_STORAGE_KEY);
  } catch {
    return null;
  }
}

function remember(value: string): void {
  try {
    window.localStorage.setItem(API_BASE_URL_STORAGE_KEY, value);
  } catch {
    // Nothing to do: the URL still carries the choice for this visit.
  }
}

function resolve(): string {
  if (typeof window === 'undefined') return '/api';

  const query = fromQuery();
  const chosen = pickApiBaseUrl({
    query,
    stored: remembered(),
    runtime: window.__COE_CONFIG__?.apiBaseUrl,
    built: import.meta.env.VITE_API_BASE_URL as string | undefined,
  });

  if (query?.trim()) remember(chosen);
  return chosen;
}

/** The base every call in `client.ts` is written against. */
export const apiBaseUrl: string = resolve();
