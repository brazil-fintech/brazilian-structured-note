import { describe, expect, it } from 'vitest';
import { clearingPath } from './client';

/**
 * The clearing routes are the one place the browser builds a URL the API answers with bytes
 * rather than JSON, and the same query has to reach both of them: a preview generated for one
 * participant and a download taken for another would be two different files on screen and on disk.
 */
describe('clearingPath', () => {
  const id = '3f4b1b6e-1c2f-4d33-9a5a-1a2b3c4d5e6f';

  it('asks for the whole set when no operation is named', () => {
    expect(clearingPath(id)).toBe(`/assets/${id}/clearing`);
  });

  it('addresses one file by its operation code', () => {
    expect(clearingPath(id, {}, '0001')).toBe(`/assets/${id}/clearing/0001`);
  });

  it('carries the participant and the file date', () => {
    expect(clearingPath(id, { participant: 'BANCO XYZ', date: '2026-09-02' }, 'FLUX'))
      .toBe(`/assets/${id}/clearing/FLUX?participant=BANCO+XYZ&date=2026-09-02`);
  });

  it('omits a participant that is blank, so the server-configured one is used', () => {
    expect(clearingPath(id, { participant: '   ', date: '2026-09-02' }))
      .toBe(`/assets/${id}/clearing?date=2026-09-02`);
  });
});
