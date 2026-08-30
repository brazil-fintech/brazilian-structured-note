import type { InstanceValues, Json, TemplateField } from './types';

/** Path arithmetic mirroring `Coe.Core.Validation.Instance`. */

/** Concrete path of a field; `prefix` is the row (`cashflows[2]`) inside a repeating section. */
export function pathFor(field: TemplateField, prefix?: string | null): string {
  return prefix ? `${prefix}.${field.key}` : field.path;
}

/** Replaces every row index with `[]`, so a concrete path matches a template path. */
export function normalize(path: string): string {
  return path.replace(/\[\d+\]/g, '[]');
}

/** Rewrites a rule target into a concrete instance path. */
export function resolveTarget(target: string, prefix?: string | null): string {
  if (!target) return prefix ?? '';
  if (!prefix) return target;

  const bracket = prefix.indexOf('[');
  const sectionKey = bracket < 0 ? prefix : prefix.slice(0, bracket);
  const generic = `${sectionKey}[]`;

  if (target.startsWith(generic)) return prefix + target.slice(generic.length);
  return target.includes('.') ? target : `${prefix}.${target}`;
}

export function sectionOf(path: string): string {
  const match = /^[^.[]+/.exec(path);
  return match ? match[0] : path;
}

/** Reads a value at `section.key` or `section[i].key`. */
export function readPath(values: InstanceValues, path: string): Json | undefined {
  let node: unknown = values;
  for (const segment of splitPath(path)) {
    if (node === null || node === undefined) return undefined;
    node = (node as Record<string, unknown>)[segment];
  }
  return node as Json | undefined;
}

/** Writes a value at `section.key` or `section[i].key`, creating containers as needed. */
export function writePath(values: InstanceValues, path: string, value: Json): InstanceValues {
  const segments = splitPath(path);
  const next: InstanceValues = { ...values };

  let cursor: Record<string, unknown> | unknown[] = next;
  for (let i = 0; i < segments.length - 1; i++) {
    const segment = segments[i];
    const isIndex = /^\d+$/.test(segments[i + 1]);
    const container = cursor as Record<string, unknown>;
    const existing = container[segment];

    const clone: unknown = Array.isArray(existing)
      ? [...existing]
      : existing && typeof existing === 'object'
        ? { ...(existing as Record<string, unknown>) }
        : isIndex ? [] : {};

    container[segment] = clone;
    cursor = clone as Record<string, unknown>;
  }

  (cursor as Record<string, unknown>)[segments[segments.length - 1]] = value;
  return next;
}

/** `cashflows[2].amount` becomes `['cashflows', '2', 'amount']`. */
export function splitPath(path: string): string[] {
  return path
    .replace(/\[(\d+)\]/g, '.$1')
    .split('.')
    .filter((segment) => segment.length > 0);
}
