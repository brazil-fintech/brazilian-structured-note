#!/usr/bin/env python3
"""Generate the payoff diagrams (SVG) used across /docs.

Every figure in docs/figures/*.svg is produced by this script so the drawings
stay reproducible: edit here, re-run, commit the regenerated SVGs.

Usage:
    python3 docs/figures/generate_figures.py

Conventions
-----------
- x axis: underlying performance at the final observation date, in % of the
  initial price (except the range accrual, whose natural x axis is the
  fraction of observation dates inside the range).
- y axis: redemption amount at maturity, in % of the unit nominal value (VN).
- Blue solid line: the COE payoff. Gray dashed line: a direct (delta-one)
  holding of the underlying, plotted as reference.
- Figures are drawn light-mode with an explicit background so they stay
  legible when embedded on a dark page (e.g. GitHub dark theme).
"""

import os

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))

# Palette (validated light-mode steps; see docs/references.md)
SURFACE = "#fcfcfb"
INK = "#0b0b0b"
INK2 = "#52514e"
MUTED = "#898781"
GRID = "#e1e0d9"
BASELINE = "#c3c2b7"
SERIES = "#2a78d6"   # COE payoff
REF = "#898781"      # direct holding of the underlying
FILL = "#cde2fb"     # light wash used to shade protected / rebate zones

plt.rcParams.update({
    "font.family": "DejaVu Sans",
    "svg.fonttype": "none",
})


def setup_ax(title, subtitle, xlabel="Underlying performance at maturity (%)",
             ylabel="Redemption (% of nominal)", xlim=(-60, 60), ylim=(0, 175)):
    fig, ax = plt.subplots(figsize=(7.4, 4.4))
    fig.patch.set_facecolor(SURFACE)
    ax.set_facecolor(SURFACE)
    for side in ("top", "right"):
        ax.spines[side].set_visible(False)
    for side in ("left", "bottom"):
        ax.spines[side].set_color(BASELINE)
        ax.spines[side].set_linewidth(1.0)
    ax.grid(axis="y", color=GRID, linewidth=0.75)
    ax.set_axisbelow(True)
    ax.tick_params(colors=MUTED, labelsize=9)
    for lbl in ax.get_xticklabels() + ax.get_yticklabels():
        lbl.set_color(MUTED)
    ax.set_xlabel(xlabel, fontsize=9.5, color=MUTED)
    ax.set_ylabel(ylabel, fontsize=9.5, color=MUTED)
    ax.set_xlim(*xlim)
    ax.set_ylim(*ylim)
    fig.text(0.065, 0.965, title, fontsize=12.5, fontweight="bold", color=INK,
             ha="left", va="top")
    fig.text(0.065, 0.905, subtitle, fontsize=9.5, color=INK2, ha="left", va="top")
    fig.subplots_adjust(top=0.745, bottom=0.13, left=0.095, right=0.97)
    return fig, ax


def hairlines(ax, xlim):
    # 100%-of-nominal horizontal reference and the 0%-performance vertical
    ax.axhline(100, color=BASELINE, linewidth=0.9, zorder=1)
    ax.axvline(0, color=BASELINE, linewidth=0.9, zorder=1)


def vline(ax, x, label, ylim_frac=1.0):
    ax.axvline(x, color=BASELINE, linewidth=1.0, linestyle=(0, (3, 3)), zorder=1)
    ax.text(x, ax.get_ylim()[1] * 0.985, label, fontsize=8.5, color=INK2,
            ha="center", va="top",
            bbox=dict(facecolor=SURFACE, edgecolor="none", pad=1.5))


def reference_line(ax, x):
    ax.plot(x, 100 + x, color=REF, linewidth=1.6, linestyle=(0, (4, 3)), zorder=2)


def legend_row(fig, entries):
    # simple swatch legend row under the subtitle; text stays in ink tokens
    x, y = 0.065, 0.800
    for color, style, text in entries:
        line = plt.Line2D([x, x + 0.030], [y, y], transform=fig.transFigure,
                          color=color, linewidth=2.4,
                          linestyle="-" if style == "solid" else (0, (4, 3)))
        fig.add_artist(line)
        fig.text(x + 0.038, y, text, fontsize=9, color=INK2, va="center")
        x += 0.038 + 0.0105 * len(text) + 0.030


def save(fig, name):
    path = os.path.join(HERE, name)
    fig.savefig(path, format="svg", facecolor=SURFACE)
    plt.close(fig)
    print("wrote", path)


LEG = [(SERIES, "solid", "COE redemption"), (REF, "dash", "Direct holding of the underlying")]


def fig_modalities():
    fig, ax = setup_ax(
        "The two COE modalities",
        "Valor Nominal Protegido (VNP) floors redemption at the nominal; Valor Nominal em Risco (VNR)\n"
        "can lose up to — but never more than — the invested nominal (CMN Resolution 4,263/2013).",
        ylim=(0, 200))
    hairlines(ax, (-60, 60))
    x = np.linspace(-60, 60, 500)
    vnp = 100 + 0.7 * np.maximum(x, 0)
    vnr = np.where(x >= 0, 100 + 1.4 * x, 100 + x)
    ax.plot(x, vnp, color=SERIES, linewidth=2.5, zorder=3)
    ax.plot(x, vnr, color="#eb6834", linewidth=2.5, zorder=3)
    ax.fill_between(x[x <= 0], 100, vnp[x <= 0], color=FILL, alpha=0.55, zorder=1)
    ax.text(58, vnp[-1] + 3, "VNP", fontsize=9.5, color=INK2, ha="right")
    ax.text(58, vnr[-1] + 3, "VNR", fontsize=9.5, color=INK2, ha="right")
    ax.text(-57, 104, "VNP floor = 100% of nominal", fontsize=8.5, color=INK2)
    legend_row(fig, [(SERIES, "solid", "VNP (protected, e.g. 70% participation)"),
                     ("#eb6834", "solid", "VNR (at risk, e.g. 1.4x booster)")])
    save(fig, "modalities-vnp-vnr.svg")


def fig_call_participation():
    fig, ax = setup_ax(
        "Call with participation (capital-protected)",
        "Redemption = 100% + participation x max(performance, 0).  Drawn with participation = 70%.",
        ylim=(60, 175))
    hairlines(ax, (-60, 60))
    x = np.linspace(-60, 60, 500)
    y = 100 + 0.70 * np.maximum(x, 0)
    reference_line(ax, x)
    ax.plot(x, y, color=SERIES, linewidth=2.5, zorder=3)
    vline(ax, 0, "strike (100%)")
    legend_row(fig, LEG)
    save(fig, "call-participation.svg")


def fig_call_spread():
    fig, ax = setup_ax(
        "Call spread (capped call, capital-protected)",
        "Redemption = 100% + participation x min(max(performance, 0), cap).\n"
        "Drawn with participation = 100% and cap = 25%.",
        ylim=(60, 175))
    hairlines(ax, (-60, 60))
    x = np.linspace(-60, 60, 500)
    y = 100 + 1.0 * np.minimum(np.maximum(x, 0), 25)
    reference_line(ax, x)
    ax.plot(x, y, color=SERIES, linewidth=2.5, zorder=3)
    vline(ax, 0, "strike (100%)")
    vline(ax, 25, "cap (125%)")
    legend_row(fig, LEG)
    save(fig, "call-spread.svg")


def fig_put_spread():
    fig, ax = setup_ax(
        "Put spread (bearish, capital-protected)",
        "Redemption = 100% + participation x min(max(-performance, 0), cap).\n"
        "Drawn with participation = 100% and cap = 25%.",
        ylim=(60, 175))
    hairlines(ax, (-60, 60))
    x = np.linspace(-60, 60, 500)
    y = 100 + 1.0 * np.minimum(np.maximum(-x, 0), 25)
    reference_line(ax, x)
    ax.plot(x, y, color=SERIES, linewidth=2.5, zorder=3)
    vline(ax, 0, "strike (100%)")
    vline(ax, -25, "cap (75%)")
    legend_row(fig, LEG)
    save(fig, "put-spread.svg")


def fig_digital():
    fig, ax = setup_ax(
        "Digital / dual indexer (duplo indexador)",
        "Above the strike the certificate pays a fixed digital coupon; otherwise the nominal\n"
        "(optionally accrued at a % of CDI). Drawn with coupon = 15%.",
        ylim=(60, 175))
    hairlines(ax, (-60, 60))
    x = np.linspace(-60, 60, 500)
    reference_line(ax, x)
    ax.plot([-60, 0], [100, 100], color=SERIES, linewidth=2.5, zorder=3)
    ax.plot([0, 60], [115, 115], color=SERIES, linewidth=2.5, zorder=3)
    ax.plot([0], [100], marker="o", markersize=6, markerfacecolor=SURFACE,
            markeredgecolor=SERIES, markeredgewidth=1.8, zorder=4)
    ax.plot([0], [115], marker="o", markersize=6, color=SERIES, zorder=4)
    vline(ax, 0, "strike (100%)")
    ax.text(30, 119, "100% + 15% coupon", fontsize=8.5, color=INK2, ha="center")
    ax.text(-30, 104, "100% (or % of CDI)", fontsize=8.5, color=INK2, ha="center")
    legend_row(fig, LEG)
    save(fig, "digital-duplo-indexador.svg")


def fig_shark_fin():
    fig, ax = setup_ax(
        "Shark fin (up-and-out call with rebate, capital-protected)",
        "Participation in the upside while the knock-out barrier is not breached; if it is, the gain\n"
        "collapses to a small rebate. Drawn with participation = 100%, barrier = 130%, rebate = 3%.",
        ylim=(60, 175))
    hairlines(ax, (-60, 60))
    x = np.linspace(-60, 60, 500)
    reference_line(ax, x)
    ax.plot([-60, 0], [100, 100], color=SERIES, linewidth=2.5, zorder=3)
    ax.plot([0, 30], [100, 130], color=SERIES, linewidth=2.5, zorder=3)
    ax.plot([30, 60], [103, 103], color=SERIES, linewidth=2.5, zorder=3)
    ax.plot([30], [130], marker="o", markersize=6, markerfacecolor=SURFACE,
            markeredgecolor=SERIES, markeredgewidth=1.8, zorder=4)
    ax.plot([30], [103], marker="o", markersize=6, color=SERIES, zorder=4)
    vline(ax, 0, "strike (100%)")
    vline(ax, 30, "KO barrier (130%)")
    ax.text(45, 107.5, "rebate: 103%", fontsize=8.5, color=INK2, ha="center")
    legend_row(fig, LEG)
    save(fig, "shark-fin.svg")


def fig_range_accrual():
    fig, ax = setup_ax(
        "Range accrual (capital-protected)",
        "The coupon accrues for each observation date the underlying fixes inside the range.\n"
        "Drawn with maximum coupon = 12%.",
        xlabel="Observation dates inside the range (% of total)",
        xlim=(0, 100), ylim=(90, 120))
    ax.axhline(100, color=BASELINE, linewidth=0.9, zorder=1)
    n = np.linspace(0, 100, 500)
    y = 100 + 12 * n / 100
    ax.plot(n, y, color=SERIES, linewidth=2.5, zorder=3)
    ax.text(97, 113.2, "max coupon 12%", fontsize=8.5, color=INK2, ha="right")
    legend_row(fig, [(SERIES, "solid", "COE redemption")])
    save(fig, "range-accrual.svg")


def fig_autocall_athena():
    fig, ax = setup_ax(
        "Autocall Athena — payoff if it reaches maturity",
        "At each observation date, closes >= initial price: early redemption at 100% + n x coupon.\n"
        "At maturity (drawn): coupon above 100%, protected down to the 70% barrier, delta-one below it.",
        ylim=(20, 175))
    hairlines(ax, (-60, 60))
    x = np.linspace(-60, 60, 500)
    reference_line(ax, x)
    ax.plot([-60, -30], [40, 70], color=SERIES, linewidth=2.5, zorder=3)
    ax.plot([-30, 0], [100, 100], color=SERIES, linewidth=2.5, zorder=3)
    ax.plot([0, 60], [140, 140], color=SERIES, linewidth=2.5, zorder=3)
    ax.plot([-30], [70], marker="o", markersize=6, markerfacecolor=SURFACE,
            markeredgecolor=SERIES, markeredgewidth=1.8, zorder=4)
    ax.plot([-30], [100], marker="o", markersize=6, color=SERIES, zorder=4)
    ax.plot([0], [100], marker="o", markersize=6, markerfacecolor=SURFACE,
            markeredgecolor=SERIES, markeredgewidth=1.8, zorder=4)
    ax.plot([0], [140], marker="o", markersize=6, color=SERIES, zorder=4)
    vline(ax, -30, "protection barrier (70%)")
    vline(ax, 0, "autocall trigger (100%)")
    ax.text(30, 145, "100% + 4 x 10% coupons", fontsize=8.5, color=INK2, ha="center")
    legend_row(fig, LEG)
    save(fig, "autocall-athena.svg")


def fig_autocall_phoenix():
    fig, ax = setup_ax(
        "Autocall Phoenix — payoff if it reaches maturity",
        "Pays periodic coupons (with memory) whenever the underlying fixes above the coupon barrier;\n"
        "autocalls above the initial price. At maturity (drawn): barrier = 70%, final coupon = 4%.",
        ylim=(20, 175))
    hairlines(ax, (-60, 60))
    x = np.linspace(-60, 60, 500)
    reference_line(ax, x)
    ax.plot([-60, -30], [40, 70], color=SERIES, linewidth=2.5, zorder=3)
    ax.plot([-30, 60], [104, 104], color=SERIES, linewidth=2.5, zorder=3)
    ax.plot([-30], [70], marker="o", markersize=6, markerfacecolor=SURFACE,
            markeredgecolor=SERIES, markeredgewidth=1.8, zorder=4)
    ax.plot([-30], [104], marker="o", markersize=6, color=SERIES, zorder=4)
    vline(ax, -30, "coupon / protection barrier (70%)")
    ax.text(20, 108.5, "100% + coupon (plus any memory coupons)",
            fontsize=8.5, color=INK2, ha="center")
    legend_row(fig, LEG)
    save(fig, "autocall-phoenix.svg")


def fig_booster():
    fig, ax = setup_ax(
        "Booster (capital at risk)",
        "Leveraged participation in the upside up to a cap, one-for-one loss below the strike.\n"
        "Drawn with boost = 2x and cap on performance = 25% (max redemption 150%).",
        ylim=(20, 175))
    hairlines(ax, (-60, 60))
    x = np.linspace(-60, 60, 500)
    y = np.where(x >= 0, 100 + 2.0 * np.minimum(x, 25), 100 + x)
    reference_line(ax, x)
    ax.plot(x, y, color=SERIES, linewidth=2.5, zorder=3)
    vline(ax, 0, "strike (100%)")
    vline(ax, 25, "cap (125%)")
    legend_row(fig, LEG)
    save(fig, "booster.svg")


def fig_reverse_convertible():
    fig, ax = setup_ax(
        "Reverse convertible (capital at risk)",
        "A fixed coupon is paid in every scenario; below the barrier the investor takes the full\n"
        "downside of the underlying. Drawn with coupon = 18% and European barrier = 70%.",
        ylim=(20, 175))
    hairlines(ax, (-60, 60))
    x = np.linspace(-60, 60, 500)
    reference_line(ax, x)
    ax.plot([-60, -30], [58, 88], color=SERIES, linewidth=2.5, zorder=3)
    ax.plot([-30, 60], [118, 118], color=SERIES, linewidth=2.5, zorder=3)
    ax.plot([-30], [88], marker="o", markersize=6, markerfacecolor=SURFACE,
            markeredgecolor=SERIES, markeredgewidth=1.8, zorder=4)
    ax.plot([-30], [118], marker="o", markersize=6, color=SERIES, zorder=4)
    vline(ax, -30, "barrier (70%)")
    ax.text(15, 122.5, "100% + 18% coupon", fontsize=8.5, color=INK2, ha="center")
    ax.text(-49, 80, "performance + 18%", fontsize=8.5, color=INK2, ha="center")
    legend_row(fig, LEG)
    save(fig, "reverse-convertible.svg")


def fig_twin_win():
    fig, ax = setup_ax(
        "Twin win (capital at risk)",
        "Gains from movement in either direction while the lower barrier holds; if it is breached,\n"
        "the payoff reverts to the direct downside. Drawn with 100% participation, barrier = 60%.",
        ylim=(20, 175))
    hairlines(ax, (-60, 60))
    x = np.linspace(-60, 60, 500)
    reference_line(ax, x)
    ax.plot([0, 60], [100, 160], color=SERIES, linewidth=2.5, zorder=3)
    ax.plot([-40, 0], [140, 100], color=SERIES, linewidth=2.5, zorder=3)
    ax.plot([-60, -40], [40, 60], color=SERIES, linewidth=2.5, zorder=3)
    ax.plot([-40], [140], marker="o", markersize=6, color=SERIES, zorder=4)
    ax.plot([-40], [60], marker="o", markersize=6, markerfacecolor=SURFACE,
            markeredgecolor=SERIES, markeredgewidth=1.8, zorder=4)
    vline(ax, -40, "barrier (60%)")
    ax.text(-20, 127, "absolute performance", fontsize=8.5, color=INK2, ha="center")
    legend_row(fig, LEG)
    save(fig, "twin-win.svg")


def fig_decomposition():
    fig, ax = setup_ax(
        "How a capital-protected COE is assembled",
        "The issue price funds a zero-coupon leg that grows back to 100% of nominal at maturity;\n"
        "the remainder is the budget for the option package (net of the issuer margin).",
        xlabel="", ylabel="% of nominal at issuance", xlim=(0, 10), ylim=(0, 140))
    ax.set_xticks([])
    ax.grid(axis="y", color=GRID, linewidth=0.75)
    bars = [
        (1.0, 0, 87.8, "#2a78d6", "Zero-coupon leg\nPV of 100% at maturity\n(e.g. 87.8%)"),
        (4.0, 0, 9.2, "#1baf7a", "Option package\n(e.g. 9.2%)"),
        (7.0, 0, 3.0, "#eda100", "Issuer margin\n(e.g. 3.0%)"),
    ]
    for xpos, y0, h, color, label in bars:
        ax.bar(xpos, h, width=1.6, bottom=y0, color=color, edgecolor=SURFACE,
               linewidth=2, zorder=3)
        ax.text(xpos, h + 4, label, fontsize=8.5, color=INK2, ha="center", va="bottom",
                bbox=dict(facecolor=SURFACE, edgecolor="none", pad=1.5), zorder=4)
    ax.axhline(100, color=BASELINE, linewidth=0.9)
    ax.text(9.6, 103, "issue price = 100%", fontsize=8.5, color=INK2, ha="right")
    save(fig, "coe-decomposition.svg")


if __name__ == "__main__":
    fig_modalities()
    fig_call_participation()
    fig_call_spread()
    fig_put_spread()
    fig_digital()
    fig_shark_fin()
    fig_range_accrual()
    fig_autocall_athena()
    fig_autocall_phoenix()
    fig_booster()
    fig_reverse_convertible()
    fig_twin_win()
    fig_decomposition()
