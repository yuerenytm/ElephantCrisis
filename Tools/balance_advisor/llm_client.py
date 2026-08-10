from __future__ import annotations

import json
import os
import urllib.error
import urllib.request
from pathlib import Path
from typing import Any, Dict, Optional

try:
    import yaml
except ImportError:  # pragma: no cover
    yaml = None

ROOT = Path(__file__).resolve().parent


def load_advisor_config(path: Optional[Path] = None) -> Dict[str, Any]:
    cfg_path = path or (ROOT / "config" / "advisor.yaml")
    if yaml is None or not cfg_path.is_file():
        return {}
    with cfg_path.open("r", encoding="utf-8") as f:
        return yaml.safe_load(f) or {}


def resolve_api_settings(cfg: Optional[Dict[str, Any]] = None) -> Dict[str, Any]:
    cfg = cfg or load_advisor_config()
    api = cfg.get("api") or {}
    # 环境变量优先；未设置时可写在 config/advisor.yaml（项目未公开前可临时这样做）
    return {
        "api_key": (
            os.environ.get("BALANCE_ADVISOR_API_KEY")
            or os.environ.get("OPENAI_API_KEY")
            or str(api.get("api_key") or "")
        ),
        "base_url": (
            os.environ.get("BALANCE_ADVISOR_BASE_URL")
            or api.get("base_url")
            or "https://api.openai.com/v1"
        ).rstrip("/"),
        "model": os.environ.get("BALANCE_ADVISOR_MODEL") or api.get("model") or "gpt-4o-mini",
        "temperature": float(api.get("temperature", 0.3)),
        "timeout_sec": float(api.get("timeout_sec", 120)),
    }


def chat_completion(prompt: str, cfg: Optional[Dict[str, Any]] = None) -> str:
    """调用 OpenAI 兼容 /chat/completions。"""
    settings = resolve_api_settings(cfg)
    if not settings["api_key"]:
        raise RuntimeError(
            "未设置 API Key。请设置环境变量 BALANCE_ADVISOR_API_KEY（或 OPENAI_API_KEY）。"
        )

    url = f"{settings['base_url']}/chat/completions"
    body = {
        "model": settings["model"],
        "temperature": settings["temperature"],
        "messages": [
            {
                "role": "system",
                "content": "你是严谨的数值策划助手，只依据给定统计与规则给出可验证的建议。",
            },
            {"role": "user", "content": prompt},
        ],
    }
    data = json.dumps(body).encode("utf-8")
    req = urllib.request.Request(
        url,
        data=data,
        method="POST",
        headers={
            "Content-Type": "application/json",
            "Authorization": f"Bearer {settings['api_key']}",
        },
    )
    try:
        with urllib.request.urlopen(req, timeout=settings["timeout_sec"]) as resp:
            payload = json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        detail = e.read().decode("utf-8", errors="replace")
        raise RuntimeError(f"API HTTP {e.code}: {detail}") from e
    except urllib.error.URLError as e:
        raise RuntimeError(f"API 连接失败: {e}") from e

    try:
        return str(payload["choices"][0]["message"]["content"])
    except (KeyError, IndexError, TypeError) as e:
        raise RuntimeError(f"API 返回格式异常: {payload}") from e
