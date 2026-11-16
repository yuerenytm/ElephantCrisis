"""数值统计工具的测试：置信区间与配对比较。

这些函数决定"改动有没有效果"的结论，算错了比没有更糟。
"""
from __future__ import annotations

import sys
from pathlib import Path

TOOLS = Path(__file__).resolve().parent.parent
if str(TOOLS) not in sys.path:
    sys.path.insert(0, str(TOOLS))

from balance.stats import is_significant, paired_diff, summarize, wilson_interval  # noqa: E402


def test_wilson_zero_trials_returns_full_range():
    assert wilson_interval(0, 0) == (0.0, 1.0)


def test_wilson_bounds_contain_estimate():
    lo, hi = wilson_interval(42, 100)
    assert 0.0 <= lo < 0.42 < hi <= 1.0


def test_wilson_extremes_stay_in_range():
    assert wilson_interval(0, 20)[0] == 0.0
    assert wilson_interval(20, 20)[1] == 1.0


def test_wilson_shrinks_with_more_samples():
    """样本越多，区间越窄。"""
    narrow = wilson_interval(50, 500)
    wide = wilson_interval(5, 50)
    assert (narrow[1] - narrow[0]) < (wide[1] - wide[0])


def test_is_significant_false_when_intervals_overlap():
    # 42/100 与 45/100：区间大幅重叠，不能下结论
    assert not is_significant((42, 100), (45, 100))


def test_is_significant_true_when_far_apart():
    assert is_significant((5, 200), (120, 200))


def test_paired_diff_counts_flips():
    pairs = [(True, True), (True, False), (False, True), (False, False)]
    r = paired_diff(pairs)
    assert r["n"] == 4
    assert r["both"] == 1
    assert r["neither"] == 1
    assert r["only_b"] == 1
    assert r["only_a"] == 1
    assert r["delta"] == 0.0


def test_paired_diff_ignores_order_of_improvement():
    r = paired_diff([(False, True)] * 3)
    assert r["b_rate"] == 1.0 and r["a_rate"] == 0.0
    assert r["delta"] == 1.0


def test_summarize_empty():
    assert summarize([])["n"] == 0


def test_summarize_basic_stats():
    r = summarize([1, 2, 3, 4])
    assert r["n"] == 4
    assert r["median"] == 2.5
    assert r["min"] == 1 and r["max"] == 4
