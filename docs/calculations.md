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
`TDI = 1.15^(1/252) − 1 = 0.00055482`; daily factor `1 + 0.00055482×0.9 = 1.00049934`;
after 2 days `1.00049934² = 1.00099893` → R$ 1,000.99.

### 2.2 Pre-fixed factor

```
FatorPre = (1 + i)^(DU/252)          i = annual rate, DU/252 compounding
```

**Example** — 12% p.a. for 378 business days (18 months): `1.12^(378/252) = 1.1853` →
18.53% period return.

### 2.3 Inflation-linked factor (IPCA + i)

```
FatorIPCA = [ ∏_m (1 + IPCA_m) ] × (1 + i)^(DU/252)
```

with monthly IPCA variations pro-rated (by DU) in the first and last months, per the
Caderno de Fórmulas' precision rules.

### 2.4 Rounding and precision

B3's Caderno de Fórmulas fixes the precision of each intermediate step (e.g. daily DI
factors truncated/rounded to 16 decimal places, accumulated factors to 8, unit prices to
8, final settlement values to 2). Implementations should follow the handbook exactly —
differences in rounding are a classic source of 1-cent breaks in settlement.

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

### 3.4 FX conversion for offshore underlyings

- **Quanto**: `Perf` computed in the underlying's local currency; payoff applied to VN in
  BRL. No FX exposure to the investor; the issuer hedges the correlation (quanto
  adjustment priced in).
- **Composite (dollar-linked)**: `Perf = (S_T × FX_T)/(S₀ × FX₀) − 1` with FX = PTAX
  (BCB) or the registered source — the investor carries both equity and FX performance.

## 4. Redemption formulas

Each payoff document under [payoffs/](payoffs/README.md) gives its full formula. The
common template for a VNP note is:

```
Redemption = VN × [ FatorBase + VariablePayoff ]
```

where `FatorBase ≥ 1` (at minimum the protected nominal; optionally an accrual such as a
% of CDI in the adverse scenario) and `VariablePayoff` is the option-package result (e.g.
`Part × max(Perf, 0)`). For a VNR note the template is `Redemption = VN × PayoffFactor`
with `0 ≤ PayoffFactor` (loss capped at the invested nominal by CMN Resolution
4,263/2013).

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

1. **Issue economics** (illustrative): DI curve ⇒ zero-coupon leg PV = 87.8% of VN;
   budget = 12.2%; ATM 3y call on IBOV costs 13.1% ⇒ affordable participation before
   margin = 12.2/13.1 = 93%; issuer keeps 3.0% ⇒ offered participation
   (12.2 − 3.0)/13.1 ≈ 70%.
2. **Scenario up**: S_avg = 150,000 ⇒ `Perf = 25%` ⇒ redemption
   `1,000 × [1 + 0.70×0.25] = R$ 1,175.00` (17.5% over 3 years).
3. **Scenario down**: S_avg = 96,000 ⇒ `Perf = −20%` ⇒ `max(Perf,0) = 0` ⇒ redemption
   `R$ 1,000.00` (0% nominal return — the cost of protection is the forgone CDI).
4. **Tax**: > 720 days ⇒ 15% withholding on the R$ 175.00 gain ⇒ net R$ 1,148.75.

## References

- B3, *Caderno de Fórmulas — COE* — official formulas and precision criteria
  ([clearing/](clearing/README.md)).
- B3, *Manual de Operações — COE* — observation/settlement mechanics.
- J. Hull, *Options, Futures, and Other Derivatives*, 11th ed. — ch. 26 (exotic options)
  for barrier/digital/asian pricing.
- Full list in [references.md](references.md).
