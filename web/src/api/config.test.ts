import { describe, expect, it } from 'vitest';
import { pickApiBaseUrl } from './config';

/**
 * The hosted copies of the app — the container image and the GitHub Pages build — are the same
 * bundle pointed at different APIs, so the order these sources are consulted in is the whole
 * of that behaviour.
 */
describe('pickApiBaseUrl', () => {
  it('falls back to /api on this origin, which both dev and nginx proxy', () => {
    expect(pickApiBaseUrl({})).toBe('/api');
  });

  it('prefers the URL over everything, so a visitor can redirect the hosted page', () => {
    expect(pickApiBaseUrl({
      query: 'https://coe.example/api',
      stored: 'http://localhost:5080/api',
      runtime: '/api',
      built: '/built',
    })).toBe('https://coe.example/api');
  });

  it('uses what a previous visit remembered before the deployed default', () => {
    expect(pickApiBaseUrl({ stored: 'http://localhost:5080/api', runtime: '/api' }))
      .toBe('http://localhost:5080/api');
  });

  it('takes the deployed config.js over the value baked in at build time', () => {
    expect(pickApiBaseUrl({ runtime: 'https://coe.example/api', built: '/api' }))
      .toBe('https://coe.example/api');
  });

  it('ignores blank values rather than calling an empty host', () => {
    expect(pickApiBaseUrl({ query: '   ', stored: null, runtime: '', built: '/api' })).toBe('/api');
  });

  it('drops a trailing slash, which would double up against the paths the client appends', () => {
    expect(pickApiBaseUrl({ runtime: 'https://coe.example/api/' })).toBe('https://coe.example/api');
  });
});
