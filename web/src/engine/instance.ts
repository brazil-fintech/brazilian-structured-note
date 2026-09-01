import { evaluate, evaluateAsBool, withItem, type EvalContext } from './evaluate';
import type { FigureTemplate, InstanceValues, Json, TemplateSection } from './types';

/**
 * Building and maintaining the instance document the template describes: seeding defaults,
 * recomputing derived attributes, and adding rows to repeating sections.
 *
 * `applyComputed` mirrors `Coe.Core.Validation.ComputedFields`. The client runs it so a
 * derived value (the VFE, say) appears the moment its inputs change; the API runs it again
 * before saving, so the stored value never disagrees with its inputs.
 */

export function buildDefaults(template: FigureTemplate): InstanceValues {
  const values: InstanceValues = {};

  for (const section of template.sections) {
    if (section.repeating) {
      values[section.key] = [];
      continue;
    }
    const block: Record<string, Json> = {};
    for (const field of section.fields) {
      if (field.default !== undefined && field.default !== null) block[field.key] = field.default;
    }
    values[section.key] = block;
  }

  return applyComputed(template, values);
}

export function emptyRow(section: TemplateSection): Record<string, Json> {
  const row: Record<string, Json> = {};
  for (const field of section.itemFields) {
    if (field.default !== undefined && field.default !== null) row[field.key] = field.default;
  }
  return row;
}

export function applyComputed(
  template: FigureTemplate,
  values: InstanceValues,
  variables?: Record<string, unknown>,
): InstanceValues {
  const next: InstanceValues = { ...values };
  const ctx: EvalContext = { root: next, variables };

  for (const section of template.sections) {
    if (section.repeating) {
      const rows = next[section.key];
      if (!Array.isArray(rows)) continue;
      const computedColumns = section.itemFields.filter((f) => f.computed);
      if (computedColumns.length === 0) continue;

      next[section.key] = rows.map((row) => {
        const item = { ...(row as Record<string, Json>) };
        const scoped = withItem(ctx, item);
        for (const field of computedColumns) {
          item[field.key] = toJson(evaluate(field.computed!, scoped));
        }
        return item;
      });
      continue;
    }

    const computedFields = section.fields.filter((f) => f.computed);
    if (computedFields.length === 0) continue;

    const block = { ...((next[section.key] as Record<string, Json> | undefined) ?? {}) };
    for (const field of computedFields) {
      block[field.key] = toJson(evaluate(field.computed!, ctx));
    }
    next[section.key] = block;
  }

  return next;
}

/** Sections currently shown, in display order: the common blocks first, then the tabs. */
export function visibleSections(template: FigureTemplate, values: InstanceValues): TemplateSection[] {
  const ctx: EvalContext = { root: values };
  return template.sections.filter((section) => evaluateAsBool(section.visibleWhen, ctx));
}

function toJson(value: unknown): Json {
  if (value === null || value === undefined) return null;
  if (typeof value === 'number' || typeof value === 'string' || typeof value === 'boolean') return value;
  return String(value);
}
