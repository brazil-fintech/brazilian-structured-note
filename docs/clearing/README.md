# Clearing documents (B3)

This folder holds the official B3 documents that govern registration, lifecycle and
calculation of the COE. They are the authoritative source for everything summarized in
[../parameters.md](../parameters.md) and [../calculations.md](../calculations.md).

## Documents to keep here

| File (naming convention) | Document | Official source |
|---|---|---|
| `manual-de-operacoes-coe-<YYYYMM>.pdf` | **Manual de Operações — COE** — registration screens/fields, payoff figures, lifecycle events (issuance, coupon payment, early redemption, buyback, maturity), settlement | [B3 normative structure page](https://www.b3.com.br/pt_br/regulacao/estrutura-normativa/estrutura-normativa/manuais-de-operacoes-8ae490ca69088bf00169104ff0ad7417/certificado-de-operacoes-estruturadas-coe/) · [direct PDF](https://www.b3.com.br/data/files/AA/66/CD/34/EBC309105FE89209AC094EA8/Manual%20de%20Operacoes%20-%20COE.pdf) |
| `caderno-de-formulas-coe-<YYYYMM>.pdf` | **Caderno de Fórmulas — COE** — calculation methodology and precision criteria for updating the parameters of a registered COE (DI/pre/IPCA factors, rounding rules) | [direct PDF](https://www.b3.com.br/data/files/E2/D1/DC/38/839009105391B9F8AC094EA8/CADERNO%20DE%20FORMULAS%20-%20COE.pdf) |
| `manual-de-normas-coe-<YYYYMM>.pdf` | **Manual de Normas — COE** — the rulebook binding participants on registration and custody of the certificate | via the [B3 normative structure page](https://www.b3.com.br/pt_br/regulacao/estrutura-normativa/estrutura-normativa/manuais-de-operacoes-8ae490ca69088bf00169104ff0ad7417/certificado-de-operacoes-estruturadas-coe/) |
| `coe-conceito-<YYYYMM>.pdf` | **COE — Conceito** — B3's product concept note | [download link](https://www.b3.com.br/lumis/portal/file/fileDownload.jsp?fileId=8AE490CA6F165E34016F250DCDCF3B40) |

> **Status:** the PDFs are not yet committed — this repository was bootstrapped from an
> environment whose network policy blocks `b3.com.br`, so the files could not be
> downloaded here. Download them from the links above (they are public) and commit them
> with the naming convention in the table, suffixing the version date stamped on the
> document's cover.

## Versioning rules

- Keep the version date in the filename; when B3 re-issues a manual, **add** the new file
  and keep the old one — registered certificates follow the manual in force at
  registration.
- Update the direct links in [../references.md](../references.md) when B3 rotates URLs
  (they change on every re-issue; the normative-structure page above always lists the
  current version).
- Anything in this folder is © B3 and redistributed here for reference only; the
  documents are publicly available on <https://www.b3.com.br>.
