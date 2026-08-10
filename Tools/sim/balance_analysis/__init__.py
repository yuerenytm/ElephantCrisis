"""对局数值平衡统计（第一优先级指标）。"""

from .aggregate import aggregate_matches, write_report
from .extract import extract_match

__all__ = ["extract_match", "aggregate_matches", "write_report"]
