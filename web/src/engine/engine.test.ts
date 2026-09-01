import { describe, expect, it } from 'vitest';
import { evaluate, type EvalContext } from './evaluate';
import { applyComputed, buildDefaults } from './instance';
import { readPath, resolveTarget, splitPath, writePath } from './paths';
import type { Expr, FigureTemplate, InstanceValues, OpCode } from './types';
import { validate } from './validate';

/**
 * These cases mirror `tests/Coe.Tests/ExpressionTests.cs` and
 * `tests/Coe.Tests/ValidationEngineTests.cs`. Both evaluators read the same compiled
 * templates, so if one of them starts answering differently the form and the API stop
 * agreeing — that is the drift these tests exist to catch.
 */

const c = (v: unknown): Expr => ({ k: 'const', v: v as never });
const f = (p: string): Expr => ({ k: 'field', p });
const at = (p: string): Expr => ({ k: 'item', p });
const op = (o: OpCode, ...a: Expr[]): Expr => ({ k: 'op', o, a });
const fn = (n: string, ...a: Expr[]): Expr => ({ k: 'fn', n, a });

function evalWith(expr: Expr, root: InstanceValues = {}): unknown {
  const ctx: EvalContext = { root };
  return evaluate(expr, ctx);
}

describe('expression evaluator', () => {
  it('applies arithmetic', () => {
    expect(evalWith(op('add', c(1), op('mul', c(2), c(3))))).toBe(7);
    expect(evalWith(op('div', c(10), c(4)))).toBe(2.5);
    expect(evalWith(op('neg', c(3)))).toBe(-3);
  });

  it('returns null instead of dividing by zero', () => {
    expect(evalWith(op('div', c(1), c(0)))).toBeNull();
  });

  it('treats an unfilled attribute as undecided, not as zero', () => {
    expect(evalWith(op('gt', f('payoff.cap'), c(0)), { payoff: {} })).toBeNull();
    expect(evalWith(fn('isNull', f('payoff.cap')), { payoff: {} })).toBe(true);
    expect(evalWith(fn('isNull', f('payoff.cap')), { payoff: { cap: '' } })).toBe(true);
  });

  it('compares and subtracts dates', () => {
    const values = { common: { issueDate: '2026-01-05', maturityDate: '2028-01-05' } };
    expect(evalWith(op('gt', f('common.maturityDate'), f('common.issueDate')), values)).toBe(true);
    expect(evalWith(fn('daysBetween', f('common.issueDate'), f('common.maturityDate')), values)).toBe(730);
    expect(evalWith(fn('addDays', f('common.issueDate'), c(7)), values)).toBe('2026-01-12');
  });

  it('handles membership and ranges', () => {
    expect(evalWith(op('in', c('VNP'), c(['VNP', 'VNR'])))).toBe(true);
    expect(evalWith(op('in', c('VNX'), c(['VNP', 'VNR'])))).toBe(false);
    expect(evalWith(op('between', c(5), c(1), c(10)))).toBe(true);
    expect(evalWith(op('between', c(50), c(1), c(10)))).toBe(false);
  });

  it('scopes collection functions to the current row', () => {
    const values = { basket: [{ component: 'PETR4', weight: 60 }, { component: 'VALE3', weight: 40 }] };
    expect(evalWith(fn('sum', f('basket'), at('weight')), values)).toBe(100);
    expect(evalWith(fn('count', f('basket')), values)).toBe(2);
    expect(evalWith(fn('all', f('basket'), op('gt', at('weight'), c(0))), values)).toBe(true);
    expect(evalWith(fn('any', f('basket'), op('gt', at('weight'), c(90))), values)).toBe(false);
    expect(evalWith(fn('isDistinct', f('basket'), at('component')), values)).toBe(true);
  });

  it('detects repeated rows', () => {
    const values = { cashflows: [{ paymentDate: '2027-01-05' }, { paymentDate: '2027-01-05' }] };
    expect(evalWith(fn('isDistinct', f('cashflows'), at('paymentDate')), values)).toBe(false);
  });

  it('tolerates floating-point drift when comparing weights', () => {
    // 33.33 * 3 is not exactly 99.99 in IEEE arithmetic; the server compares decimals and
    // would say these sum to 99.99, so the browser must not disagree.
    const values = { basket: [{ weight: 33.33 }, { weight: 33.33 }, { weight: 33.33 }] };
    const total = fn('sum', f('basket'), at('weight'));
    expect(evalWith(op('eq', total, c(99.99)), values)).toBe(true);
  });
});

describe('instance paths', () => {
  it('splits and reads indexed paths', () => {
    expect(splitPath('cashflows[2].amount')).toEqual(['cashflows', '2', 'amount']);
    expect(readPath({ cashflows: [{}, {}, { amount: 5 }] }, 'cashflows[2].amount')).toBe(5);
  });

  it('writes without mutating the previous value', () => {
    const before: InstanceValues = { common: { quantity: 1 } };
    const after = writePath(before, 'common.quantity', 2);
    expect((before.common as Record<string, unknown>).quantity).toBe(1);
    expect(readPath(after, 'common.quantity')).toBe(2);
  });

  it('creates missing containers', () => {
    const values = writePath({}, 'cashflows[0].paymentDate', '2027-01-05');
    expect(readPath(values, 'cashflows[0].paymentDate')).toBe('2027-01-05');
    expect(Array.isArray(values.cashflows)).toBe(true);
  });

  it('rewrites a rule target onto the row it belongs to', () => {
    expect(resolveTarget('cashflows[].paymentDate', 'cashflows[3]')).toBe('cashflows[3].paymentDate');
    expect(resolveTarget('common.issueDate', 'cashflows[3]')).toBe('common.issueDate');
    expect(resolveTarget('payoff.cap', null)).toBe('payoff.cap');
  });
});

/** A miniature template exercising the same features the real ones use. */
const template: FigureTemplate = {
  schemaVersion: '1.0',
  figureCode: 'TEST001',
  figureName: 'Test figure',
  version: 1,
  modalities: ['VNP'],
  underlyingClasses: [],
  compiledAtUtc: '2026-08-30T00:00:00Z',
  sections: [
    {
      key: 'common',
      label: { pt: 'Dados gerais' },
      kind: 'common',
      order: 0,
      fields: [
        { key: 'issueDate', path: 'common.issueDate', label: { pt: 'Emissão' }, dataType: 'date', required: true },
        { key: 'maturityDate', path: 'common.maturityDate', label: { pt: 'Vencimento' }, dataType: 'date', required: true },
        { key: 'quantity', path: 'common.quantity', label: { pt: 'Quantidade' }, dataType: 'integer', required: true, min: 1, default: 1 },
        { key: 'unitPrice', path: 'common.unitPrice', label: { pt: 'PU' }, dataType: 'money', required: true, default: 1000 },
        {
          key: 'notional', path: 'common.notional', label: { pt: 'VFE' }, dataType: 'money',
          computed: op('mul', f('common.quantity'), f('common.unitPrice')),
        },
      ],
      itemFields: [],
    },
    {
      key: 'payoff',
      label: { pt: 'Payoff' },
      kind: 'tab',
      order: 30,
      fields: [
        { key: 'strike', path: 'payoff.strike', label: { pt: 'Strike' }, dataType: 'percent', required: true, default: 100 },
        { key: 'cap', path: 'payoff.cap', label: { pt: 'Cap' }, dataType: 'percent', required: true, min: 0 },
        {
          key: 'rebate', path: 'payoff.rebate', label: { pt: 'Rebate' }, dataType: 'percent',
          visibleWhen: op('gt', f('payoff.cap'), c(50)),
          requiredWhen: op('gt', f('payoff.cap'), c(50)),
        },
      ],
      itemFields: [],
    },
    {
      key: 'cashflows',
      label: { pt: 'Fluxo de caixa' },
      kind: 'tab',
      order: 40,
      repeating: true,
      minItems: 1,
      visibleWhen: op('gt', fn('count', f('cashflows')), c(0)),
      fields: [],
      itemFields: [
        { key: 'paymentDate', path: 'cashflows[].paymentDate', label: { pt: 'Data' }, dataType: 'date', required: true },
        { key: 'amount', path: 'cashflows[].amount', label: { pt: 'Valor' }, dataType: 'percent', required: true, min: 0 },
      ],
    },
  ],
  rules: [
    {
      id: 'test.maturity-after-issue',
      targets: ['common.maturityDate'],
      assert: op('gt', f('common.maturityDate'), f('common.issueDate')),
      message: { pt: 'O vencimento deve ser posterior à emissão.' },
      severity: 'error',
      execution: 'both',
      trigger: 'change',
      dependsOn: ['common.issueDate', 'common.maturityDate'],
    },
    {
      id: 'test.cap-positive',
      targets: ['payoff.cap'],
      assert: op('gt', f('payoff.cap'), c(0)),
      message: { pt: 'O cap deve ser maior que zero.' },
      severity: 'error',
      execution: 'both',
      trigger: 'change',
      dependsOn: ['payoff.cap'],
    },
    {
      id: 'test.wide-cap',
      targets: ['payoff.cap'],
      assert: op('lte', f('payoff.cap'), c(200)),
      message: { pt: 'Cap muito largo.' },
      severity: 'warning',
      execution: 'both',
      trigger: 'change',
      dependsOn: ['payoff.cap'],
    },
    {
      id: 'test.payment-within-tenor',
      forEachSection: 'cashflows',
      targets: ['cashflows[].paymentDate'],
      assert: op('lte', at('paymentDate'), f('common.maturityDate')),
      message: { pt: 'Pagamento após o vencimento.' },
      severity: 'error',
      execution: 'both',
      trigger: 'change',
      dependsOn: ['cashflows[].paymentDate', 'common.maturityDate'],
    },
    {
      id: 'test.business-day',
      targets: ['common.issueDate'],
      serverCheck: 'businessDay',
      message: { pt: 'A emissão deve ser dia útil.' },
      severity: 'error',
      execution: 'server',
      trigger: 'change',
      dependsOn: ['common.issueDate'],
    },
  ],
};

function baseValues(): InstanceValues {
  return {
    common: { issueDate: '2026-09-01', maturityDate: '2028-09-01', quantity: 1000, unitPrice: 1000, notional: 1_000_000 },
    payoff: { strike: 100, cap: 25 },
    cashflows: [],
  };
}

const ids = (messages: { ruleId?: string; path: string }[]) => messages.map((m) => m.ruleId ?? m.path);

describe('client validation', () => {
  it('accepts a well-formed instance', () => {
    const result = validate(template, baseValues(), { scope: 'submit' });
    expect(ids(result.messages)).toEqual([]);
  });

  it('rejects a maturity before the issue date', () => {
    const values = baseValues();
    (values.common as Record<string, unknown>).maturityDate = '2025-01-01';
    const result = validate(template, values, { scope: 'submit' });
    expect(ids(result.messages)).toContain('test.maturity-after-issue');
  });

  it('pins a message to the attribute it is about', () => {
    const values = baseValues();
    (values.payoff as Record<string, unknown>).cap = 0;
    const result = validate(template, values, { scope: 'submit' });
    expect(result.messages.find((m) => m.ruleId === 'test.cap-positive')?.path).toBe('payoff.cap');
  });

  it('separates warnings from errors', () => {
    const values = baseValues();
    (values.payoff as Record<string, unknown>).cap = 300;
    const result = validate(template, values, { scope: 'submit' });
    const warning = result.messages.find((m) => m.ruleId === 'test.wide-cap');
    expect(warning?.severity).toBe('warning');
    expect(result.messages.filter((m) => m.severity === 'error' && m.ruleId)).toEqual([]);
  });

  it('leaves server-only rules to the API', () => {
    const result = validate(template, baseValues(), { scope: 'submit' });
    expect(ids(result.messages)).not.toContain('test.business-day');
  });

  it('does not demand hidden attributes', () => {
    // rebate only appears once the cap passes 50.
    const result = validate(template, baseValues(), { scope: 'submit' });
    expect(result.messages.map((m) => m.path)).not.toContain('payoff.rebate');
  });

  it('demands a conditional attribute once its condition holds', () => {
    const values = baseValues();
    (values.payoff as Record<string, unknown>).cap = 80;
    const result = validate(template, values, { scope: 'submit' });
    expect(result.messages.map((m) => m.path)).toContain('payoff.rebate');
  });

  it('field scope only speaks about what changed', () => {
    const values = baseValues();
    (values.payoff as Record<string, unknown>).cap = 0;
    (values.common as Record<string, unknown>).issueDate = null;

    const result = validate(template, values, { scope: 'field', changedPaths: ['payoff.cap'] });

    expect(ids(result.messages)).toContain('test.cap-positive');
    expect(result.messages.map((m) => m.path)).not.toContain('common.issueDate');
  });

  it('field scope re-runs the rules that read the changed attribute', () => {
    const values = baseValues();
    (values.common as Record<string, unknown>).maturityDate = '2025-01-01';

    const result = validate(template, values, { scope: 'field', changedPaths: ['common.issueDate'] });

    expect(ids(result.messages)).toContain('test.maturity-after-issue');
  });

  it('flags the offending row of a repeating section', () => {
    const values = baseValues();
    values.cashflows = [
      { paymentDate: '2027-03-01', amount: 5 },
      { paymentDate: '2030-01-01', amount: 5 },
    ];
    const result = validate(template, values, { scope: 'submit' });
    const message = result.messages.find((m) => m.ruleId === 'test.payment-within-tenor');
    expect(message?.path).toBe('cashflows[1].paymentDate');
  });

  it('does not enforce minItems on a hidden repeating section', () => {
    const result = validate(template, baseValues(), { scope: 'submit' });
    expect(result.messages.map((m) => m.path)).not.toContain('cashflows');
  });

  it('form scope stays quiet about attributes that are merely empty', () => {
    const values = baseValues();
    (values.payoff as Record<string, unknown>).cap = null;
    const result = validate(template, values, { scope: 'form' });
    expect(result.messages.map((m) => m.path)).not.toContain('payoff.cap');
  });
});

describe('computed attributes', () => {
  it('seeds defaults and derives values', () => {
    const values = buildDefaults(template);
    expect(readPath(values, 'common.quantity')).toBe(1);
    expect(readPath(values, 'common.notional')).toBe(1000);
    expect(readPath(values, 'payoff.strike')).toBe(100);
    expect(values.cashflows).toEqual([]);
  });

  it('recomputes a derived attribute from its inputs', () => {
    const values = applyComputed(template, {
      ...baseValues(),
      common: { issueDate: '2026-09-01', maturityDate: '2028-09-01', quantity: 500, unitPrice: 2000, notional: 1 },
    });
    expect(readPath(values, 'common.notional')).toBe(1_000_000);
  });
});
