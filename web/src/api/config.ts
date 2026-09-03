/**
 * Where the app sends its calls.
 *
 * Decided when the page loads rather than when the bundle is built, so one published copy —
 * the container image, the GitHub Pages copy — serves a desk running the API next door and a
 * desk running it somewhere else. In order: `?api=` on the URL (remembered afterwards, which
 * is how a visitor points the hosted page at their own instance), what a previous visit
 * remembered, the `config.js` the container writes, the build-time `VITE_API_BASE_URL`, and
 * finally `/api` on this origin — what the dev server proxies and what nginx proxies.
 *
 * The last one is a fallback, not a configuration: on a static copy such as GitHub Pages there
 * is no API on the page's own origin, so `apiBaseUrlSource` says which of the five was used and
 * the screen can tell an unconfigured page apart from an API that is merely down.
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

/** Which of the sources above answered — `fallback` meaning none of them did. */
export type ApiBaseUrlSource = keyof ApiBaseUrlSources | 'fallback';

export interface ResolvedApiBaseUrl {
  url: string;
  source: ApiBaseUrlSource;
}

const FALLBACK = '/api';

export function resolveApiBaseUrl(sources: ApiBaseUrlSources): ResolvedApiBaseUrl {
  const order: [ApiBaseUrlSource, string | null | undefined][] = [
    ['query', sources.query],
    ['stored', sources.stored],
    ['runtime', sources.runtime],
    ['built', sources.built],
  ];

  for (const [source, candidate] of order) {
    const value = candidate?.trim();
    // A trailing slash would double up against the paths the client appends.
    if (value) return { url: value.replace(/\/+$/, ''), source };
  }

  return { url: FALLBACK, source: 'fallback' };
}

export function pickApiBaseUrl(sources: ApiBaseUrlSources): string {
  return resolveApiBaseUrl(sources).url;
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

/**
 * Points this browser at an API from here on. Takes effect on reload rather than in place:
 * `apiBaseUrl` is read once at module load, and half the screen holding data fetched from the
 * old base would be worse than a reload.
 */
export function rememberApiBaseUrl(value: string): void {
  remember(value.trim().replace(/\/+$/, ''));
}

/** Forgets a base URL a visitor set, so the deployed default applies again on the next load. */
export function forgetApiBaseUrl(): void {
  try {
    window.localStorage.removeItem(API_BASE_URL_STORAGE_KEY);
  } catch {
    // Nothing remembered it in the first place.
  }
}

function resolve(): ResolvedApiBaseUrl {
  if (typeof window === 'undefined') return { url: FALLBACK, source: 'fallback' };

  const query = fromQuery();
  const chosen = resolveApiBaseUrl({
    query,
    stored: remembered(),
    runtime: window.__COE_CONFIG__?.apiBaseUrl,
    built: import.meta.env.VITE_API_BASE_URL as string | undefined,
  });

  if (query?.trim()) remember(chosen.url);
  return chosen;
}

const resolved = resolve();

/** The base every call in `client.ts` is written against. */
export const apiBaseUrl: string = resolved.url;

/** Which source it came from; `fallback` means nothing pointed this page at an API. */
export const apiBaseUrlSource: ApiBaseUrlSource = resolved.source;
