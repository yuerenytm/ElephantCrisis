from __future__ import annotations

import json
from pathlib import Path
from typing import Any, Dict, List, Optional, Sequence, Tuple

import joblib
import numpy as np
from sklearn.ensemble import IsolationForest

from .features import extract_match_features, extract_snapshots_matrix


class AnomalyDetector:
    def __init__(
        self,
        n_estimators: int = 200,
        contamination: float = 0.02,
        random_state: int = 42,
        max_samples="auto",
        score_threshold: float = -0.15,
        use_hp_delta: bool = True,
    ) -> None:
        self.n_estimators = n_estimators
        self.contamination = contamination
        self.random_state = random_state
        self.max_samples = max_samples
        self.score_threshold = score_threshold
        self.use_hp_delta = use_hp_delta
        self.model: Optional[IsolationForest] = None
        self.feature_names: List[str] = []
        self.feature_mean: Optional[np.ndarray] = None
        self.feature_std: Optional[np.ndarray] = None

    @classmethod
    def from_config(cls, cfg: Dict[str, Any]) -> "AnomalyDetector":
        iforest = cfg.get("isolation_forest") or {}
        feats = cfg.get("features") or {}
        raw_th = cfg.get("score_threshold", None)
        score_threshold = float(raw_th) if raw_th is not None else -0.5
        return cls(
            n_estimators=int(iforest.get("n_estimators", 200)),
            contamination=float(iforest.get("contamination", 0.02)),
            random_state=int(iforest.get("random_state", 42)),
            max_samples=iforest.get("max_samples", "auto"),
            score_threshold=score_threshold,
            use_hp_delta=bool(feats.get("use_hp_delta", True)),
        )

    def fit(self, X: np.ndarray, feature_names: Sequence[str], *, auto_threshold: bool = True) -> None:
        if X.ndim != 2 or X.shape[0] == 0:
            raise ValueError("训练样本为空")
        self.feature_names = list(feature_names)
        self.feature_mean = X.mean(axis=0)
        self.feature_std = X.std(axis=0)
        self.feature_std[self.feature_std < 1e-8] = 1.0
        Xs = (X - self.feature_mean) / self.feature_std
        self.model = IsolationForest(
            n_estimators=self.n_estimators,
            contamination=self.contamination,
            random_state=self.random_state,
            max_samples=self.max_samples,
            n_jobs=-1,
        )
        self.model.fit(Xs)
        # score_samples 尺度随数据变化；按 contamination 分位自动校准阈值
        if auto_threshold:
            train_scores = self.model.score_samples(Xs)
            pct = max(0.1, min(20.0, float(self.contamination) * 100.0))
            self.score_threshold = float(np.percentile(train_scores, pct))

    def _transform(self, X: np.ndarray) -> np.ndarray:
        assert self.feature_mean is not None and self.feature_std is not None
        return (X - self.feature_mean) / self.feature_std

    def score(self, X: np.ndarray) -> np.ndarray:
        if self.model is None:
            raise RuntimeError("模型未训练")
        if X.shape[0] == 0:
            return np.zeros(0, dtype=np.float64)
        return self.model.score_samples(self._transform(X))

    def is_anomaly(self, scores: np.ndarray) -> np.ndarray:
        return scores < self.score_threshold

    def top_deviating_features(self, row: np.ndarray, k: int = 5) -> List[Dict[str, Any]]:
        if self.feature_mean is None or self.feature_std is None:
            return []
        z = np.abs((row - self.feature_mean) / self.feature_std)
        idx = np.argsort(-z)[:k]
        out = []
        for i in idx:
            name = self.feature_names[i] if i < len(self.feature_names) else str(i)
            out.append(
                {
                    "feature": name,
                    "value": float(row[i]),
                    "z": float(z[i]),
                    "mean": float(self.feature_mean[i]),
                }
            )
        return out

    def save(self, model_dir: Path) -> None:
        model_dir.mkdir(parents=True, exist_ok=True)
        if self.model is None:
            raise RuntimeError("无模型可保存")
        joblib.dump(
            {
                "model": self.model,
                "feature_mean": self.feature_mean,
                "feature_std": self.feature_std,
                "feature_names": self.feature_names,
                "score_threshold": self.score_threshold,
                "use_hp_delta": self.use_hp_delta,
                "params": {
                    "n_estimators": self.n_estimators,
                    "contamination": self.contamination,
                    "random_state": self.random_state,
                    "max_samples": self.max_samples,
                },
            },
            model_dir / "iforest.joblib",
        )
        with (model_dir / "feature_meta.json").open("w", encoding="utf-8") as f:
            json.dump(
                {
                    "feature_names": self.feature_names,
                    "n_features": len(self.feature_names),
                    "score_threshold": self.score_threshold,
                    "use_hp_delta": self.use_hp_delta,
                },
                f,
                ensure_ascii=False,
                indent=2,
            )

    @classmethod
    def load(cls, model_dir: Path) -> "AnomalyDetector":
        blob = joblib.load(model_dir / "iforest.joblib")
        params = blob.get("params") or {}
        det = cls(
            n_estimators=int(params.get("n_estimators", 200)),
            contamination=float(params.get("contamination", 0.02)),
            random_state=int(params.get("random_state", 42)),
            max_samples=params.get("max_samples", "auto"),
            score_threshold=float(blob.get("score_threshold", -0.15)),
            use_hp_delta=bool(blob.get("use_hp_delta", True)),
        )
        det.model = blob["model"]
        det.feature_mean = blob["feature_mean"]
        det.feature_std = blob["feature_std"]
        det.feature_names = list(blob.get("feature_names") or [])
        return det


def train_from_matches(
    match_events: Sequence[Tuple[str, Sequence[Dict[str, Any]]]],
    cfg: Dict[str, Any],
) -> Tuple[AnomalyDetector, int]:
    det = AnomalyDetector.from_config(cfg)
    X, _, names = extract_snapshots_matrix(match_events, use_hp_delta=det.use_hp_delta)
    det.fit(X, names)
    return det, int(X.shape[0])


def score_match(
    det: AnomalyDetector,
    events: Sequence[Dict[str, Any]],
    match_id: str,
) -> Dict[str, Any]:
    X, meta = extract_match_features(events, match_id=match_id, use_hp_delta=det.use_hp_delta)
    if X.shape[0] == 0:
        return {
            "match_id": match_id,
            "n_snapshots": 0,
            "anomaly_count": 0,
            "max_anomaly_score": None,
            "min_score": None,
            "anomalies": [],
        }
    scores = det.score(X)
    flags = det.is_anomaly(scores)
    anomalies = []
    for i, flag in enumerate(flags):
        if not flag:
            continue
        anomalies.append(
            {
                **meta[i],
                "score": float(scores[i]),
                "top_features": det.top_deviating_features(X[i], k=5),
            }
        )
    return {
        "match_id": match_id,
        "n_snapshots": int(X.shape[0]),
        "anomaly_count": int(flags.sum()),
        "max_anomaly_score": float(scores.max()),  # 越高越正常
        "min_score": float(scores.min()),
        "anomalies": anomalies,
    }
