# B3 reference exports

B3's own published reference data. These files are the authority for what a registration may
contain, and the platform checks itself against them rather than against hand-written lists.

| File | B3 export | Rows | What it is |
|---|---|---|---|
| `figuras.csv` | `DTpFiguras` | 88 | The payoff-figure catalogue: code, registered name, and whether B3 calculates settlement |
| `dominios-derivativos.csv` | `Dominios_DERIVATIVOS_COE` | 161 | Every registration domain and its values, across instrument types |
| `dominios-coe.csv` | `DominiosCOE` | 38 | The COE-scoped view of the same domains — a strict subset, kept as shipped |
| `dados-estrategia.csv` | `DTpDadosEstrategia` | 13,619 | The strategy-field dictionary: 5,503 attributes with type, size, decimals, mandatory flag, and accepted values |
| `ativos-subjacentes.csv` | `Ativos Subjacentes` | 7,784 | The underlying master; 3,487 rows are COE-eligible, covering 1,582 distinct codes |
| `campos-figuras.csv` | *Manual de Operações*, annex | 1,708 | Which attributes belong to which figure, with B3's own instruction for each — extracted from the manual, not an export |

**As of 2026-08-28**, with `figuras.csv` and `dados-estrategia.csv` carrying an internal stamp of
`20260831` on their first line. `campos-figuras.csv` comes from the manual version dated
20/07/2026 committed under [`../../docs/clearing/`](../../docs/clearing/README.md).

The five exports were converted from B3's Latin-1/CRLF to UTF-8 with LF so they diff and grep
cleanly. Nothing else was changed — no rows reordered, renamed or removed.

## The figure-attribute annex

`campos-figuras.csv` is the odd one out: B3 does not export it. The *Manual de Operações* carries
it as the annex **"Descrição dos campos das figuras"**, a two-column table — field name against
B3's instruction for filling it in — with a heading per figure. It is the only published source
that says which attributes belong to which figure, so it is extracted once and committed:

```bash
python3 tools/b3-annex/extract.py \
    docs/clearing/manual-de-operacoes-coe-202607.pdf \
    reference/b3/campos-figuras.csv
```

The instruction text is kept verbatim, because it carries the type, the precision, the accepted
domain and the conditions in a form the generator reads and a person can check:

> *Campo de preenchimento obrigatório. Formato: Numérico percentual com 4 inteiros e 8 decimais,
> maior que 0. Percentual aplicado sobre o Valor Inicial do Ativo Subjacente.*

**84 of the 88 figures are covered.** The annex has no entry for `COE001053`, `COE001057`,
`COE001072` or `COE001076` — the *Retorno Condicional* family — and the change log on page 5 of
the manual records B3 withdrawing the Dados Específicos of two of them on 05/09/2024. Read
alongside the *Caderno de Fórmulas*, which gives their redemption as principal plus interest
against the guaranteed capital and marks them *figura não-calculada*, the absence is the point:
those figures have no attributes of their own. They are modelled by hand against the common
blocks in [`../../domain/figures/`](../../domain/README.md#the-generated-figures) and are bookable
like any other.

**About 5% of rows are imperfect.** The annex is typeset three different ways across its 200
pages, and on a few of them several field names share one description cell or a note runs the
full width of the table. Those rows come out as one merged label; the generator recognises them
as prose and leaves them out rather than inventing an attribute, and the count it prints per
figure says how many it skipped.

## How they are used

**At generation time.** `tools/Coe.DomainGen` reads `figuras.csv` and `campos-figuras.csv` and
writes one domain file per catalogue figure into `domain/figures/generated/`. See
[`../../domain/README.md`](../../domain/README.md#the-generated-figures).

**At compile time.** `B3Reference` reads these files and `TemplateCompiler` checks every domain
file against them:

- a `figureCode` must exist in the catalogue, and a `figureName` that has drifted from B3's warns;
- a field declaring `b3Domain` must give every option a `b3Code` that exists and is enabled in
  that domain;
- a field declaring `b3FieldCode` must agree with the dictionary on type, size and decimals.

A figure that fails these is quarantined instead of published, so a B3 rename surfaces at
ingestion rather than when a registration is rejected.

**At run time.** The worker loads them into `b3.Figure`, `b3.Domain`, `b3.StrategyField`,
`b3.StrategyFieldValue` and `ref.Underlying`, replacing the contents each time — the export is
the whole truth, so a row that disappears from it must disappear from the database. That is what
backs the underlying picker and the `underlyingRegistered` check.

## Refreshing

Drop in newer exports and restart the worker (or `POST /api/admin/ingest`). There is no
migration: the files are the interface. `b3.ReferenceLoad` records what was loaded and when.

Expect the compiler to start complaining if B3 renamed a figure or retired a domain code — that
is the point of the check, and the fix is to update the affected domain file.

## Two things worth knowing about the data

**The figure label column is not consistently punctuated.** Most rows read
`COE001005 - Call Spread`, but `COE001060- CCallSpread + VPutSpread` has no space before the
hyphen and `COE001087 CallSpread + CallSpread` has no hyphen at all, so
`B3Reference.SplitFigureLabel` matches the code as a token rather than splitting on punctuation.
Two rows — `COE de Crédito – CDS com Amortização` and its TRS twin — carry a name and no code at
all; the manual's annex heads the same two figures `COE001085` and `COE001086`, so the code is
recovered from there instead of dropping figures B3 does publish. That is why `b3.Figure` holds
88 rows and the export appears to hold 86.

**The strategy-field dictionary has no figure association, and its own naming.** It is a flat
catalogue of 5,503 attributes; concept names repeat across figures — 488 of them contain "Strike"
— and they are not the names the registration screen and the annex use (`Limitador_Strike Put_1`
against `Limitador Cenário de Alta (%)`). Fewer than a third of the annex's field names appear in
it verbatim, so it cannot be used to attach a `C…` code to an attribute by name, in either
direction. `b3FieldCode` is therefore left unset — the annex settles *which* attributes a figure
has and what they hold, which is what the form needs; the `C…` code matters only for the batch
registration layout — and it is validated wherever it is set.
