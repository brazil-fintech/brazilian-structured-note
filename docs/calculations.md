# Calculations

The calculation conventions used by COE payoff formulas and by the fixed-income leg. The
authoritative source for registered instruments is B3's *Caderno de Fórmulas — COE* (see
[clearing/](clearing/README.md)); this document states the standard formulas and worked
examples. Notation: **VN** = unit nominal value; **DU** = business days (dias úteis) on
the B3/ANBIMA calendar; performance and levels are fractions unless written as %.

## 1. Day count and calendar

Brazilian fixed income accrues on **DU/252**: a rate *i* per year compounds over *n*
business days as `(1 + i)^(n/252)`. Business days follow the national holiday calendar
used by B3/ANBIMA. Observation dates falling on non-business days (or disrupted market
days for the underlying) roll per the registered fallback — market standard is *following
business day* for fixings and payments.

## 2. Accrual factors

### 2.1 DI (CDI) factor at a percentage p

The DI over rate `DI_k` (% per year, 252 basis) published by B3 for each business day *k*
converts to a daily factor, applied at the registered percentage *p* (e.g. *p* = 90 for
"90% of CDI"):

```
TDI_k    = (DI_k/100 + 1)^(1/252) − 1                (daily rate)
FatorDI  = ∏_{k=1}^{n} [ 1 + TDI_k × (p/100) ]       (accumulated factor, n business days)
```

**Example** — R$ 1,000 at 90% of CDI for 2 business days with DI at 15.00% p.a.:
`TDI = 1.15^(1/252) − 1 = 0.00055476`; daily factor `1 + 0.00055476×0.9 = 1.00049929`;
after 2 days `1.00049929² = 1.00099883` → R$ 1,000.99.

### 2.2 Pre-fixed factor

```
FatorPre = (1 + i)^(DU/252)          i = annual rate, DU/252 compounding
```

**Example** — 12% p.a. for 378 business days (18 months): `1.12^(378/252) = 1.1853` →
18.53% period return.

### 2.3 Inflation-linked factor (IPCA)

The Caderno de Fórmulas computes the IPCA leg on the **índice-number ratio** with a
participation percentage *p*, not by compounding monthly variations:

```
FatorIPCA = 1 + ( NI_n / NI_0 − 1 ) × p/100
```

where `NI_n` and `NI_0` are the IPCA index numbers of the month immediately before the
end and start dates (M−1); if the index has not been published by the day before the
date, the previous month's number (M−2, the last known) is used. A fixed spread
("IPCA + i") enters as a separate `FatorSPREAD` on the registered `Spread/Cupom` and
`Base` (see 2.2 and the 360-day bases below).

### 2.4 Day-count bases and the spread factor

The registered `Base` of a Spread/Cupom admits three conventions (Caderno de Fórmulas,
"Informações Adicionais"):

```
252 exp:  FatorSPREAD = (1 + TXPRE/100)^(du/252)      du = business days
360 exp:  FatorSPREAD = (1 + TXPRE/100)^(dc/360)      dc = calendar days
360 lin:  FatorSPREAD = 1 + TXPRE/100 × dc/360
```

For a DI remunerator the full factor is `FatorJUROS = FatorDI × FatorSPREAD`.

### 2.5 Rounding and precision

Official criteria (Caderno de Fórmulas — COE, the committed copy in
[clearing/](clearing/README.md)):

- **Payoff-figure calculations:** intermediate results **rounded to 16 decimal places**;
  the financial value (Valor Financeiro) **truncated to 2 decimal places**.
- **Remuneration factors:** daily `TDI_k` calculated with **8 decimals, rounded**;
  `FatorDI` accumulated with **8 decimals, rounded**; `FatorSPREAD` with **9 decimals,
  rounded**; `FatorJUROS = FatorDI × FatorSPREAD` with 9 decimals.

Implementations should follow the handbook exactly — differences in rounding are a
classic source of 1-cent breaks in settlement.

## 3. Performance of the underlying

### 3.1 Point-to-point

```
Perf = S_final / S_inicial − 1
```

- `S_inicial` (S₀): fixing on the initial observation date (or average of the first *n*
  scheduled fixings).
- `S_final` (S_T): fixing on the final observation date (or average — "asian tail" — of
  the last *n* scheduled fixings, which reduces the cost of the option and the
  sensitivity to a single closing print).

### 3.2 Averaging

```
S_avg = (1/n) × Σ_{j=1}^{n} S(t_j)
```

Any of S₀ / S_T may be an average; the registration lists the exact dates `t_j`.

### 3.3 Barrier and digital observation

For a level *H* (as a fraction of S₀):

| Convention | Condition checked |
|---|---|
| European | only `S_T` vs `H·S₀` at the final fixing |
| Discrete | `S(t_i)` vs `H·S₀` on each scheduled observation date |
| Continuous | intraday/daily prices on every trading day of the window |

Standard comparisons are `≥` for up-barriers/triggers and `<` for down-barrier breaches
(so touching a protection barrier exactly does not breach it) — but the registered
formula's inequality governs; the payoff documents here state the convention drawn in
each figure.

In B3's registration screens (*Manual de Operações — COE*), barrier observation is the
field `Período de Verificação de Barreiras`, with two domains: **Europeia** (a single
`Data para Fixing`) and **Americana** (verification on every day between a registered
start and end date, using a registered quote type — Fechamento, Média, Máximo, Mínimo or
Ajuste). A discrete schedule is achieved with the *Mais Datas* fixing mechanism, and the
final fixing itself can be a single date, a 1–5-business-day window (max/min/mean), or an
explicit list of dates (see [parameters.md](parameters.md#2-underlying-ativo-subjacente-fields)).

### 3.4 FX conversion for offshore underlyings

- **Quanto**: `Perf` computed in the underlying's local currency; payoff applied to VN in
  BRL. No FX exposure to the investor; the issuer hedges the correlation (quanto
  adjustment priced in).
- **Composite (dollar-linked)**: `Perf = (S_T × FX_T)/(S₀ × FX₀) − 1` with FX = PTAX
  (BCB) or the registered source — the investor carries both equity and FX performance.

## 4. Redemption formulas

Each payoff document under [payoffs/](payoffs/README.md) gives its full formula in the
simplified form `Redemption = VN × [ FatorBase + VariablePayoff ]`, with
`FatorBase ≥ 1` for a protected note and `Redemption = VN × PayoffFactor ≥ 0` for a VNR
note (loss capped at the invested nominal by CMN Resolution 4,263/2013).

The Caderno de Fórmulas writes every registered figure on one master template:

```
VResg = Máx[ { PAccruado × BaseOp + Posi × OptionResult × ΔC } ; { P × CG } ]
```

- `P` — Valor Financeiro de Emissão (the invested principal);
- `PAccruado` — `P` accrued by the registered Remunerador, if any;
- `BaseOp` — the Base Aplicação percentage (the protected-leg base);
- `Posi` — the issuer's registered position in the derivative (−1 comprado, +1 vendido —
  the sign that makes the option result flow to the investor);
- `OptionResult` — the figure's payoff, built from `(S − X_i) × Qtde_i × PercAA/AB`
  terms with `Qtde_i = P / X_i` (so `(S − X_i) × Qtde_i = P × (S/X_i − 1)`), digital
  coupons `RemAd × P`, or rebates `KO/KI × P`;
- `ΔC` — the FX (quanto) variation factor;
- `CG` — the registered Capital Garantido percentage: the whole expression is floored at
  `P × CG`, which is how both modalities are enforced in calculation.

## 5. Valuation and mark-to-market

- **Theoretical value** at any date = PV of the funding leg + value of the option package:

```
V_t = VN × DF(t,T) × E_Q[ PayoffFactor ]        (risk-neutral expectation)
```

  discounted on the DI curve plus the issuer's credit spread. Vanilla legs (calls, puts,
  spreads) price by Black–Scholes/Black-76; barriers, autocalls and range accruals price
  by closed forms where available or Monte Carlo / PDE on the registered observation
  schedule (see Hull, ch. 26 – exotic options).
- **Reference prices**: B3 publishes daily reference prices for registered COEs (used in
  custody statements); the issuer's buyback bid is its own market-making price, typically
  reference value minus a spread.
- **Scenario tables in the DIE** are *not* valuations: they show redemption outcomes
  under fixed hypothetical underlying paths, as required by CVM Resolution 8/2020.

## 6. Worked end-to-end example (capital-protected call)

3-year (756 DU) VNP note on IBOVESPA, VN = R$ 1,000, participation 70%, S₀ = 120,000
(final fixing = average of the last 3 monthly closings).

1. **Issue economics** (illustrative): DI curve at ~10% p.a. ⇒ zero-coupon leg
   PV = 75.1% of VN; budget = 24.9%; ATM 3y call on IBOV costs 30.0% of notional ⇒
   affordable participation before margin = 24.9/30.0 ≈ 83%; issuer keeps 3.9% ⇒ offered
   participation (24.9 − 3.9)/30.0 = 70%.
2. **Scenario up**: S_avg = 150,000 ⇒ `Perf = 25%` ⇒ redemption
   `1,000 × [1 + 0.70×0.25] = R$ 1,175.00` (17.5% over 3 years).
3. **Scenario down**: S_avg = 96,000 ⇒ `Perf = −20%` ⇒ `max(Perf,0) = 0` ⇒ redemption
   `R$ 1,000.00` (0% nominal return — the cost of protection is the forgone CDI).
4. **Tax**: > 720 days ⇒ 15% withholding on the R$ 175.00 gain ⇒ net R$ 1,148.75.

## References

- B3, *Caderno de Fórmulas — COE* — official formulas and precision criteria (update
  dated 21/07/2026, committed at
  [clearing/caderno-de-formulas-coe-202607.pdf](clearing/caderno-de-formulas-coe-202607.pdf)).
- B3, *Manual de Operações — COE* — observation/settlement mechanics.
- J. Hull, *Options, Futures, and Other Derivatives*, 11th ed. — ch. 26 (exotic options)
  for barrier/digital/asian pricing.
- Full list in [references.md](references.md).
