import type { Json } from './types';

/**
 * Coercion rules mirroring `Coe.Core.Expressions.Values`.
 *
 * One deliberate difference: the server compares with `decimal`, the browser with IEEE
 * doubles, so numeric equality here uses a small tolerance. Without it a basket of three
 * 33.33% weights would sum to 99.99000000000001 in the browser and light up an error the
 * server would not raise. The server stays the authority; this only keeps the live feedback
 * from lying.
 */
const EPSILON = 1e-9;

const ISO_DATE = /^\d{4}-\d{2}-\d{2}$/;

export function isDateString(value: unknown): value is string {
  return typeof value === 'string' && ISO_DATE.test(value);
}

/** Empty strings count as absent, so "required" agrees with what the user sees. */
export function isAbsent(value: unknown): boolean {
  return value === null || value === undefined || value === '';
}

export function asNumber(value: unknown): number | null {
  if (value === null || value === undefined || value === '') return null;
  if (typeof value === 'number') return Number.isFinite(value) ? value : null;
  if (typeof value === 'boolean') return value ? 1 : 0;
  if (isDateString(value)) return dayNumber(value);
  if (typeof value === 'string') {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : null;
  }
  return null;
}

export function asBool(value: unknown): boolean | null {
  if (typeof value === 'boolean') return value;
  if (typeof value === 'number') return value !== 0;
  if (value === 'true') return true;
  if (value === 'false') return false;
  return null;
}

/** Truthiness for `and`/`or`/`not` and rule guards: null is false. */
export function truthy(value: unknown): boolean {
  return asBool(value) ?? false;
}

export function asString(value: unknown): string | null {
  if (value === null || value === undefined) return null;
  if (typeof value === 'string') return value;
  if (typeof value === 'boolean') return value ? 'true' : 'false';
  if (typeof value === 'number') return String(value);
  return String(value);
}

export function asList(value: unknown): Json[] {
  return Array.isArray(value) ? (value as Json[]) : [];
}

/** Days since the epoch for an ISO date, used for date arithmetic and comparison. */
export function dayNumber(iso: string): number {
  return Math.round(Date.parse(`${iso}T00:00:00Z`) / 86_400_000);
}

export function fromDayNumber(days: number): string {
  return new Date(days * 86_400_000).toISOString().slice(0, 10);
}

export function addDays(iso: string, days: number): string {
  return fromDayNumber(dayNumber(iso) + days);
}

/** Returns null when the operands are not comparable, which makes the comparison undecided. */
export function compare(a: unknown, b: unknown): number | null {
  if (a === null || a === undefined || b === null || b === undefined) return null;

  if (isDateString(a) || isDateString(b)) {
    if (!isDateString(a) || !isDateString(b)) return null;
    return dayNumber(a) - dayNumber(b);
  }

  if (typeof a === 'string' && typeof b === 'string') {
    return a < b ? -1 : a > b ? 1 : 0;
  }

  const na = asNumber(a);
  const nb = asNumber(b);
  if (na === null || nb === null) return null;
  if (Math.abs(na - nb) < EPSILON) return 0;
  return na - nb;
}

export function equal(a: unknown, b: unknown): boolean {
  const aMissing = a === null || a === undefined;
  const bMissing = b === null || b === undefined;
  if (aMissing && bMissing) return true;
  if (aMissing || bMissing) return false;

  if (typeof a === 'boolean' || typeof b === 'boolean') {
    const ba = asBool(a);
    const bb = asBool(b);
    return ba !== null && bb !== null && ba === bb;
  }

  const cmp = compare(a, b);
  if (cmp !== null) return cmp === 0;
  return asString(a) === asString(b);
}

/** Number of decimal places, for the "registered with N decimals" hint. */
export function scaleOf(value: number): number {
  const text = String(value);
  const dot = text.indexOf('.');
  if (dot < 0) return 0;
  const exponent = text.indexOf('e');
  return (exponent < 0 ? text.length : exponent) - dot - 1;
}
