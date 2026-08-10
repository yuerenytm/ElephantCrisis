from __future__ import annotations

from enum import Enum
from typing import Any, Dict


class Role(str, Enum):
    ELEPHANT = "elephant"
    HUMAN = "human"
    MONKEY = "monkey"
    CAT = "cat"


ROLE_ORDER = (Role.ELEPHANT, Role.HUMAN, Role.MONKEY, Role.CAT)


def load_yaml(path: str) -> Dict[str, Any]:
    import yaml

    with open(path, "r", encoding="utf-8") as f:
        return yaml.safe_load(f)
