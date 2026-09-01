#!/usr/bin/env python3
"""
Extract the "Anexo - Descricao dos campos das figuras" table out of B3's
*Manual de Operacoes - COE* into a CSV the build can read.

The annex is the only published source that says which fields belong to which payoff figure:
the DTpDadosEstrategia export is a flat dictionary of 5,503 attributes with no figure
association, so the mapping cannot be recovered from the CSV exports alone.

The annex is a two-column table (field name | description) with a heading per figure. Text is
taken in layout mode, which preserves the columns as character offsets, and the description
column is located per page from the indentation histogram -- B3 re-flows the table across the
annex, so a fixed offset splits words in half a few hundred pages in.

Run by hand when a new manual version is committed:

    python3 tools/b3-annex/extract.py \
        docs/clearing/manual-de-operacoes-coe-202607.pdf \
        reference/b3/campos-figuras.csv
"""
from __future__ import annotations

import csv
import re
import sys
from collections import Counter
from dataclasses import dataclass, field as dc_field

from pypdf import PdfReader

ANNEX_TITLE = "Anexo – Descrição dos campos das figuras"

# "Dados Especificos - COE001001 - Call", "Dados especificos - Figura COE 001044 - Podium",
# and the pair heading "... COE001011 Digital Call e COE001012 - Digital Put".
HEADING = re.compile(r"^\s*Dados\s+[Ee]spec[ií]ficos\b(?P<rest>.*)$")
FIGURE_CODE = re.compile(r"COE\s?0?(\d{6})")

# Running headers, footers and the table's own column headings.
NOISE = (
    "INFORMAÇÃO PÚBLICA",
    "COE – Certificado de Operações Estruturadas",
    "Descrição dos campos Específicos",
    ANNEX_TITLE,
)

# The table's own column headings, repeated at the top of every page of the annex.
TABLE_HEADER = re.compile(r"^\s*Campo\s{2,}Descrição\s*$")

# Almost every description opens with one of these, which is what tells rows apart where B3
# dropped the blank line between them. Deliberately narrow: "Campo com as opções…" also opens
# a description, but it just as often continues one, and treating it as a row start chops a
# wrapped label ("Período de captura do | ativo subjacente para liquidação") into two fields.
ROW_OPENER = re.compile(
    r"^(Campo (de preenchimento|obrigatório|opcional)|Nesta figura|Neste campo)", re.IGNORECASE
)


@dataclass
class FigureFields:
    code: str
    title: str
    fields: list[list] = dc_field(default_factory=list)   # [label, [description lines]]


# "Data de Observação 3 Data de Observação 4 Data de Observação 5" -- a numbered family that
# shares one description cell, so B3 sets the names side by side in the label column.
REPEATED_FAMILY = re.compile(r"^(?P<stem>.+?)\s*(?P<first>\d+)(?:\s+(?P=stem)\s*\d+)+$")


def split_repeated(label: str) -> list[str]:
    """One label per member of a numbered family sharing a description; otherwise the label."""
    match = REPEATED_FAMILY.match(label)
    if not match:
        return [label]

    stem = match.group("stem")
    numbers = re.findall(rf"{re.escape(stem)}\s*(\d+)", label)
    return [f"{stem} {number}" for number in numbers]


def is_noise(line: str) -> bool:
    stripped = line.strip()
    return (
        not stripped
        or stripped.isdigit()
        or bool(TABLE_HEADER.match(line))
        or any(n in line for n in NOISE)
    )


def description_column(lines: list[str]) -> int | None:
    """
    Character offset where the description column starts on this page.

    Both columns wrap, so the description's left edge is the smallest indent that recurs --
    bulleted sub-lists inside a description sit further right and must not win the vote.
    """
    indents = Counter(
        len(line) - len(line.lstrip())
        for line in lines
        if not is_noise(line)
    )
    candidates = [indent for indent, count in indents.items() if indent > 10 and count >= 3]
    return min(candidates) if candidates else None


def parse(pdf_path: str) -> list[FigureFields]:
    reader = PdfReader(pdf_path)
    figures: list[FigureFields] = []
    current: list[FigureFields] = []   # one heading may name two figures sharing a table
    in_annex = False
    boundary: int | None = None

    for page in reader.pages:
        text = page.extract_text(extraction_mode="layout")

        # The manual shows the same table earlier as a screen example, and the title also
        # appears in the table of contents (with a dot leader) -- only the annex itself is wanted.
        if not in_annex:
            if not any(ANNEX_TITLE in line and "...." not in line for line in text.split("\n")):
                continue
            in_annex = True

        lines = text.split("\n")
        boundary = description_column(lines) or boundary
        if boundary is None:
            continue

        # Both columns of a table row wrap, so a line carrying label text is only a NEW field
        # when it opens a block: B3 separates rows with a blank line, and a long description
        # continues past one with its label column empty.
        starts_block = True

        for line in lines:
            if not line.strip():
                starts_block = True
                continue
            if is_noise(line):
                continue

            heading = HEADING.match(line)
            if heading:
                codes = FIGURE_CODE.findall(line)
                if codes:
                    title = heading.group("rest").strip(" -–—")
                    current = [FigureFields(f"COE{code}", title) for code in codes]
                    figures.extend(current)
                # "Dados Específicos da figura" carries no code: it resumes the figure above,
                # after an interleaved "Campos Fixos" block.
                starts_block = True
                continue

            if not current:
                continue

            # A note that spans the full width of the table is not two columns: the character
            # before the boundary and the one at it both carry text. Splitting it there would
            # cut a word in half and invent a field out of the left fragment.
            if len(line) > boundary and line[boundary - 1] != " " and line[boundary] != " ":
                for figure in current:
                    if figure.fields:
                        figure.fields[-1][1].append(line.strip())
                starts_block = False
                continue

            label, description = line[:boundary].strip(), line[boundary:].strip()

            # Later in the annex B3 centres the field name against its description, so the row
            # opens on a line whose label column is empty and the name arrives underneath. A
            # description-only line therefore starts a row when it reads like the beginning of
            # one, and continues the row above when it does not.
            opens_row = starts_block and (label or ROW_OPENER.match(description))
            wraps_row = not starts_block and label and ROW_OPENER.match(description)

            for figure in current:
                if opens_row or not figure.fields or (wraps_row and figure.fields[-1][1]):
                    figure.fields.append(["", []])

                if label:
                    figure.fields[-1][0] = f"{figure.fields[-1][0]} {label}".strip()
                if description:
                    figure.fields[-1][1].append(description)

            starts_block = False

    return figures


def main() -> int:
    if len(sys.argv) != 3:
        print(__doc__)
        return 2

    figures = parse(sys.argv[1])

    with open(sys.argv[2], "w", newline="", encoding="utf-8") as handle:
        writer = csv.writer(handle, delimiter=";", lineterminator="\n")
        writer.writerow(["FIGURA", "TITULO", "ORDEM", "CAMPO", "DESCRICAO"])
        for figure in figures:
            # A block that collected description text but never a name is a note, not a field.
            named = [f for f in figure.fields if f[0].strip()]
            ordinal = 0
            for label, description in named:
                text = " ".join(" ".join(description).split())
                for name in split_repeated(" ".join(label.split())):
                    ordinal += 1
                    writer.writerow([figure.code, " ".join(figure.title.split()), ordinal, name, text])

    print(f"{len(figures)} figures, {sum(len([x for x in f.fields if x[0].strip()]) for f in figures)} field rows")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
