# B3 reference exports

B3's own published reference data. These files are the authority for what a registration may
contain, and the platform checks itself against them rather than against hand-written lists.

They are **pulled from CETIP's public directory**, `ftp://ftp.cetip.com.br/Public`, which is the
address the *ENVIAR ARQUIVOS* layouts themselves print next to the fields whose domains live in
them. The copies here are what the last sync fetched; [`cetip-manifest.json`](cetip-manifest.json)
records which dated file each one came from. See [Where they come from](#where-they-come-from).

| File | CETIP export | Rows | What it is |
|---|---|---|---|
| `figuras.csv` | `DTpFiguras` | 88 | The payoff-figure catalogue: code, registered name, and whether B3 calculates settlement |
| `dados-derivativo.csv` | `DTpTipoDadosDerivativo` | 850 | **The dictionary the registration file writes against**: 496 attributes with type, size, decimals, mandatory flag, and the identifier of every value a domain field takes |
| `figuras-dados-derivativo.csv` | `DTpFigurasDadosDerivativo` | 3,100 | **Which of those attributes each figure registers** — 1,647 pairings over all 88 figures |
| `dominios-derivativos.csv` | `Dominios_DERIVATIVOS_COE` | 161 | Every registration domain and its values, across instrument types |
| `dominios-coe.csv` | `DominiosCOE` | 38 | The COE-scoped view of the same domains — a strict subset, kept as shipped |
| `dados-estrategia.csv` | `DTpDadosEstrategia` | 13,619 | The strategy-field dictionary: 5,503 attributes. A *different* catalogue — see [below](#two-dictionaries-not-one) |
| `ativos-subjacentes.csv` | `Ativos Subjacentes` | 7,784 | The underlying master; 3,487 rows are COE-eligible, covering 1,582 distinct codes |
| `curvas-moedas-feeder.csv` | `Cadastro_Curvas_Moedas_Feeder_Dominios` | 4,653 | Curve, currency and feeder qualifications, including the codes for "Condição Específica Resgate" |
| `mnemonicos.csv` | `mnemonicos_cetip` | 132 | Participant mnemonics: the "Nome Simplificado" every upload header carries, against the institution's account and CNPJ |
| `campos-figuras.csv` | *Manual de Operações*, annex | 1,708 | B3's own instruction for filling each attribute in — extracted from the manual, not an export |

**As of 2026-08-28**, with several files carrying an internal stamp of `20260831` on their first
line. `campos-figuras.csv` comes from the manual version dated 20/07/2026 committed under
[`../../docs/clearing/`](../../docs/clearing/README.md).

The exports are converted from CETIP's single-byte CRLF text to UTF-8 with LF so they diff and
grep cleanly. Nothing else is changed — no rows reordered, renamed or removed. The encoding is
Windows-1252 rather than the ISO-8859-1 it is usually called: `COE de Crédito – CDS com
Amortização` carries byte `0x96`, an en dash there and an unassigned control character in
Latin-1. `Coe.Core.Text.Windows1252` maps the thirty-two positions where the two differ.

## Where they come from

`CetipReferenceSync` lists `/Public`, takes the newest dated file for each export — the directory
keeps every day's, named `20260828_DTpFiguras.txt` — transcodes it, and writes it here. The
worker runs it at start-up and then no more often than `Cetip:MinimumInterval` (six hours by
default), because B3 publishes once a day and the ingestion loop wakes every few minutes.

```jsonc
"Cetip": {
  "Enabled": true,
  "Host": "ftp.cetip.com.br",
  "Directory": "/Public",
  "MinimumInterval": "06:00:00",
  // Set this and nothing connects to B3: the folder is read instead, with the same
  // newest-file selection. This is how a desk behind a firewall mirrors once and shares.
  "LocalMirrorDirectory": null
}
```

`POST /api/admin/cetip/sync` runs it on demand; `GET /api/admin/cetip` says what the platform is
currently checking against and where each file came from.

**Nothing here is required for the platform to run.** A directory that cannot be reached, an
export not yet published, a listing that comes back short — each leaves the copy on disk in place
and is reported. A sync also refuses to replace a file with an older one, so a partial listing
cannot roll the reference data backwards. That is why the files stay committed: stale reference
data is a great deal better than none, and it means a fresh checkout compiles without a network.

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
- a field declaring `b3FieldCode` must agree with the strategy dictionary on type, size and decimals;
- a field declaring `b3DataCode` must agree with the derivative dictionary on the same, must
  offer only values that field accepts, and must be an attribute B3 registers for *this* figure;
- a field that declares none is matched to one by B3's own name for the attribute, and the
  compiler reports how many of the figure's published attributes are still unaccounted for.

A figure that fails these is quarantined instead of published, so a B3 rename surfaces at
ingestion rather than when a registration is rejected.

**At registration time.** `b3DataCode` is what the "Identificador do Campo" of the *Registro COE*
variable-data record carries. An attribute without one is bookable and validated but cannot be
written to B3, which is why the compiler counts them and the clearing endpoint says so beside
the file it produced.

**At run time.** The worker loads them into `b3.Figure`, `b3.Domain`, `b3.StrategyField`,
`b3.StrategyFieldValue`, `b3.DerivativeField`, `b3.DerivativeFieldValue`, `b3.FigureAttribute`
and `ref.Underlying`, replacing the contents each time — the export is the whole truth, so a row
that disappears from it must disappear from the database. That is what backs the underlying
picker and the `underlyingRegistered` check.

## Refreshing

Normally nothing: the worker pulls the newest published files on its own. To force a pass now,
`POST /api/admin/cetip/sync`. Dropping newer files in by hand still works and is what
`Cetip:Enabled: false` leaves you with. There is no migration either way — the files are the
interface — and `b3.ReferenceLoad` records what was loaded and when.

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

## Two dictionaries, not one

`dados-estrategia.csv` and `dados-derivativo.csv` both key attributes on a `C…` code, and **the
codes do not mean the same thing in the two**. `C0000001` is "% Capital Protegido" in the first
and "Strike 1(%)" in the second. Both are published, and each is authoritative for its own file,
so both are kept and the domain files address them through separate properties — `b3FieldCode`
for the strategy dictionary, `b3DataCode` for the derivative one.

The one the registration writes against is `dados-derivativo.csv`: the *Registro COE* layout
names `DTpTipoDadosDerivativo.txt` for the "Identificador do Campo" of its variable-data record,
and `DTpFigurasDadosDerivativo.txt` says which of its fields belong to which figure. That second
file is the association this repository previously had to read out of the manual's prose annex —
published as data, for all 88 figures, including the four the annex has no entry for at all.
Between them the two exports carry the type, size, decimals, mandatory flag and accepted values
of every attribute, which is why a generated figure no longer depends on parsing an instruction
sentence to learn that a strike is a percentage with eight decimals.

`dados-estrategia.csv` remains what it was: a flat catalogue of 5,503 attributes with no figure
association and its own naming — concept names repeat, 488 of them contain "Strike", and they are
not the names the registration screen uses (`Limitador_Strike Put_1` against `Limitador Cenário
de Alta (%)`). It cannot be used to attach a code to an attribute by name, which is why
`b3FieldCode` is left unset everywhere and validated only where someone sets it deliberately.

**Names match across the two published sources, mostly.** Around four fifths of the annex's field
names appear verbatim in the export for the same figure, once accents, case and the `(%)` suffix
are set aside. The rest differ only in grammar — a preposition, a plural, a word order — so a
second pass compares the words that carry the meaning, and a match is taken only when it is the
only one in the figure. Across B3's catalogue no figure has two attributes that reduce alike,
which is what makes that safe. Between the two passes all 1,647 pairings are matched, so nothing
is copied by hand and nothing is guessed at.

It also settles what the annex only describes. `tools/Coe.DomainGen` used to read an attribute's
type and precision out of a sentence — *"Formato: Numérico percentual com 4 inteiros e 8
decimais"* — which is stated three different ways across the annex and sometimes not at all. It
now takes the type, the precision, the size and the mandatory flag from the export and leaves the
prose to explain the field. That corrected 71 attributes across the generated figures, and turned
several that had fallen back to text into the numbers B3 registers.
