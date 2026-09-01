# Domain files — adding and changing a figure

A **domain file** is the source of truth for one B3 payoff figure: which attributes it has,
how they are laid out, when each one appears, and every rule that governs them. The ingestion
worker compiles these files into templates; the API and the React app do nothing but read the
result. **Adding a figure means adding a file here — no code, no deploy.**

```
domain/
  common/                reusable blocks a figure inherits through "extends"
  figures/               hand-written figures — the ones a person has modelled
    generated/           the rest of B3's catalogue, written from the manual's field annex
```

A figure with a hand-written file wins: the generator skips it, and the loader ignores a
generated file whose code a curated file already claims. Promoting a figure is dropping a file
into `figures/` — nothing to delete, nothing to remember.

## What happens to a file you drop in

1. The worker notices the change (file watch, or the next poll — see
   `Ingestion` in `appsettings.json`).
2. `TemplateCompiler` merges the fragments the figure extends, assigns an absolute path to
   every attribute, parses every condition and rule into a portable AST, resolves bare
   attribute names, and works out which attributes each rule reads.
3. If it compiles, a **new template version** is written to `figure.FigureTemplate` and becomes
   the active one; the figure is marked `Enabled` (unless `AutoEnableNewFigures` is off) and
   appears in the picker.
4. If it does **not** compile, nothing is published: the figure is marked `Quarantined` with
   the errors in `figure.Figure.LastError`, and the previously active template keeps serving.

Versions are immutable. Assets record the template version they were booked against, so an
edit here never rewrites the meaning of an asset already in the book.

Editing a file in `common/` re-issues a version for **every** figure that extends it — the
source hash covers the fragments too, so no figure is left running against a stale copy of a
shared block.

## File shape

```jsonc
{
  "figureCode": "COE001005",          // the B3 figure code; unique across the catalog
  "figureName": "Call Spread",        // the registered figure name
  "commercialName": "Call spread (trava de alta)",
  "description": { "pt": "…", "en": "…" },
  "modalities": ["VNP"],
  "underlyingClasses": ["ACOES", "INDICES", "…"],

  "extends": ["common/identification", "common/underlying",
              "common/remuneration", "common/settlement"],
  "removeSections": [],               // fragment sections this figure does not use

  "sections": [ … ],                  // the figure's own blocks, and overrides of inherited ones
  "rules":    [ … ]                   // the figure's own rules
}
```

The common fragments carry the whole *Registro COE* fixed record between them, so a figure that
extends them is registrable without adding a field of its own:

| Fragment | Section | What it covers |
|---|---|---|
| `common/identification` | `common` | Conta Emissora, Nome Fantasia, Código Identificador, ISIN, dates including Emissão a Termo, quantity, price, modality, guaranteed capital |
| `common/underlying` | `underlying` | Class, asset, initial value, fixing window and dates, lookback, quanto and parity, dividend protection, and the basket's type, parity currency and parity fixing |
| | `basket` | One row per component: code, quotation type, initial value, fixing date and weight — the *RegistroCestas* (CEST) file |
| | `fixingDates` | The explicit capture schedule a "Mais Datas" period leaves pending — the *Registro Datas Fixing* (DTFX) file |
| `common/remuneration` | `remuneration` | Maturity remunerator with its description, floating percentage, spread, basis and initial quote; and the cash-flow schedule's remunerator, basis, barrier conditions and coupon memory |
| | `cashflows` | One row per event: payment date, floating rate, spread, call and coupon barriers with their second bounds, fixing dates and fixing type — the *Registro Fluxo de Caixa* (FLUX) file |
| `common/settlement` | `terms` | Base application, issuer position, custody regime, CVM 8, physical delivery and its description, early redemption and its qualification, functionality, extraordinary payment, issuer call clause |
| | `deposit` | The deposit leg the registration carries: beneficiary account and document, own reference, unit price, settlement modality and bank |
| `common/barriers` | `barriers` | Barrier level, direction, type, verification period and window |
| `common/autocall` | `autocall`, `observations` | Autocall trigger, payment timing, coupon memory and the observation schedule |

### Sections

A section is either the **common block** shown above the tabs, or one **tab**.

```jsonc
{
  "key": "payoff",
  "kind": "tab",                      // "common" | "tab"
  "order": 30,                        // display order; the common block sits at 0
  "label": { "pt": "Payoff", "en": "Payoff" },
  "help":  { "pt": "…" },
  "visibleWhen": "underlying.assetClass == 'CESTA'",   // optional

  "repeating": false,                 // true for grids: cash flows, basket, observations
  "minItems": 1, "maxItems": 120,     // repeating sections only
  "fields":     [ … ],                // non-repeating sections
  "itemFields": [ … ]                 // repeating sections (the grid columns)
}
```

A repeating section's rows are an array in the instance document, and its columns are addressed
as `cashflows[].amount` in templates and `cashflows[2].amount` in messages.

A section listed by a figure with a key a fragment already provides **merges into** it: the
figure can retitle it, add columns, and replace individual attributes by key. That is how the
shark fin narrows the generic barrier block down to an up-and-out.

### Fields

```jsonc
{
  "key": "cap",
  "order": 30,
  "label":  { "pt": "Cap (%)", "en": "Cap (%)" },
  "help":   { "pt": "Performance máxima considerada pela fórmula." },
  "dataType": "percent",
  "b3Field": "Limitador",             // the registered B3 field, shown under the input
  "symbol": "Cap",                    // the formula symbol from docs/payoffs/
  "required": true,
  "min": 0, "max": 1000, "decimals": 6,
  "default": 25,
  "visibleWhen":  "…",                // hidden fields are never required and never validated
  "requiredWhen": "…",
  "enabledWhen":  "false",            // read-only
  "computed": "quantity * unitIssuePrice",   // derived; the API recomputes it before saving
  "options": [ { "code": "VNP", "b3Code": "1", "label": { "pt": "…" } } ],
  "optionSource": "underlyings",      // or resolved from /api/reference/{source}
  "b3Domain": "TIPO CESTA",           // options are checked against this B3 domain
  "b3FieldCode": "C0000368",          // type/size/decimals checked against B3's dictionary
  "inGrid": true                      // show in the asset list
}
```

### Codes: yours and B3's

An option has two codes. `code` is the mnemonic stored in the instance and referenced by rules —
readable, stable, and yours. `b3Code` is what a registration file must carry. Declare `b3Domain`
on the field and the compiler checks every `b3Code` against
[`reference/b3/`](../reference/b3/README.md): unknown codes fail the build, and codes B3 has
disabled warn.

**`b3DataCode` is the one that decides whether an attribute can be registered.** It is the
attribute's identifier in B3's derivative-data dictionary (`DTpTipoDadosDerivativo`), and it is
what the "Identificador do Campo" of the *Registro COE* variable-data record carries. An
attribute without one is bookable and validated but cannot be written to B3.

You rarely write it. B3 publishes, per figure, which of its attributes the figure registers
(`DTpFigurasDadosDerivativo`), so the compiler matches a field to one **by B3's own name for the
attribute** — which is what `b3Field` is for. Write `b3Field` exactly as the registration screen
prints it and the code attaches itself; the compiler then reports how many of the figure's
published attributes are still unaccounted for. Setting `b3DataCode` outright always wins, and
the compiler holds you to it: the code must exist, agree on type and precision, offer only the
values that field accepts, and be an attribute B3 registers for *this* figure.

`b3FieldCode` is a different dictionary — `DTpDadosEstrategia`, where the same `C…` codes mean
different attributes — and is left unset almost everywhere; see
[the reference README](../reference/b3/README.md#two-dictionaries-not-one). Where it is set, the
declared `dataType`, `maxLength` and `decimals` are checked against that dictionary instead.

Asset classes are stored using B3's own spelling (`ACOES INTERNACIONAIS`), since the underlying
master is keyed on it.

`dataType` is one of `string`, `text`, `integer`, `decimal`, `percent`, `money`, `date`,
`boolean`, `enum`, `enumSet`.

**Percentages are stored as the percentage number**: `25` means 25%, matching the B3
registration screens. Rules must be written in the same units.

### Rules

```jsonc
{
  "id": "callspread.cap-positive",     // unique within the figure
  "targets": ["payoff.cap"],           // where the message lands; a section key for row-count rules
  "when": "modality == 'VNP'",         // optional guard; the rule is skipped unless truthy
  "assert": "cap > 0",                 // must hold
  "message": { "pt": "…", "en": "…" },
  "severity": "error",                 // error blocks the save | warning does not | info
  "execution": "both",                 // client | server | both
  "trigger": "change",                 // change | submit | both
  "forEachSection": "cashflows"        // evaluate once per row
}
```

A rule that cannot be expressed as an expression names a **server check** instead — anything
needing reference data:

```jsonc
{
  "id": "common.issue-date-business-day",
  "targets": ["common.issueDate"],
  "serverCheck": "businessDay",
  "args": { "path": "common.issueDate", "calendar": "BRASIL" },
  "message": { "pt": "A Data de Emissão deve ser dia útil no calendário nacional." },
  "severity": "error", "execution": "server", "trigger": "change"
}
```

Available checks (`src/Coe.Infrastructure/ServerChecks/`):

| id | arguments | asks |
|---|---|---|
| `businessDay` | `path`, `calendar` | is the date a business day? |
| `businessDaysBefore` | `path`, `referencePath`, `minimum`, `maximum`, `calendar` | does the date sit N business days before another? |
| `observationCountMatchesCalendar` | `countPath`, `startPath`, `endPath`, `calendar` | does a fixing count match the window? |
| `uniqueInstrumentCode` | `path` | is the Código IF free? |
| `underlyingRegistered` | `path` | does B3's master list this underlying for this class? |

**Three severities, one gate.** `error` blocks the save. `warning` never does — the user can
save through it and the accepted warnings are stored on the asset for audit. `info` is a note.

**Where a rule runs is a performance choice, not a safety one.** `execution` says where a rule
*can* run: `client` gives instant feedback, `server` is for checks needing reference data,
`both` runs in the browser and again on the API. Regardless of the setting, **the API re-runs
every server-side rule on save** — nothing is trusted because the browser already checked it.

## Expression language

Conditions and rules are short infix expressions, parsed once at ingestion into an AST that
both the API (`ExpressionEvaluator`) and the browser (`web/src/engine/evaluate.ts`) evaluate.
A typo is a compile error that quarantines the figure, not a surprise at booking time.

**Attribute names.** Write a bare name (`cap`) and the compiler resolves it: a column of the
current repeating section first, then a field of the current section, then any uniquely-named
field in the template. Ambiguous or unknown names fail compilation — qualify them
(`underlying.assetClass`). Inside a repeating section, `@.weight` is explicitly the current
row's column.

**Operators**, loosest to tightest binding:

| | |
|---|---|
| `or` `\|\|` | |
| `and` `&&` | |
| `not` `!` | |
| `==` `!=` `>` `>=` `<` `<=` `in` | `in` takes a list literal: `modality in ['VNP', 'VNR']` |
| `+` `-` | also date ± days, and date − date giving a day count |
| `*` `/` `%` | |

**Functions**

| group | functions |
|---|---|
| null handling | `isNull(x)` `notNull(x)` `coalesce(a, b, …)` |
| numbers | `abs` `min` `max` `round(x, d)` `floor` `ceil` `num` |
| collections | `count(list)` `len(x)` `sum(list, @.f)` `any(list, pred)` `all(list, pred)` `isDistinct(list, @.f)` |
| dates | `year` `month` `day` `daysBetween(a, b)` `addDays(d, n)` `today()` |
| text | `contains(s, sub)` `upper` `lower` `str` |
| ranges | `between(x, low, high)` |

Literals: numbers, `'single-quoted strings'`, `true`, `false`, `null`, and `[…]` list literals
of constants. `$name` reads a host variable (`$today`).

**Missing values are undecided, not zero.** `cap > 0` with no cap yet evaluates to null, and a
rule whose assertion is null says nothing. That is what keeps a half-filled form quiet instead
of wrong. Use `isNull` / `notNull` when you mean to test for absence.

## The generated figures

Fourteen figures are written by hand. The other 74 come from B3's own documentation:
`tools/Coe.DomainGen` reads the figure catalogue and the field annex extracted to
[`reference/b3/campos-figuras.csv`](../reference/b3/README.md#the-figure-attribute-annex), and
writes one file per figure into `figures/generated/`.

```bash
dotnet run --project tools/Coe.DomainGen        # from the repository root
```

It prints a line per figure — attributes written, attributes inherited from a common block,
rules derived, rows skipped — so a manual that changes shows up as a diff in both the CSV and
the files.

What it takes from B3's instruction for each attribute:

| B3 writes | becomes |
|---|---|
| "Formato: Numérico percentual com 4 inteiros e 8 decimais" | `dataType: percent`, `decimals: 8`, `max: 9999` |
| "Formato: DD/MM/AAAA" | `dataType: date` |
| "Campo com as opções: Data Única, Janela de Datas e Mais Datas" | `dataType: enum` with those three options |
| "Campo de preenchimento obrigatório" | `required: true` |
| "obrigatório, se indicado 'Janela de datas'" | `requiredWhen` on whichever attribute offers that value |
| "maior que 0" | a rule asserting `> 0`, with B3's field name in the message |
| "Não preencher se a 'Classe do Ativo Subjacente' for igual a 'CESTA'" | `visibleWhen: underlying.assetClass != 'CESTA'` |

Everything else in the instruction is kept verbatim as the attribute's `help`, because the desk
reads the same sentences. An attribute a common block already carries — the fixing window, the
quotation type, the barrier direction — is inherited rather than restated, so the curated version
with its labels, defaults and rules is the one that renders.

**What generation does not give you.** No formula symbols, no link to a page under
`docs/payoffs/`, no economic warnings, and no modality restriction — B3 does not publish the
modality per figure, so a generated file offers both rather than inventing a rule. That is the
work a curated file adds, and it is why the fourteen exist.

**A figure with no attributes of its own is still a figure.** Four codes — `COE001053`,
`COE001057`, `COE001072` and `COE001076`, the *Retorno Condicional* family — have no entry in the
annex, and B3 explicitly withdrew the Dados Específicos of two of them in September 2024. That is
not a gap: the *Caderno de Fórmulas* gives their redemption as
`máx(principal acrescido de juros; principal × Capital Garantido)`, with early redemption settled
under the registered specific conditions. There is no option leg and nothing figure-specific to
register beyond the DI accrual offset, so they are hand-written against the common blocks and are
bookable like anything else. Generation skips a figure with no annex rows; do not read that as
"cannot be modelled".

## Checklist for a new figure

1. Copy the closest file in `figures/`; set `figureCode`, `figureName`, `commercialName`,
   `description`, `modalities`.
2. Extend the fragments it needs. `common/barriers` for a barrier figure, `common/autocall` for
   an autocall overlay.
3. Add the `payoff` section with the figure's own parameters — mirror the names and symbols in
   the matching page under [`docs/payoffs/`](../docs/payoffs/README.md).
4. Write the rules that a booking desk would otherwise catch by eye: level ordering, the
   modality the figure is registered under, and economic sanity checks as warnings.
5. Run `dotnet test` — the suite compiles every file in this directory, generated ones included,
   and fails on an unknown attribute, an ambiguous name, a rule with no target, a duplicate
   figure code, a figure code B3 does not publish, or an option code that is not in the B3 domain
   the field names.

To replace a generated figure with a curated one, copy `figures/generated/<code>.json` up into
`figures/`, give it a descriptive filename, and work from there — the generated copy stops being
loaded the moment the curated file declares the same `figureCode`, and the next regeneration
drops it.

## Where to read more

- [`../reference/b3/README.md`](../reference/b3/README.md) — B3's published exports and what the compiler checks against them.
- [`../docs/parameters.md`](../docs/parameters.md) — every registration field and payoff parameter, with its B3 name.
- [`../docs/payoffs/`](../docs/payoffs/README.md) — the formula and worked example behind each figure.
- [`../docs/platform.md`](../docs/platform.md) — how the worker, API and React app fit together.
