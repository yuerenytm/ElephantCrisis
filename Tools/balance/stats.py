"""统计工具：置信区间 + 配对比较。

为什么需要：跑 100 局得到的胜率是**估计值**。
42% 和 45% 在 100 局下区间大幅重叠，直接下结论会骗自己。
"""
from __future__ import annotations

import math
from typing import Iterable, List, Sequence, Tuple


def wilson_interval(successes: int, trials: int, z: float = 1.96) -> Tuple[float, float]:
    """二项比例的 Wilson 置信区间（95% 时 z=1.96）。

    比"成功率 ± 1.96*sqrt(p(1-p)/n)"可靠：小样本或极端比例也不会跑出 [0,1]。
    """
    if trials <= 0:
        return (0.0, 1.0)
    p = successes / trials
    denom = 1 + z * z / trials
    center = (p + z * z / (2 * trials)) / denom
    margin = z * math.sqrt(p * (1 - p) / trials + z * z / (4 * trials * trials)) / denom
    return (max(0.0, center - margin), min(1.0, center + margin))


def is_significant(a: Tuple[int, int], b: Tuple[int, int]) -> bool:
    """两组 (成功数, 总数) 的 95% 区间是否不重叠。

    区间重叠就说明"还不能下结论"——这是防止"跑 100 局就说改了有用"的最简闸门。
    """
    lo_a, hi_a = wilson_interval(*a)
    lo_b, hi_b = wilson_interval(*b)
    return hi_a < lo_b or hi_b < lo_a


def paired_diff(pairs: Iterable[Tuple[bool, bool]]) -> dict:
    """配对比较：每局在 A/B 两种配置下的结果。

    Args:
        pairs: (a 成功?, b 成功?) 的可迭代对象，一局一项。

    Returns:
        含 a_rate / b_rate / delta / wins_b / wins_a 的摘要。
    """
    a_win = b_win = both_win = neither = 0
    n = 0
    for a, b in pairs:
        n += 1
        a_win += 1 if a else 0
        b_win += 1 if b else 0
        if a and b:
            both_win += 1
        elif not a and not b:
            neither += 1

    return {
        "n": n,
        "a_rate": a_win / n if n else 0.0,
        "b_rate": b_win / n if n else 0.0,
        "delta": (b_win - a_win) / n if n else 0.0,
        "only_b": b_win - both_win,
        "only_a": a_win - both_win,
        "both": both_win,
        "neither": neither,
    }


def summarize(values: Sequence[float]) -> dict:
    """一组数值的常见统计量。"""
    if not values:
        return {"n": 0}
    ordered = sorted(values)
    n = len(ordered)
    mean = sum(ordered) / n
    median = ordered[n // 2] if n % 2 else (ordered[n // 2 - 1] + ordered[n // 2]) / 2
    return {
        "n": n,
        "mean": mean,
        "median": median,
        "min": ordered[0],
        "max": ordered[-1],
        "p90": ordered[min(n - 1, int(n * 0.9))],
    }
