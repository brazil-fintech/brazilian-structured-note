/**
 * TypeScript mirror of the compiled template contract (`src/Coe.Core/Templates/FigureTemplate.cs`).
 *
 * The client never invents form structure: everything it renders and everything it checks
 * comes from the template the ingestion worker compiled. Changing a shape here without
 * changing the C# record — or the reverse — breaks the form at runtime, so the two files are
 * edited together.
 */

export type Json = null | boolean | number | string | Json[] | { [key: string]: Json };

/** Node of the portable expression AST. Mirrors `Coe.Core.Expressions.Expr`. */
export type Expr =
  | { k: 'const'; v: Json }
  | { k: 'field'; p: string }
  | { k: 'item'; p: string }
  | { k: 'var'; n: string }
  | { k: 'op'; o: OpCode; a: Expr[] }
  | { k: 'fn'; n: string; a: Expr[] };

export type OpCode =
  | 'and' | 'or' | 'not'
  | 'eq' | 'neq' | 'gt' | 'gte' | 'lt' | 'lte'
  | 'add' | 'sub' | 'mul' | 'div' | 'mod' | 'neg'
  | 'in' | 'between';

export interface LocalizedText {
  pt: string;
  en?: string;
}

export type SectionKind = 'common' | 'tab';

export type FieldDataType =
  | 'string' | 'text' | 'integer' | 'decimal' | 'percent' | 'money'
  | 'date' | 'boolean' | 'enum' | 'enumSet';

export type RuleSeverity = 'error' | 'warning' | 'info';

/** `both` is written by the compiler as the single flag name, not as `client,server`. */
export type RuleExecution = 'client' | 'server' | 'both';

export type RuleTrigger = 'change' | 'submit' | 'both';

export interface FieldOption {
  code: string;
  label: LocalizedText;
  help?: LocalizedText;
  visibleWhen?: Expr;
}

export interface TemplateField {
  key: string;
  /** `section.key`, or `section[].key` for a repeating-section column. */
  path: string;
  label: LocalizedText;
  dataType: FieldDataType;
  b3Field?: string;
  /**
   * The attribute's identifier in B3's derivative-data dictionary. Carried so the form can say
   * which attributes a registration will actually be able to write; the field without one is
   * bookable but not registrable.
   */
  b3DataCode?: string;
  symbol?: string;
  help?: LocalizedText;
  unit?: string;
  decimals?: number;
  maxLength?: number;
  min?: number;
  max?: number;
  default?: Json;
  required?: boolean;
  requiredWhen?: Expr;
  visibleWhen?: Expr;
  enabledWhen?: Expr;
  computed?: Expr;
  options?: FieldOption[];
  optionSource?: string;
  dependsOn?: string[];
  order?: number;
  inGrid?: boolean;
}

export interface TemplateSection {
  key: string;
  label: LocalizedText;
  kind: SectionKind;
  order: number;
  help?: LocalizedText;
  visibleWhen?: Expr;
  repeating?: boolean;
  minItems?: number;
  maxItems?: number;
  fields: TemplateField[];
  itemFields: TemplateField[];
}

export interface TemplateRule {
  id: string;
  targets: string[];
  when?: Expr;
  assert?: Expr;
  serverCheck?: string;
  args?: Record<string, Json>;
  message: LocalizedText;
  severity: RuleSeverity;
  execution: RuleExecution;
  trigger: RuleTrigger;
  forEachSection?: string;
  dependsOn: string[];
}

export interface FigureTemplate {
  schemaVersion: string;
  figureCode: string;
  figureName: string;
  commercialName?: string;
  description?: LocalizedText;
  version: number;
  modalities: string[];
  underlyingClasses: string[];
  sourceFile?: string;
  sourceHash?: string;
  compiledAtUtc: string;
  sections: TemplateSection[];
  rules: TemplateRule[];
}

export type ValidationOrigin = 'field' | 'rule' | 'serverCheck';

export interface ValidationMessage {
  path: string;
  message: string;
  severity: RuleSeverity;
  origin: ValidationOrigin;
  ruleId?: string;
  section?: string;
}

/** The instance document: one entry per section, arrays for repeating sections. */
export type InstanceValues = Record<string, Json>;
