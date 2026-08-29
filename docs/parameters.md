# Parameters of a COE

Every parameter that defines a COE, in two layers:

1. **Registration fields** — the instrument-level data registered at B3 (per the
   [Manual de Operações — COE](clearing/README.md));
2. **Payoff parameters** — the fields specific to each payoff figure, referenced by the
   registered formula (per the [Caderno de Fórmulas — COE](clearing/README.md)).

Cross-references: how each parameter enters the calculation is in
[calculations.md](calculations.md); which parameters each structure uses is in each
payoff document under [payoffs/](payoffs/README.md).

## 1. Identification and instrument-level fields

| Field (PT-BR) | Field (EN) | Description |
|---|---|---|
| Emissor | Issuer | Issuing bank (multiple/commercial/investment bank or caixa econômica), identified by CNPJ. |
| Registrador | Registrar | B3 — registration is mandatory; issuance is exclusively book-entry. |
| Código do instrumento | Instrument code | Identifier assigned at registration (and ISIN where applicable). |
| Data de emissão | Issue date | Date the certificate is created and the investor's cash settles. |
| Data de início de rentabilidade | Return start date | Date remuneration starts to accrue (usually = issue date). |
| Data de vencimento | Maturity date | Final settlement date of the certificate. |
| Valor nominal unitário (VN) | Unit nominal value | Base amount per certificate on which the payoff formula applies (e.g. R$ 1,000.00). |
| Quantidade | Quantity | Number of certificates in the issuance/subscription. |
| Preço unitário de emissão | Issue price | Normally 100% of VN; the formula pays as % of VN. |
| Modalidade | Modality | **VNP** (Valor Nominal Protegido) or **VNR** (Valor Nominal em Risco) — see [overview.md](overview.md#3-modalities). |
| Figura de payoff | Payoff figure | Which registered payoff structure applies (see [payoffs/](payoffs/README.md)); determines which payoff fields below are required. |
| Moeda de liquidação | Settlement currency | BRL — offshore underlyings are converted or quantoed per the registered FX convention. |
| Público-alvo / DIE | Target investor / DIE | Distribution data: DIE identifier/version, target audience, suitability category (CVM Resolution 8/2020). |

## 2. Underlying (ativo subjacente) fields

| Field | Description |
|---|---|
| Tipo de ativo | Class of underlying: equity index, single stock, basket, FX rate, interest rate/index (DI, pre), inflation index (IPCA/IGP-M), commodity, offshore index/stock, fund/ETF. CMN Resolution 4,263/2013 requires prices/indices with public, regular and verifiable disclosure. |
| Identificação do ativo | Ticker / index name / currency pair, and for baskets each component and its weight. |
| Fonte de apuração | Fixing source: B3 closing price, BCB PTAX (option D2 sale rate is the market standard for USDBRL), index sponsor's official close, or a named information-vendor page. |
| Preço inicial (S₀) | Initial price/strike reference: the fixing on the initial observation date (possibly an average of the first *n* fixings). |
| Datas/horários de apuração | Observation schedule: initial date, periodic dates (autocall/coupon/range), final date; the fixing time follows the source's official publication. |
| Tratamento cambial | For offshore underlyings: **quanto** (payoff computed on the underlying's local-currency performance, paid in BRL with no FX effect) or **composite/dollar-linked** (BRL performance includes the FX variation). |
| Eventos de ajuste | Fallback and adjustment rules: corporate actions on stocks, index discontinuation/substitution, market disruption days (roll to next business day per the fixing source's calendar). |

## 3. Remuneration (fixed-income leg) fields

| Field | Description |
|---|---|
| Indexador base | The base accrual when the payoff pays "the nominal accrued at X": % of DI (CDI), fixed rate (pre), or inflation index + fixed rate (e.g. IPCA + x% p.a.). |
| Percentual do indexador (p) | e.g. 90 (% of CDI) — enters the DI factor formula in [calculations.md](calculations.md#2-accrual-factors). |
| Taxa pré / cupom fixo (i) | Annual rate, DU/252 compounding, for pre-fixed legs and fixed coupons. |
| Base de contagem | Business days / 252 (DU/252) on the B3/ANBIMA holiday calendar — the Brazilian fixed-income convention. |

## 4. Payoff parameters (per figure)

The registered formula of each payoff figure references a subset of these fields. The
"used by" column points at the payoff documents.

| Parameter (PT-BR) | Symbol | Description | Used by |
|---|---|---|---|
| Participação | *Part* | Multiplier on the underlying performance passed to the investor (may be <, = or > 100%). | [call-participation](payoffs/call-participation.md), [call-spread](payoffs/call-spread.md), [put-spread](payoffs/put-spread.md), [shark-fin](payoffs/shark-fin.md), [twin-win](payoffs/twin-win.md) |
| Alavancagem / boost | *B* | Leverage factor on the upside of a booster (e.g. 2×). | [booster](payoffs/booster.md) |
| Cap (teto) | *Cap* | Maximum performance considered by the formula (caps the gain). | [call-spread](payoffs/call-spread.md), [put-spread](payoffs/put-spread.md), [booster](payoffs/booster.md) |
| Piso (floor de performance) | *Floor* | Minimum performance considered (bounds a loss leg). | VNR variants |
| Strike (preço de exercício) | *K* | Performance/price level from which the option leg starts paying; usually 100% of S₀ but may be set above/below. | all directional payoffs |
| Cupom / rebate digital | *C* | Fixed coupon paid when a digital condition verifies; rebate paid on knock-out. | [digital](payoffs/digital-duplo-indexador.md), [shark-fin](payoffs/shark-fin.md), [autocall-athena](payoffs/autocall-athena.md), [autocall-phoenix](payoffs/autocall-phoenix.md), [reverse-convertible](payoffs/reverse-convertible.md), [range-accrual](payoffs/range-accrual.md) |
| Memória de cupom | — | Whether missed coupons are recovered when a later coupon condition verifies. | [autocall-phoenix](payoffs/autocall-phoenix.md) |
| Barreira (nível) | *H* | Barrier level as % of S₀ (e.g. 70%, 130%). | barrier payoffs |
| Tipo de barreira | — | **Knock-in** (activates a leg) or **knock-out** (extinguishes a leg); direction **up** or **down**. | [shark-fin](payoffs/shark-fin.md) (up-and-out), [reverse-convertible](payoffs/reverse-convertible.md) / [autocalls](payoffs/autocall-athena.md) / [twin-win](payoffs/twin-win.md) (down-and-in) |
| Observação da barreira | — | **European** (final fixing only), **discrete** (scheduled dates), or **continuous** (any trading day). Materially changes risk and price. | barrier payoffs |
| Trigger de autocall | *T<sub>i</sub>* | Level (usually 100% of S₀, sometimes step-down) that causes automatic early redemption on observation date *i*. | [autocall-athena](payoffs/autocall-athena.md), [autocall-phoenix](payoffs/autocall-phoenix.md) |
| Barreira de cupom | *H<sub>c</sub>* | Level above which the periodic coupon is paid. | [autocall-phoenix](payoffs/autocall-phoenix.md) |
| Datas de observação | *t₁…t<sub>n</sub>* | Schedule of autocall/coupon/range observations (e.g. semi-annual). | autocalls, [range-accrual](payoffs/range-accrual.md) |
| Range (banda) | *[L, U]* | Lower/upper bounds of the accrual range. | [range-accrual](payoffs/range-accrual.md) |
| Média (asiática) | — | Number and schedule of fixings averaged for S₀ and/or S<sub>T</sub>. | any figure may average fixings |

## 5. Early exit and settlement fields

| Field | Description |
|---|---|
| Resgate antecipado automático | Autocall provisions: dates, triggers, redemption formula per date (see autocall payoffs). |
| Recompra / revenda | Whether and how the issuer offers early buyback at market value (protection does not apply before maturity). |
| Datas de pagamento | Payment dates for interim coupons and final settlement (D+ convention on the B3 cash window). |
| Preço de referência / MtM | B3 discloses reference prices used for statements and portfolio marking (see [calculations.md](calculations.md#5-valuation-and-mark-to-market)). |

## References

- B3, *Manual de Operações — COE* and *Caderno de Fórmulas — COE* — the authoritative
  field-by-field registration and calculation reference: see [clearing/](clearing/README.md).
- CMN Resolution 4,263/2013, arts. 2–9 (registration content, eligible underlyings,
  modalities).
- CVM Resolution 8/2020, Annex — DIE minimum content (the DIE mirrors these parameters).
- Full list in [references.md](references.md).
