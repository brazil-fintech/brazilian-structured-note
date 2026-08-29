# Payoff catalog

The standard payoff structures ("figuras de payoff") of the Brazilian COE market. Each
document contains: description, modality, parameter table, redemption formula, worked
example, scenario table, the payoff drawing, the option decomposition, and references.

The formulas write redemption as a percentage of the unit nominal value **VN**, with
`Perf = S_T/S₀ − 1` per the observation conventions in
[../calculations.md](../calculations.md). Figures are drawn with illustrative parameter
values (each page states them); registered terms of an actual certificate govern.

## Capital protected (Valor Nominal Protegido)

| Payoff | One-line payoff | Option package (on top of the zero-coupon leg) |
|---|---|---|
| [Call with participation](call-participation.md) | `100% + Part × max(Perf, 0)` | long ATM call × Part |
| [Call spread](call-spread.md) | `100% + Part × min(max(Perf,0), Cap)` | long call K, short call K+Cap |
| [Put spread](put-spread.md) | `100% + Part × min(max(−Perf,0), Cap)` | long put K, short put K−Cap |
| [Digital / duplo indexador](digital-duplo-indexador.md) | `100% + C` if `S_T ≥ K`, else base accrual | long cash-or-nothing call |
| [Shark fin](shark-fin.md) | participation until KO barrier; rebate if KO | long up-and-out call + KO rebate |
| [Range accrual](range-accrual.md) | `100% + C_max × n_in/N` | strip of daily double-no-touch digitals |
| [Autocall Athena](autocall-athena.md) | early `100% + i×C` at trigger; barrier-protected at maturity | autocall strip + down-and-in put (VNR variant) |

## Capital at risk (Valor Nominal em Risco)

| Payoff | One-line payoff | Option package |
|---|---|---|
| [Autocall Phoenix](autocall-phoenix.md) | periodic coupons above barrier (memory), autocall at trigger, downside below barrier | coupon digitals + autocall strip + short down-and-in put |
| [Booster](booster.md) | `100% + B × min(Perf, Cap)` up; `100% + Perf` down | long B× call spread, short ATM put |
| [Reverse convertible](reverse-convertible.md) | `100% + C` above barrier; `100% + Perf + C` below | fixed coupon + short down-and-in put |
| [Twin win](twin-win.md) | `100% + Part × |Perf|` while barrier holds; downside if breached | call + down-and-out put |

## Reading a payoff page

- **Modality** — VNP/VNR per CMN Resolution 4,263/2013 (see
  [../overview.md](../overview.md#3-modalities)).
- **B3 registered figure** — the names above are commercial/distribution names; B3
  registers each COE under a figure code (`COE001001`–`COE001088`) named after the option
  package (Call, Call KO, Put KI, Straddle Put KO, Range Accrual, …). Each page states
  its figure; the mapping table is in
  [../parameters.md](../parameters.md#4a-registered-payoff-figures-códigos-de-figura).
- **Parameters** — subset of [../parameters.md](../parameters.md#4-payoff-parameters-per-figure).
- **Barrier conventions** — European / discrete / continuous observation changes the
  economics; each page states what its figure assumes
  ([../calculations.md](../calculations.md#33-barrier-and-digital-observation)).
- **Building blocks** — the derivative replication is how issuers price and hedge the
  note and the fastest way to reason about its risk.
