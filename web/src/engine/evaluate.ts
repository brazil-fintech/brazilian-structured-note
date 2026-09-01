import type { Expr, InstanceValues, Json } from './types';
import {
  addDays, asBool, asList, asNumber, asString, compare, dayNumber,
  equal, isAbsent, isDateString, truthy,
} from './values';

/**
 * Evaluates the portable AST — the browser half of the pair whose other half is
 * `Coe.Core.Expressions.ExpressionEvaluator`. Same node kinds, same operators, same
 * treatment of missing values (unknown paths yield null, so a half-filled form is quiet
 * rather than wrong).
 */

export interface EvalContext {
  root: InstanceValues;
  item?: Record<string, Json> | null;
  variables?: Record<string, unknown>;
}

export function withItem(ctx: EvalContext, item: Record<string, Json> | null | undefined): EvalContext {
  return { ...ctx, item: item ?? null };
}

/** Resolves a dotted path; missing segments yield null rather than throwing. */
export function navigate(from: unknown, path: string): unknown {
  let node: unknown = from;
  for (const segment of path.split('.')) {
    if (node === null || node === undefined) return null;
    if (Array.isArray(node)) {
      const index = Number(segment);
      node = Number.isInteger(index) ? node[index] : null;
    } else if (typeof node === 'object') {
      node = (node as Record<string, unknown>)[segment];
    } else {
      return null;
    }
  }
  return node ?? null;
}

export function evaluate(expr: Expr, ctx: EvalContext): unknown {
  switch (expr.k) {
    case 'const':
      return expr.v;

    case 'field': {
      if (ctx.item) {
        const fromItem = navigate(ctx.item, expr.p);
        if (fromItem !== null && fromItem !== undefined) return fromItem;
      }
      return navigate(ctx.root, expr.p);
    }

    case 'item':
      return ctx.item ? navigate(ctx.item, expr.p) : null;

    case 'var':
      return resolveVariable(expr.n, ctx);

    case 'op':
      return evaluateOp(expr, ctx);

    case 'fn':
      return evaluateFn(expr, ctx);

    default:
      return null;
  }
}

/** A condition that cannot be decided yet counts as false, matching `EvaluateAsBool`. */
export function evaluateAsBool(expr: Expr | undefined, ctx: EvalContext): boolean {
  return expr === undefined ? true : truthy(evaluate(expr, ctx));
}

function resolveVariable(name: string, ctx: EvalContext): unknown {
  if (ctx.variables && name in ctx.variables) return ctx.variables[name];
  if (name === 'today') return today();
  return null;
}

function today(): string {
  return new Date().toISOString().slice(0, 10);
}

function evaluateOp(expr: Extract<Expr, { k: 'op' }>, ctx: EvalContext): unknown {
  const { o, a } = expr;

  switch (o) {
    case 'and':
      return a.every((operand) => truthy(evaluate(operand, ctx)));

    case 'or':
      return a.some((operand) => truthy(evaluate(operand, ctx)));

    case 'not':
      return !truthy(evaluate(a[0], ctx));

    case 'eq':
      return equal(evaluate(a[0], ctx), evaluate(a[1], ctx));

    case 'neq':
      return !equal(evaluate(a[0], ctx), evaluate(a[1], ctx));

    case 'gt':
    case 'gte':
    case 'lt':
    case 'lte': {
      const cmp = compare(evaluate(a[0], ctx), evaluate(a[1], ctx));
      if (cmp === null) return null;
      if (o === 'gt') return cmp > 0;
      if (o === 'gte') return cmp >= 0;
      if (o === 'lt') return cmp < 0;
      return cmp <= 0;
    }

    case 'neg': {
      const n = asNumber(evaluate(a[0], ctx));
      return n === null ? null : -n;
    }

    case 'add':
    case 'sub':
    case 'mul':
    case 'div':
    case 'mod': {
      const left = evaluate(a[0], ctx);
      const right = evaluate(a[1], ctx);

      if (o === 'add' && (typeof left === 'string' || typeof right === 'string') &&
          !(isDateString(left) && asNumber(right) !== null)) {
        return (asString(left) ?? '') + (asString(right) ?? '');
      }

      if (isDateString(left) && (o === 'add' || o === 'sub')) {
        if (isDateString(right) && o === 'sub') return dayNumber(left) - dayNumber(right);
        const days = asNumber(right);
        if (days === null) return null;
        return addDays(left, o === 'add' ? days : -days);
      }

      const x = asNumber(left);
      const y = asNumber(right);
      if (x === null || y === null) return null;
      switch (o) {
        case 'add': return x + y;
        case 'sub': return x - y;
        case 'mul': return x * y;
        case 'div': return y === 0 ? null : x / y;
        default: return y === 0 ? null : x % y;
      }
    }

    case 'in': {
      const needle = evaluate(a[0], ctx);
      const haystack = evaluate(a[1], ctx);
      if (Array.isArray(haystack)) return haystack.some((candidate) => equal(needle, candidate));
      return a.slice(1).some((operand) => equal(needle, evaluate(operand, ctx)));
    }

    case 'between': {
      const value = evaluate(a[0], ctx);
      const low = compare(value, evaluate(a[1], ctx));
      const high = compare(value, evaluate(a[2], ctx));
      if (low === null || high === null) return null;
      return low >= 0 && high <= 0;
    }

    default:
      return null;
  }
}

function evaluateFn(expr: Extract<Expr, { k: 'fn' }>, ctx: EvalContext): unknown {
  const { n, a } = expr;
  const arg = (i: number) => evaluate(a[i], ctx);

  switch (n) {
    case 'isNull':
      return isAbsent(arg(0));

    case 'notNull':
      return !isAbsent(arg(0));

    case 'coalesce': {
      for (let i = 0; i < a.length; i++) {
        const value = arg(i);
        if (!isAbsent(value)) return value;
      }
      return null;
    }

    case 'abs': {
      const x = asNumber(arg(0));
      return x === null ? null : Math.abs(x);
    }

    case 'num':
      return asNumber(arg(0));

    case 'str':
      return asString(arg(0));

    case 'min':
    case 'max': {
      const numbers = a.map((_, i) => asNumber(arg(i)));
      if (numbers.length === 0 || numbers.some((x) => x === null)) return null;
      return n === 'min' ? Math.min(...(numbers as number[])) : Math.max(...(numbers as number[]));
    }

    case 'round': {
      const x = asNumber(arg(0));
      if (x === null) return null;
      const digits = a.length > 1 ? (asNumber(arg(1)) ?? 0) : 0;
      const factor = 10 ** Math.max(0, Math.trunc(digits));
      return Math.round(x * factor) / factor;
    }

    case 'floor':
    case 'ceil': {
      const x = asNumber(arg(0));
      if (x === null) return null;
      return n === 'floor' ? Math.floor(x) : Math.ceil(x);
    }

    case 'len': {
      const value = arg(0);
      if (typeof value === 'string') return value.length;
      if (Array.isArray(value)) return value.length;
      if (value === null || value === undefined) return 0;
      return null;
    }

    case 'count':
      return asList(arg(0)).length;

    case 'sum': {
      let total = 0;
      for (const item of asList(arg(0))) {
        const scoped = withItem(ctx, item as Record<string, Json>);
        total += asNumber(evaluate(a[1], scoped)) ?? 0;
      }
      return total;
    }

    case 'any':
    case 'all': {
      const items = asList(arg(0));
      const wantAll = n === 'all';
      for (const item of items) {
        const scoped = withItem(ctx, item as Record<string, Json>);
        const hit = truthy(evaluate(a[1], scoped));
        if (wantAll && !hit) return false;
        if (!wantAll && hit) return true;
      }
      return wantAll;
    }

    case 'isDistinct': {
      const seen = new Set<string>();
      for (const item of asList(arg(0))) {
        const scoped = withItem(ctx, item as Record<string, Json>);
        const key = asString(evaluate(a[1], scoped)) ?? '<null>';
        if (seen.has(key)) return false;
        seen.add(key);
      }
      return true;
    }

    case 'year':
    case 'month':
    case 'day': {
      const value = arg(0);
      if (!isDateString(value)) return null;
      const [y, m, d] = value.split('-').map(Number);
      return n === 'year' ? y : n === 'month' ? m : d;
    }

    case 'daysBetween': {
      const from = arg(0);
      const to = arg(1);
      if (!isDateString(from) || !isDateString(to)) return null;
      return dayNumber(to) - dayNumber(from);
    }

    case 'addDays': {
      const date = arg(0);
      const days = asNumber(arg(1));
      if (!isDateString(date) || days === null) return null;
      return addDays(date, days);
    }

    case 'today':
      return resolveVariable('today', ctx);

    case 'contains': {
      const haystack = arg(0);
      const needle = arg(1);
      if (Array.isArray(haystack)) return haystack.some((candidate) => equal(needle, candidate));
      const hs = asString(haystack);
      const ns = asString(needle);
      return hs === null || ns === null ? null : hs.includes(ns);
    }

    case 'upper':
    case 'lower': {
      const s = asString(arg(0));
      return s === null ? null : n === 'upper' ? s.toUpperCase() : s.toLowerCase();
    }

    default:
      return null;
  }
}

export { asBool };
