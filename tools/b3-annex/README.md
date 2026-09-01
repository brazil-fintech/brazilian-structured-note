# Figure-attribute annex extraction

`extract.py` pulls the **"Anexo — Descrição dos campos das figuras"** out of B3's *Manual de
Operações — COE* and writes it as [`reference/b3/campos-figuras.csv`](../../reference/b3/README.md#the-figure-attribute-annex).

That annex is the only published source saying which attributes belong to which payoff figure —
B3's `DTpDadosEstrategia` export is a flat dictionary with no figure association and its own
naming — so the platform cannot build a figure's form without it.

```bash
pip install pypdf
python3 tools/b3-annex/extract.py \
    docs/clearing/manual-de-operacoes-coe-202607.pdf \
    reference/b3/campos-figuras.csv
```

Run it when a new manual version is committed, then regenerate the domain files:

```bash
dotnet run --project tools/Coe.DomainGen
dotnet test tests/Coe.Tests
```

`AnnexTests` guards the output — the figure count, the four figures B3 has withdrawn, and a
sample figure's attributes — so a re-run that quietly loses rows fails the suite instead of
shrinking the forms.

## Why it is not a straight text dump

The annex is a two-column table, and `pypdf`'s plain text extraction interleaves the columns.
Layout mode keeps them apart as character offsets, so the script:

- locates the description column per page from the indentation histogram — B3 re-flows the table
  across 200 pages, and a fixed offset starts splitting words in half a few dozen pages in;
- treats a line whose text crosses that offset without a space as a full-width note, not a row;
- opens a table row on a blank line, or on a line whose description begins
  "Campo de preenchimento…" — later pages centre the field name against its description, so the
  row starts on a line whose name column is still empty;
- splits a numbered family that shares one description cell ("Data de Observação 3 4 5") back
  into one row per member.

About 5% of rows still come out as merged prose where several names share a cell. The generator
recognises those as notes and leaves them out rather than inventing an attribute.
