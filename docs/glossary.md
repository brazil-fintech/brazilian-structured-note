# Glossary (PT-BR ↔ EN)

## Product and market

| Portuguese | English | Notes |
|---|---|---|
| Certificado de Operações Estruturadas (COE) | Structured note (certificate of structured operations) | The Brazilian wrapper; single, indivisible set of rights and obligations |
| Valor Nominal Protegido (VNP) | Principal-protected modality | Floor of 100% of nominal at maturity, subject to issuer credit |
| Valor Nominal em Risco (VNR) | Principal-at-risk modality | Loss limited to the invested nominal (no leverage beyond it) |
| Valor nominal (VN) | Nominal / face value | Base for the payoff formula |
| Documento de Informações Essenciais (DIE) | Key information document (KID) | Required by CVM Resolution 8/2020 |
| Ativo subjacente | Underlying asset | Index, stock, FX, rate, inflation, commodity, basket |
| Figura de payoff | Payoff figure / structure | Registered payoff type at B3 |
| Participação | Participation | Multiplier on the underlying performance |
| Alavancagem | Leverage / boost | Booster upside factor |
| Cap / teto | Cap | Maximum performance considered |
| Barreira (knock-in / knock-out) | Barrier (KI/KO) | Activates / extinguishes an option leg |
| Barreira de proteção | Protection barrier | Level below which downside is transferred to the investor |
| Cupom | Coupon | Fixed or conditional payment |
| Memória de cupom | Coupon memory | Missed coupons recovered when a later condition verifies |
| Rebate | Rebate | Small fixed payment on knock-out |
| Resgate antecipado | Early redemption | Autocall or negotiated |
| Recompra / revenda | Issuer buyback / resale | Early exit at market value |
| Data de observação / apuração | Observation / fixing date | Schedule of fixings |
| Duplo indexador | Dual indexer | Digital switching between a fixed rate and a % of CDI |
| Trava de alta / baixa | Call spread / put spread | |
| Dias úteis (DU) | Business days | DU/252 day count |
| CDI / Taxa DI | Interbank deposit (DI) rate | Brazilian overnight benchmark, published by B3 |
| PTAX | PTAX | BCB's official USDBRL fixing rate |
| Emissor | Issuer | Bank issuing the COE |
| Registradora / mercado de balcão | Registrar / OTC registration venue | B3 |
| Fundo Garantidor de Créditos (FGC) | Deposit insurance fund | Does **not** cover COE |
| Come-cotas | Periodic fund tax anticipation | Not applicable to COE |
| Escritural | Book-entry | COE has no physical certificate |
| Caderno de Fórmulas | Formula handbook | B3 calculation methodology document |
| Manual de Operações | Operations manual | B3 registration/lifecycle document |

## Registration and clearing

The vocabulary of the registration itself — the words the booking screen, the upload files and
[platform.md](platform.md) use.

| Portuguese | English | Notes |
|---|---|---|
| Figura (código de figura) | Payoff figure (figure code) | `COE001001`–`COE001088`: the option package a COE is registered as. Named after the derivative, not the commercial structure |
| Dados Específicos | Figure-specific data | The attributes a given figure registers, over and above the common registration fields |
| Registro COE | COE registration record | Operation `0001` of *Enviar Arquivos* §4.8 — the file that registers the certificate |
| Enviar Arquivos | File transfer (upload) | B3's batch interface; the *Manual de Transferência de Arquivos* prints its fixed-width layouts |
| Fluxo de Caixa | Cash-flow schedule | The interim events (coupons) of a note, registered as their own file (FLUX) |
| Nome Simplificado / mnemônico | Participant short name | The registrant's mnemonic, carried in every upload header |
| Código IF | Instrument code | The certificate's identifier at B3; an operator fills it in after registration |
| Nome Fantasia | Commercial name | The free-text title of the note |
| Capital Garantido (%) | Guaranteed capital | What the redemption is floored at: ≥ 100% for VNP, 0 to < 100% for VNR |
| Base Aplicação (%) | Protected-leg base | The share of the issued financial value carried by the funding leg |
| Período de Verificação de Barreiras | Barrier verification period | **Europeia** (one fixing date) or **Americana** (every day in a registered window) |
| Data de fixing / apuração | Fixing date | A date the underlying is captured on; *Mais Datas* registers an explicit schedule (DTFX) |
| Cesta | Basket | A multi-component underlying, registered component by component with weights (CEST) |
| Figura calculada pela B3 | Figure calculated by B3 | Whether B3 itself computes the redemption of the figure, or the issuer reports it |
| Indicação de Disparo de Trigger | Trigger indication | The command that fires an autocall's early redemption |
