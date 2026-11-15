"""领域枚举：角色与顺序。

只保留实际被用到的内容；原先 models.py 里的 load_yaml 已删除
（yaml 配置在配置管线改造后不复存在）。
"""
from __future__ import annotations

from enum import Enum


class Role(str, Enum):
    ELEPHANT = "elephant"
    HUMAN = "human"
    MONKEY = "monkey"
    CAT = "cat"


ROLE_ORDER = (Role.ELEPHANT, Role.HUMAN, Role.MONKEY, Role.CAT)
