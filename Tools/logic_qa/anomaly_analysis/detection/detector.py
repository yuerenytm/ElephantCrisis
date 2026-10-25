from __future__ import annotations

import json
from pathlib import Path
from typing import Any, Dict, List, Optional, Sequence, Tuple

import joblib
import numpy as np
from sklearn.ensemble import IsolationForest

from .features import extract_match_features, extract_snapshots_matrix


class AnomalyDetector:
    """孤立森林封装：标准化 + 训练 + 阈值校准 + 持久化。

    判定粒度是"快照帧"而不是"整局"：一局里可能只有一两帧异常。
    `score_samples` 越高越正常；本类统一用 `score < score_threshold` 判异常。

    """
    def __init__(
        self,
        n_estimators: int = 200,
        contamination: float = 0.02,
        random_state: int = 42,
        max_samples="auto",
        score_threshold: float = -0.15,
        use_hp_delta: bool = True,
    ) -> None:
        """初始化参数。

        Args:
            n_estimators: 树的数量（200 足够，样本越多可适当增加）。
            contamination: 预期异常比例，决定自动阈值分位（0.02 = 前 2%）。
            random_state: 固定随机种子，保证同一批数据训练结果可复现。
            max_samples: 每棵树的采样数；"auto" = min(256, 样本数)。
            score_threshold: 初始阈值；fit 时若开启 auto_threshold 会被覆盖。
            use_hp_delta: 是否使用 d_hp 特征，必须与训练时一致。

        """
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
        """从 anomaly.yaml 构造检测器（字段缺失时用内置默认值）。
        """
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
        """训练：先标准化，再拟合孤立森林，最后按分位校准阈值。

        标准化用训练集的 mean/std 并保存下来——推理时必须用同一套，
        否则分数尺度漂移（这是这类模型最常见的线上事故）。

        Args:
            X: (样本数, 特征数) 的特征矩阵。
            feature_names: 与列顺序一致的特征名。
            auto_threshold: True 时把阈值设为训练分数的 contamination 分位。

        """
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
        """用训练时保存的 mean/std 做标准化（推理路径必须走这里）。
        """
        assert self.feature_mean is not None and self.feature_std is not None
        return (X - self.feature_mean) / self.feature_std

    def score(self, X: np.ndarray) -> np.ndarray:
        """给样本打分：返回 sklearn 的 score_samples（越高越正常）。
        """
        if self.model is None:
            raise RuntimeError("模型未训练")
        if X.shape[0] == 0:
            return np.zeros(0, dtype=np.float64)
        return self.model.score_samples(self._transform(X))

    def is_anomaly(self, scores: np.ndarray) -> np.ndarray:
        """按当前阈值把分数转成布尔标记（低于阈值 = 异常）。
        """
        return scores < self.score_threshold

    def top_deviating_features(self, row: np.ndarray, k: int = 5) -> List[Dict[str, Any]]:
        """按 z-score 找出偏离训练分布最大的 k 个特征。

        孤立森林只回答"怪不怪"，不回答"哪里怪"；这里用标准化后的
        绝对偏差补上可解释性，供报告展示。

        Returns:
            [{feature, value, z, mean}, ...]，按 z 降序。

        """
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
        """把模型、标准化参数、特征名、阈值与超参一起存成 joblib。

        同时写 feature_meta.json 供人查看；两者必须成对使用。

        """
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
        """从模型目录恢复检测器（含 mean/std，保证推理标准化一致）。
        """
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
    """从多局 events 训练一个检测器。

    Returns:
        (detector, 训练快照帧数)

    """
    det = AnomalyDetector.from_config(cfg)
    X, _, names = extract_snapshots_matrix(match_events, use_hp_delta=det.use_hp_delta)
    det.fit(X, names)
    return det, int(X.shape[0])


def score_match(
    det: AnomalyDetector,
    events: Sequence[Dict[str, Any]],
    match_id: str,
) -> Dict[str, Any]:
    """给一整局打分：逐帧判定，汇总异常帧与最可疑特征。

    Returns:
        {match_id, n_snapshots, anomaly_count, max_anomaly_score,
        min_score, anomalies[]}

    """
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
