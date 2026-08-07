from __future__ import annotations

import numpy as np

from anomaly_analysis.detection.detector import AnomalyDetector, train_from_matches
from anomaly_analysis.detection.features import FEATURE_NAMES, extract_match_features, snapshot_to_vector


def test_feature_dim_stable(clean_events):
    X, meta = extract_match_features(clean_events, match_id="c")
    assert X.shape[1] == len(FEATURE_NAMES)
    assert len(meta) == X.shape[0]
    assert X.shape[0] >= 2


def test_snapshot_vector_deterministic():
    snap = {
        "round": 1,
        "hour": 8,
        "weather": "clear",
        "lava_inset": 0,
        "deck_draw": 10,
        "units": [
            {
                "role": "elephant",
                "hp": 100,
                "max_hp": 100,
                "atk": 9,
                "def": 10,
                "move": 3,
                "vis": 8,
                "weight": 0,
                "cap": 15,
                "bag": [],
                "statuses": [],
                "tile": "normal",
            },
            {"role": "human", "dead": True},
            {"role": "monkey", "dead": True},
            {"role": "cat", "dead": True},
        ],
    }
    a = snapshot_to_vector(snap)
    b = snapshot_to_vector(snap)
    assert np.allclose(a, b)
    assert a.shape[0] == len(FEATURE_NAMES)


def test_train_and_score_tiny(clean_events, tmp_path):
    # 复制多份以凑训练样本
    pairs = [(f"m{i}", clean_events) for i in range(8)]
    cfg = {
        "isolation_forest": {
            "n_estimators": 50,
            "contamination": 0.1,
            "random_state": 0,
            "max_samples": "auto",
        },
        "score_threshold": -0.5,
        "features": {"use_hp_delta": True},
    }
    det, n = train_from_matches(pairs, cfg)
    assert n > 0
    det.save(tmp_path / "models")
    loaded = AnomalyDetector.load(tmp_path / "models")
    X, _ = extract_match_features(clean_events, "c")
    scores = loaded.score(X)
    assert scores.shape[0] == X.shape[0]
