"""Побудова рисунків розділу 4. Дані: папка out із CSV, які створює TraceSim.cs."""
import os
import numpy as np
import pandas as pd
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

OUT = "bin/Debug/net10.0/out"
FIG = "figures"
os.makedirs(FIG, exist_ok=True)

plt.rcParams.update({
    "font.size": 10,
    "axes.grid": True,
    "grid.alpha": 0.3,
    "axes.spines.top": False,
    "axes.spines.right": False,
})

NAN = float("nan")
QUEUES = [
    ("qin", "Q_in (вхідний накопичувач)", "tab:blue"),
    ("qsucc", "Q_succ (перед D2)", "tab:green"),
    ("qfail", "Q_fail (перед D3)", "tab:red"),
]
ROUTES = [
    ("D1-D2", "Маршрут D1 → D2", "tab:green"),
    ("D3", "Маршрут D3", "tab:red"),
]

EXPECTED = {
    "N0_T5000": dict(qin_avg=0.00, qin_max=0.00, qfail_max=117.06,
                     w_Q_in=NAN, w_Q_succ=16.74, w_Q_fail=1699.42,
                     t_D1_D2=76.25, t_D3=1763.17),
    "N4_T5000": dict(qin_avg=1.50, qin_max=4.00, qfail_max=41.50,
                     w_Q_in=44.75, w_Q_succ=71.34, w_Q_fail=1041.79,
                     t_D1_D2=161.05, t_D3=1117.95),
    "N14_T5000": dict(qin_avg=5.89, qin_max=13.96, qfail_max=16.80,
                      w_Q_in=115.34, w_Q_succ=135.39, w_Q_fail=731.75,
                      t_D1_D2=293.82, t_D3=819.49),
    "N14_T10000": dict(qin_avg=6.02, qin_max=14.00, qfail_max=26.31,
                       w_Q_in=118.13, w_Q_succ=160.90, w_Q_fail=1033.97,
                       t_D1_D2=322.38, t_D3=1128.72),
    "N124_T5000": dict(qin_avg=17.32, qin_max=42.90, qfail_max=0.00,
                       w_Q_in=280.96, w_Q_succ=167.16, w_Q_fail=NAN,
                       t_D1_D2=473.22, t_D3=NAN),
}

_cache = {}


def path(kind, tag):
    return os.path.join(OUT, f"{kind}_{tag}.csv")


def load(kind, tag):
    key = (kind, tag)
    if key not in _cache:
        _cache[key] = pd.read_csv(path(kind, tag))
    return _cache[key]


def save(fig, name):
    fig.tight_layout()
    fig.savefig(os.path.join(FIG, name), dpi=200)
    plt.close(fig)
    print("  збережено", name)


def band(ax, df, col, label, color, note_zero=False):
    g = df.groupby("t")[col]
    m, s = g.mean(), g.std().fillna(0)
    ax.plot(m.index, m.values, color=color, label=label, lw=1.4)
    ax.fill_between(m.index, (m - s).clip(lower=0), m + s, color=color, alpha=0.2)
    if note_zero and m.max() == 0:
        ax.set_ylim(0, 1)
        ax.text(0.5, 0.5, "черга відсутня (дорівнює 0 протягом усього прогону)",
                transform=ax.transAxes, ha="center", va="center", color="gray")


def no_data(ax, x, y=0.0):
    ax.text(x, y, "немає\nданих", ha="center", va="bottom", fontsize=8, color="gray")


def fig_queues(tag, name, title, vline=None):
    q = load("queues", tag)
    fig, axes = plt.subplots(3, 1, figsize=(9, 8), sharex=True)
    for ax, (col, label, color) in zip(axes, QUEUES):
        band(ax, q, col, label, color)
        ax.set_ylabel("шт.")
        ax.set_title(label, fontsize=10)
        ax.set_ylim(bottom=0)
        if vline:
            ax.axvline(vline, ls="--", c="k", lw=1)
    if vline:
        axes[0].text(vline, axes[0].get_ylim()[1], f" {vline} хв", va="top", fontsize=8)
    axes[-1].set_xlabel("Час моделювання, хв")
    fig.suptitle(title)
    save(fig, name)


def fig_total(tag, name, title):
    q = load("queues", tag)
    fig, ax = plt.subplots(figsize=(9, 4.5))
    band(ax, q, "total", "Агрегатів у системі", "tab:purple")
    ax.set(xlabel="Час моделювання, хв", ylabel="Кількість агрегатів, шт.", title=title)
    ax.legend()
    save(fig, name)


def fig_queue_stats(tag, name, title):
    sm, wt = load("summary", tag), load("waits", tag)
    names = ["Q_in", "Q_succ", "Q_fail"]
    avg = [sm.qin_avg.mean(), sm.qsucc_avg.mean(), sm.qfail_avg.mean()]
    mx = [sm.qin_max.mean(), sm.qsucc_max.mean(), sm.qfail_max.mean()]
    wait = wt.groupby("queue").wait.mean().reindex(names)
    wstd = (wt.groupby(["queue", "rep"]).wait.mean()
              .groupby("queue").std().reindex(names))

    fig, axes = plt.subplots(1, 2, figsize=(11, 4.8))
    x = np.arange(3)
    b1 = axes[0].bar(x - 0.2, avg, 0.4, label="Середня", color="tab:blue")
    b2 = axes[0].bar(x + 0.2, mx, 0.4, label="Максимальна (середнє за реплікаціями)", color="tab:orange")
    axes[0].bar_label(b1, fmt="%.2f", fontsize=8)
    axes[0].bar_label(b2, fmt="%.2f", fontsize=8)
    axes[0].set_xticks(x)
    axes[0].set_xticklabels(names)
    axes[0].set(ylabel="Довжина черги, шт.", title="Довжини черг")
    axes[0].legend(fontsize=8)

    b3 = axes[1].bar(names, wait.fillna(0).values, yerr=wstd.fillna(0).values,
                     capsize=4, color="tab:green")
    for i, n in enumerate(names):
        if np.isnan(wait[n]):
            no_data(axes[1], i)
        else:
            axes[1].text(i, wait[n], f"{wait[n]:.2f}", ha="center", va="bottom", fontsize=8)
    axes[1].set(ylabel="Час очікування, хв", title="Середній час очікування (±σ між реплікаціями)")
    fig.suptitle(title)
    save(fig, name)


def fig_routes(tag, name, title):
    df = load("routes", tag)
    present = [r for r in ROUTES if (df.route == r[0]).any()]
    fig, axes = plt.subplots(1, 2, figsize=(11, 4.8))

    data = [df.loc[df.route == r, "time"] for r, _, _ in present]
    bp = axes[0].boxplot(data, patch_artist=True)
    for patch, (_, _, c) in zip(bp["boxes"], present):
        patch.set_facecolor(c)
        patch.set_alpha(0.5)
    axes[0].set_xticks(range(1, len(present) + 1))
    axes[0].set_xticklabels([l for _, l, _ in present])
    axes[0].set(ylabel="Час перебування в системі, хв", title="Boxplot")

    bins = np.linspace(0, df.time.max(), 40)
    for r, l, c in present:
        axes[1].hist(df.loc[df.route == r, "time"], bins=bins, alpha=0.55, color=c, label=l)
    axes[1].set(xlabel="Час перебування в системі, хв", ylabel="Кількість агрегатів",
                title="Гістограма")
    axes[1].legend()
    if len(present) < len(ROUTES):
        fig.text(0.5, 0.01, "Маршрут D3 не використовується (відмов немає)",
                 ha="center", fontsize=8, color="gray")
    fig.suptitle(title)
    save(fig, name)



def fig_mean_times(tags, name, title):
    """Висота стовпчика = середнє по всіх агрегатах (як у консолі),
    вуса = σ між середніми реплікацій."""
    fig, ax = plt.subplots(figsize=(10, 5))
    width = 0.8 / len(tags)
    for i, (tag, label) in enumerate(tags):
        df = load("routes", tag)
        pooled = df.groupby("route").time.mean()
        spread = df.groupby(["route", "rep"]).time.mean().groupby("route").std()
        for j, (r, _, _) in enumerate(ROUTES):
            x = j + (i - (len(tags) - 1) / 2) * width
            if r not in pooled.index:
                no_data(ax, x)
                continue
            bar = ax.bar(x, pooled[r], width, yerr=spread[r], capsize=3,
                         color=f"C{i}", label=label if j == 0 else None)
            ax.bar_label(bar, fmt="%.2f", fontsize=6, padding=8)
    ax.set_xticks(range(len(ROUTES)))
    ax.set_xticklabels([l for _, l, _ in ROUTES])
    ax.set(ylabel="Середній час перебування, хв", title=title)
    ax.legend()
    save(fig, name)


def dashboard(tag, name, title, cap=None):
    q, sm = load("queues", tag), load("summary", tag)
    fig = plt.figure(figsize=(12, 11))
    gs = fig.add_gridspec(3, 3, height_ratios=[1.1, 1, 1.4])

    ax = fig.add_subplot(gs[0, :])
    for col, label, color in QUEUES:
        band(ax, q, col, label, color)
    ax.set(title="Довжини черг у часі", xlabel="Час, хв", ylabel="шт.")
    ax.legend(fontsize=8)

    ax = fig.add_subplot(gs[1, 0])
    band(ax, q, "total", "Агрегатів у системі", "tab:purple")
    ax.set(title="Агрегати в системі", xlabel="Час, хв", ylabel="шт.")

    ax = fig.add_subplot(gs[1, 1])
    u = sm[["u1", "u2", "u3"]]
    bars = ax.bar(["D1", "D2", "D3"], u.mean(), yerr=u.std(), capsize=4, color="tab:blue")
    ax.bar_label(bars, fmt="%.3f", fontsize=8)
    ax.set(title="Завантаження каналів (±σ)", ylim=(0, 1.1))

    ax = fig.add_subplot(gs[1, 2])
    if sm.p_rej.max() == 0 and cap is not None:
        ax.hist(sm.qin_max, bins=20, color="tab:orange")
        ax.axvline(cap, ls="--", c="k")
        ax.set(title="Розподіл max Q_in (пунктир — N)", xlabel="шт.", ylabel="Реплікацій")
    else:
        ax.hist(sm.p_rej, bins=20, color="tab:orange")
        ax.set(title="Розподіл P_відм за реплікаціями", xlabel="P_відм", ylabel="Реплікацій")

    ax = fig.add_subplot(gs[2, :])
    ax.axis("off")
    rows = [("P_відм", "p_rej"), ("Завантаження D1", "u1"), ("Завантаження D2", "u2"),
            ("Завантаження D3", "u3"), ("Q_in сер., шт.", "qin_avg"), ("Q_in макс., шт.", "qin_max"),
            ("Q_succ сер., шт.", "qsucc_avg"),
            ("Q_fail сер., шт.", "qfail_avg"), ("Q_fail макс., шт.", "qfail_max")]
    cells = [[n, f"{sm[c].mean():.4f}", f"{sm[c].std():.4f}"] for n, c in rows]
    tbl = ax.table(cellText=cells, colLabels=["Показник", "Середнє", "σ"],
                   loc="center", cellLoc="center")
    tbl.auto_set_font_size(False)
    tbl.set_fontsize(9)
    tbl.scale(1, 1.3)
    fig.suptitle(title, fontweight="bold")
    save(fig, name)


def fig_dependence_on_N(name):
    """Додатковий рисунок за таблицею 4.2 (значення з консолі)."""
    N = [0, 2, 4, 6, 8, 10, 12, 14, 16, 20, 30, 40, 50, 60, 80, 100, 120, 150, 200]
    p_sim = [0.5003, 0.3347, 0.2484, 0.1958, 0.1609, 0.1334, 0.1081, 0.0977, 0.0844,
             0.0608, 0.0419, 0.0279, 0.0138, 0.0059, 0.0007, 0, 0, 0, 0]
    no_rej = [0, 0, 0, 0, 0, 1, 1, 3, 7, 14, 30, 49, 66, 81, 97, 100, 100, 100, 100]
    p_th = [1 / (1 + n / 2 + 1) for n in N]
    fig, axes = plt.subplots(1, 2, figsize=(11, 4.5))
    axes[0].plot(N, p_sim, "o-", label="Симуляція (T = 5000 хв)")
    axes[0].plot(N, p_th, "s--", label="Теорія M/M/1/K (стаціонар)")
    axes[0].set(xlabel="N, шт.", ylabel="P_відм", title="Ймовірність відмови")
    axes[0].legend()
    axes[1].plot(N, no_rej, "o-", color="tab:green")
    axes[1].set(xlabel="N, шт.", ylabel="Реплікацій без відмов, %",
                title="Частка реплікацій без жодної відмови")
    save(fig, name)


def metrics(tag):
    sm, wt, rt = load("summary", tag), load("waits", tag), load("routes", tag)
    got = {"qin_avg": sm.qin_avg.mean(), "qin_max": sm.qin_max.mean(),
           "qfail_max": sm.qfail_max.mean()}
    for qn in ["Q_in", "Q_succ", "Q_fail"]:
        got[f"w_{qn}"] = wt.loc[wt.queue == qn, "wait"].mean()
    for r in ["D1-D2", "D3"]:
        got["t_" + r.replace("-", "_")] = rt.loc[rt.route == r, "time"].mean()
    return got


def check_against_console(tag):
    got, exp = metrics(tag), EXPECTED[tag]
    bad = []
    for k, e in exp.items():
        g = got[k]
        if np.isnan(e) and np.isnan(g):
            continue
        if np.isnan(e) != np.isnan(g) or abs(g - e) > 0.011:
            bad.append(f"{k}: CSV {g:.2f}, консоль {e:.2f}")
    print(f"  {tag}: {'OK' if not bad else 'РОЗБІЖНІСТЬ -> ' + '; '.join(bad)}")


def report(tag):
    sm = load("summary", tag)
    print(f"\n[{tag}]")
    print(f"  P_відм = {sm.p_rej.mean():.4f}; завантаження D1 = {sm.u1.mean():.4f}, "
          f"D2 = {sm.u2.mean():.4f}, D3 = {sm.u3.mean():.4f}")
    print(f"  Q_succ: сер. {sm.qsucc_avg.mean():.2f}, макс. {sm.qsucc_max.mean():.2f}; "
          f"Q_fail: сер. {sm.qfail_avg.mean():.2f}")
    print("  агрегатів оброблено:", round(sm.completed.mean(), 1))


def growth(tag, col):
    """Середнє значення col за вікнами часу: показує, чи йде вихід на стаціонар."""
    q = load("queues", tag)
    tmax = int(q.t.max())
    step = 1000 if tmax <= 20000 else 5000
    edges = list(range(0, tmax + 1, step))
    m = q.groupby(pd.cut(q.t, edges, include_lowest=True), observed=True)[col].mean()
    print(f"  {col}, середнє за вікнами по {step} хв:", [round(v, 2) for v in m.values])


def main():
    required = list(EXPECTED.keys())
    missing = [t for t in required if not os.path.exists(path("queues", t))]
    if missing:
        print("Бракує файлів для:", missing, "- додайте ці прогони в TraceSim.cs і перезапустіть.")
        return

    print("Звірка з консоллю:")
    for t in required:
        check_against_console(t)

    print("\nПобудова рисунків...")
    fig_queues("N0_T5000", "fig_4_1.png", "Довжини черг, базова модель (N = 0)")
    fig_queue_stats("N0_T5000", "fig_4_2.png", "Аналіз черг, базова модель (N = 0)")
    fig_routes("N0_T5000", "fig_4_3.png", "Маршрути обробки, базова модель (N = 0)")
    dashboard("N0_T5000", "fig_4_4.png", "Основні метрики, базова модель (N = 0)")
    fig_queues("N4_T5000", "fig_4_5.png", "Довжини черг (N = 4)")
    fig_queues("N14_T5000", "fig_4_6.png", "Довжини черг (N = 14)")
    fig_queues("N14_T10000", "fig_4_7.png",
               "Перевірка виходу на сталий режим (N = 14, T = 10000 хв)", vline=5000)
    fig_queues("N124_T5000", "fig_4_8.png", "Довжини черг (N = 124)")
    fig_total("N124_T5000", "fig_4_9.png", "Кількість агрегатів у системі (N = 124)")
    fig_queue_stats("N124_T5000", "fig_4_10.png", "Аналіз черг (N = 124)")
    fig_routes("N124_T5000", "fig_4_11.png", "Час перебування за маршрутами (N = 124)")
    fig_mean_times([("N0_T5000", "N = 0"), ("N4_T5000", "N = 4"),
                    ("N14_T5000", "N = 14"), ("N124_T5000", "N = 124")],
                   "fig_4_12.png", "Середній час перебування в системі за маршрутами")
    dashboard("N124_T5000", "fig_4_13.png", "Основні метрики (N = 124)")
    fig_dependence_on_N("fig_extra_N.png")

    if os.path.exists(path("queues", "N14_T50000")):
        fig_queues("N14_T50000", "fig_4_7_long.png", "Довгий прогін (N = 14, T = 50000 хв)")

    print("\nДодаткові числа для тексту:")
    for t in required:
        report(t)

    print("\nПеревірка виходу на стаціонар:")
    print(" N = 14, T = 10000:")
    growth("N14_T10000", "qfail")
    if os.path.exists(path("queues", "N14_T50000")):
        print(" N = 14, T = 50000:")
        growth("N14_T50000", "qfail")
    print(" N = 124, T = 5000:")
    growth("N124_T5000", "qin")
    growth("N124_T5000", "total")


if __name__ == "__main__":
    main()
