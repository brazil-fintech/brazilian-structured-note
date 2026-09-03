import { describe, expect, it } from 'vitest';
import { ApiUnreachableError, asFetchFailure, clearingPath } from './client';

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

/**
 * `fetch` rejects with the same bare TypeError whether the host refused the connection, the name
 * did not resolve or the API declined the origin — and rejects the same way again when the caller
 * aborted on purpose. Telling those two apart is the whole point: one is a screen that cannot
 * work until it is pointed at an API, the other is a validation pass overtaken by the next
 * keystroke, which happens on every fast typist and means nothing.
 */
describe('asFetchFailure', () => {
  const base = 'https://coe.example/api';

  it('turns a network failure into an error that carries the base URL it tried', () => {
    const failure = asFetchFailure(base, new TypeError('Failed to fetch'));
    expect(failure).toBeInstanceOf(ApiUnreachableError);
    expect((failure as ApiUnreachableError).baseUrl).toBe(base);
    expect((failure as ApiUnreachableError).message).toContain(base);
  });

  it('leaves a deliberate abort alone', () => {
    const abort = new DOMException('The operation was aborted.', 'AbortError');
    expect(asFetchFailure(base, abort)).toBe(abort);
  });

  it('leaves an abort alone where DOMException is not what was thrown', () => {
    // Node's fetch rejects with an Error named AbortError rather than a DOMException.
    const abort = Object.assign(new Error('This operation was aborted'), { name: 'AbortError' });
    expect(asFetchFailure(base, abort)).toBe(abort);
  });
});
