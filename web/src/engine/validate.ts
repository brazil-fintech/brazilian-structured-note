import { evaluate, evaluateAsBool, navigate, withItem, type EvalContext } from './evaluate';
import { pathFor, resolveTarget } from './paths';
import { localized, texts } from './texts';
import type {
  FigureTemplate, InstanceValues, Json, TemplateField, TemplateRule, TemplateSection, ValidationMessage,
} from './types';
import { asBool, asList, asNumber, asString, isAbsent, isDateString, scaleOf, truthy } from './values';

/**
 * The browser half of `Coe.Core.Validation.ValidationEngine`.
 *
 * It runs on every keystroke so the user gets an answer without a round trip, and it runs the
 * *same* rules the API will run — but only those marked `client` or `both`. Rules needing
 * reference data (`serverCheck`, `execution: server`) are left to the validate endpoint, and
 * the API re-runs everything on save regardless. Anything this misses, the save catches.
 */

export type Scope = 'field' | 'form' | 'submit';

export interface ValidateOptions {
  scope: Scope;
  /** Concrete paths the user just changed; omit to check everything. */
  changedPaths?: string[];
  culture?: string;
  variables?: Record<string, unknown>;
}

export interface ClientValidationResult {
  messages: ValidationMessage[];
  evaluatedPaths: string[];
}

export function validate(
  template: FigureTemplate,
  values: InstanceValues,
  options: ValidateOptions,
): ClientValidationResult {
  const culture = options.culture ?? 'pt-BR';
  const ctx: EvalContext = { root: values, variables: options.variables };
  const changed = options.changedPaths ? new Set(options.changedPaths) : null;

  const messages: ValidationMessage[] = [];
  const evaluatedPaths: string[] = [];

  for (const section of template.sections) {
    if (!evaluateAsBool(section.visibleWhen, ctx)) continue;

    if (section.repeating) {
      validateRepeating(section, values, ctx, options.scope, changed, culture, messages, evaluatedPaths);
    } else {
      validateFields(section, section.fields, ctx, null, options.scope, changed, culture, messages, evaluatedPaths);
    }
  }

  for (const rule of template.rules) {
    // A serverCheck has no expression to evaluate here; the validate endpoint answers it.
    if (rule.serverCheck || rule.execution === 'server') continue;
    if (options.scope !== 'submit' && rule.trigger === 'submit') continue;

    if (rule.forEachSection) {
      evaluateRowRule(template, rule, values, ctx, options.scope, changed, culture, messages);
    } else {
      evaluateRule(rule, ctx, null, options.scope, changed, culture, messages);
    }
  }

  return { messages: deduplicate(messages), evaluatedPaths };
}

// ----- fields ---------------------------------------------------------------------

function validateRepeating(
  section: TemplateSection,
  values: InstanceValues,
  ctx: EvalContext,
  scope: Scope,
  changed: Set<string> | null,
  culture: string,
  messages: ValidationMessage[],
  evaluatedPaths: string[],
): void {
  const rows = asList(navigate(values, section.key));

  if (scope !== 'field' || touched(changed, section.key)) {
    const label = localized(section.label, culture);
    if (section.minItems !== undefined && rows.length < section.minItems) {
      messages.push(fieldMessage(section, section.key, 'error',
        `${label}: ${texts.minItems(culture, section.minItems)}`));
    }
    if (section.maxItems !== undefined && rows.length > section.maxItems) {
      messages.push(fieldMessage(section, section.key, 'error',
        `${label}: ${texts.maxItems(culture, section.maxItems)}`));
    }
  }

  rows.forEach((row, index) => {
    const scoped = withItem(ctx, row as Record<string, Json>);
    validateFields(section, section.itemFields, scoped, `${section.key}[${index}]`, scope, changed, culture, messages, evaluatedPaths);
  });
}

function validateFields(
  section: TemplateSection,
  fields: TemplateField[],
  ctx: EvalContext,
  prefix: string | null,
  scope: Scope,
  changed: Set<string> | null,
  culture: string,
  messages: ValidationMessage[],
  evaluatedPaths: string[],
): void {
  for (const field of fields) {
    const path = pathFor(field, prefix);
    if (!evaluateAsBool(field.visibleWhen, ctx)) continue;

    if (scope === 'field' && !touched(changed, path) && !dependsOnChanged(field.dependsOn, changed)) continue;

    evaluatedPaths.push(path);

    const raw = prefix ? navigate(ctx.item, field.key) : navigate(ctx.root, field.path);
    const absent = isAbsent(raw) || (Array.isArray(raw) && raw.length === 0);

    if (absent) {
      const required = field.required === true ||
        (field.requiredWhen !== undefined && evaluateAsBool(field.requiredWhen, ctx));
      // "Fill this in" is only useful once the user has left the field or pressed save.
      if (required && scope !== 'form') {
        messages.push(fieldMessage(section, path, 'error', texts.required(culture, localized(field.label, culture))));
      }
      continue;
    }

    checkValue(section, field, path, raw, culture, messages);
  }
}

function checkValue(
  section: TemplateSection,
  field: TemplateField,
  path: string,
  raw: unknown,
  culture: string,
  messages: ValidationMessage[],
): void {
  const label = localized(field.label, culture);

  switch (field.dataType) {
    case 'integer':
    case 'decimal':
    case 'percent':
    case 'money': {
      const n = asNumber(raw);
      if (n === null) {
        messages.push(fieldMessage(section, path, 'error', texts.notANumber(culture, label)));
        return;
      }
      if (field.dataType === 'integer' && !Number.isInteger(n)) {
        messages.push(fieldMessage(section, path, 'error', texts.notAnInteger(culture, label)));
      }
      if (field.min !== undefined && n < field.min) {
        messages.push(fieldMessage(section, path, 'error', texts.min(culture, label, field.min)));
      }
      if (field.max !== undefined && n > field.max) {
        messages.push(fieldMessage(section, path, 'error', texts.max(culture, label, field.max)));
      }
      if (field.decimals !== undefined && scaleOf(n) > field.decimals) {
        messages.push(fieldMessage(section, path, 'warning', texts.decimals(culture, label, field.decimals)));
      }
      return;
    }

    case 'date':
      if (!isDateString(raw)) {
        messages.push(fieldMessage(section, path, 'error', texts.notADate(culture, label)));
      }
      return;

    case 'boolean':
      if (asBool(raw) === null) {
        messages.push(fieldMessage(section, path, 'error', texts.notABoolean(culture, label)));
      }
      return;

    case 'enum': {
      if (!field.options?.length) return;
      const code = asString(raw);
      if (!field.options.some((option) => option.code === code)) {
        messages.push(fieldMessage(section, path, 'error', texts.notAnOption(culture, label, code ?? '')));
      }
      return;
    }

    case 'enumSet': {
      if (!field.options?.length) return;
      for (const entry of asList(raw)) {
        const code = asString(entry);
        if (!field.options.some((option) => option.code === code)) {
          messages.push(fieldMessage(section, path, 'error', texts.notAnOption(culture, label, code ?? '')));
        }
      }
      return;
    }

    case 'string':
    case 'text':
      if (field.maxLength !== undefined && (asString(raw)?.length ?? 0) > field.maxLength) {
        messages.push(fieldMessage(section, path, 'error', texts.maxLength(culture, label, field.maxLength)));
      }
  }
}

// ----- rules ----------------------------------------------------------------------

function evaluateRowRule(
  template: FigureTemplate,
  rule: TemplateRule,
  values: InstanceValues,
  ctx: EvalContext,
  scope: Scope,
  changed: Set<string> | null,
  culture: string,
  messages: ValidationMessage[],
): void {
  const section = template.sections.find((s) => s.key === rule.forEachSection);
  if (!section || !evaluateAsBool(section.visibleWhen, ctx)) return;

  asList(navigate(values, section.key)).forEach((row, index) => {
    const scoped = withItem(ctx, row as Record<string, Json>);
    evaluateRule(rule, scoped, `${section.key}[${index}]`, scope, changed, culture, messages);
  });
}

function evaluateRule(
  rule: TemplateRule,
  ctx: EvalContext,
  prefix: string | null,
  scope: Scope,
  changed: Set<string> | null,
  culture: string,
  messages: ValidationMessage[],
): void {
  if (scope === 'field' && !dependsOnChanged(rule.dependsOn, changed) && !targetsChanged(rule, prefix, changed)) return;
  if (!evaluateAsBool(rule.when, ctx)) return;
  if (!rule.assert) return;

  const value = evaluate(rule.assert, ctx);
  // A rule whose inputs are not all filled in yet says nothing.
  if (value === null || value === undefined) return;
  if (truthy(value)) return;

  const text = localized(rule.message, culture);
  const targets = rule.targets.length > 0 ? rule.targets : [''];

  for (const target of targets) {
    messages.push({
      path: resolveTarget(target, prefix),
      message: text,
      severity: rule.severity,
      origin: 'rule',
      ruleId: rule.id,
      section: target.includes('.') ? target.slice(0, target.indexOf('.')) : undefined,
    });
  }
}

// ----- helpers --------------------------------------------------------------------

function touched(changed: Set<string> | null, path: string): boolean {
  if (!changed) return true;
  if (changed.has(path)) return true;
  for (const entry of changed) {
    if (entry.startsWith(`${path}.`) || entry.startsWith(`${path}[`)) return true;
  }
  return false;
}

function dependsOnChanged(dependsOn: string[] | undefined, changed: Set<string> | null): boolean {
  if (!changed) return true;
  if (!dependsOn?.length) return false;
  const normalizedChanged = new Set([...changed].map((path) => path.replace(/\[\d+\]/g, '[]')));
  return dependsOn.some((dep) => normalizedChanged.has(dep.replace(/\[\d+\]/g, '[]')));
}

function targetsChanged(rule: TemplateRule, prefix: string | null, changed: Set<string> | null): boolean {
  if (!changed) return false;
  return rule.targets.some((target) => changed.has(resolveTarget(target, prefix)));
}

function fieldMessage(
  section: TemplateSection,
  path: string,
  severity: 'error' | 'warning',
  message: string,
): ValidationMessage {
  return { path, message, severity, origin: 'field', section: section.key };
}

function deduplicate(messages: ValidationMessage[]): ValidationMessage[] {
  const seen = new Set<string>();
  return messages.filter((m) => {
    const key = `${m.path}|${m.ruleId ?? ''}|${m.message}`;
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  });
}
