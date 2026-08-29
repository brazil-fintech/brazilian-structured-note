# References

All sources used across this documentation. Official Portuguese-language documents
govern; this repository is a technical summary of them.

## Law and regulation

1. **Law 12,249, of June 11, 2010** — creates the Certificado de Operações Estruturadas
   (conversion of Provisional Measure 472/2009).
   <https://www.planalto.gov.br/ccivil_03/_ato2007-2010/2010/lei/l12249.htm>
2. **CMN Resolution 4,263, of September 5, 2013** — conditions for issuance of the COE:
   eligible issuers, book-entry form, mandatory registration, VNP/VNR modalities,
   eligible reference assets, loss limited to the invested nominal.
   <https://www.bcb.gov.br/estabilidadefinanceira/exibenormativo?tipo=Resolu%C3%A7%C3%A3o&numero=4263>
3. **CVM Instruction 569, of October 14, 2015** *(revoked)* — public offers of COE with
   registration waiver; created the DIE.
   <https://conteudo.cvm.gov.br/legislacao/instrucoes/inst569.html>
4. **CVM Resolution 8, of 2020** — current rule for public distribution of COE with
   automatic waiver; DIE minimum content; distributor duties. Revokes ICVM 569/2015.
   <https://conteudo.cvm.gov.br/legislacao/resolucoes/resol008.html>
5. **CVM Resolution 30, of 2021** — suitability rules applicable to distribution.
   <https://conteudo.cvm.gov.br/legislacao/resolucoes/resol030.html>
6. **Law 11,033, of December 21, 2004** — fixed-income taxation regime (regressive IR
   schedule) applied to COE income.
   <https://www.planalto.gov.br/ccivil_03/_ato2004-2006/2004/lei/l11033.htm>

## Clearing (B3) — see also [clearing/](clearing/README.md)

7. **B3 — Certificado de Operações Estruturadas (COE), product page.**
   <https://www.b3.com.br/pt_br/produtos-e-servicos/registro/operacoes-estruturadas/certificado-de-operacoes-estruturadas-coe.htm>
8. **B3 — Sobre o Certificado de Operações Estruturadas.**
   <https://www.b3.com.br/pt_br/produtos-e-servicos/registro/operacoes-estruturadas/sobre-o-certificado-de-operacoes-estruturadas.htm>
9. **B3 — Manual de Operações — COE** (normative structure page listing current and
   historical versions).
   <https://www.b3.com.br/pt_br/regulacao/estrutura-normativa/estrutura-normativa/manuais-de-operacoes-8ae490ca69088bf00169104ff0ad7417/certificado-de-operacoes-estruturadas-coe/>
   Direct PDF (version current at the time of writing):
   <https://www.b3.com.br/data/files/AA/66/CD/34/EBC309105FE89209AC094EA8/Manual%20de%20Operacoes%20-%20COE.pdf>
   Committed copy: [clearing/manual-de-operacoes-coe-202607.pdf](clearing/manual-de-operacoes-coe-202607.pdf)
   (version dated 20/07/2026 — the version this documentation was checked against).
10. **B3 — Caderno de Fórmulas — COE** (calculation methodology and precision criteria
    for registered parameters).
    <https://www.b3.com.br/data/files/E2/D1/DC/38/839009105391B9F8AC094EA8/CADERNO%20DE%20FORMULAS%20-%20COE.pdf>
    Committed copy: [clearing/caderno-de-formulas-coe-202607.pdf](clearing/caderno-de-formulas-coe-202607.pdf)
    (update dated 21/07/2026 — the version the formulas here were checked against).
11. **B3 — COE: conceito** (product concept note).
    <https://www.b3.com.br/lumis/portal/file/fileDownload.jsp?fileId=8AE490CA6F165E34016F250DCDCF3B40>

## Self-regulation

12. **ANBIMA — Certificados de Operações Estruturadas (COE), regulatory summary.**
    <https://www.anbima.com.br/pt_br/informar/regulacao/informe-de-legislacao/certificados-de-operacoes-estruturadas-coe.htm>
13. **ANBIMA — Código de Distribuição de Produtos de Investimento** (distribution,
    marketing material and DIE presentation standards).
    <https://www.anbima.com.br/pt_br/autorregular/codigos/distribuicao-de-produtos-de-investimento.htm>

## Bibliography (pricing and design)

14. J. C. Hull, *Options, Futures, and Other Derivatives*, 11th ed., Pearson, 2021 —
    ch. 12 (spreads), ch. 26 (exotic options: binaries, barriers, asians).
15. M. Bouzoubaa, A. Osseiran, *Exotic Options and Hybrids: A Guide to Structuring,
    Pricing and Trading*, Wiley, 2010 — autocallables, reverse convertibles, twin win.
16. M. Broadie, P. Glasserman, S. G. Kou, "A Continuity Correction for Discrete Barrier
    Options", *Mathematical Finance* 7(4), 1997 — discrete barrier monitoring adjustment.
17. E. Reiner, M. Rubinstein, "Breaking Down the Barriers", *Risk* 4(8), 1991 — closed
    forms for continuously monitored barrier options.

## Note on links

B3 rotates document URLs when manuals are re-issued; if a direct PDF link breaks, reach
the current version through the normative-structure page (ref. 9) or the product page
(ref. 7). The PDFs kept under [clearing/](clearing/README.md) pin the exact versions this
documentation was written against.
