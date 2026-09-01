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

**As of 2026-08-28**, with `figuras.csv` and `dados-estrategia.csv` carrying an internal stamp of
`20260831` on their first line.

Converted from B3's Latin-1/CRLF to UTF-8 with LF so they diff and grep cleanly. Nothing else
was changed — no rows reordered, renamed or removed.

## How they are used

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
hyphen, `COE001087 CallSpread + CallSpread` has no hyphen at all, and two rows
(`COE de Crédito – CDS com Amortização` and its TRS twin) carry a name with no code and are
skipped. `B3Reference.SplitFigureLabel` matches the code as a token for this reason.

**The strategy-field dictionary has no figure association.** It is a flat catalogue of 5,503
attributes, and concept names repeat across figures — 488 of them contain "Strike". Mapping a
platform attribute to its `C…` code therefore cannot be done from the name; it needs the
"Descrição dos campos das figuras" annex of the *Manual de Operações*
([`../../docs/clearing/`](../../docs/clearing/README.md)). `b3FieldCode` is left unset until that
mapping is established, and validated wherever it is set.
