"""模块2：对局日志硬性规则基线校验。"""

from .checks import Violation, check_match_events
from .runner import run_baseline

__all__ = ["Violation", "check_match_events", "run_baseline"]
