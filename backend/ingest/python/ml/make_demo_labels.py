#!/usr/bin/env python3
"""Генератор ДЕМО-меток для проверки пайплайна ML (не для продакшена!).

Пока таблица catches пуста, train.py не с чем обучать. Этот скрипт строит
синтетические уловы, коррелирующие с rule-v1 скором, чтобы проверить весь
контур: features -> LightGBM -> ONNX -> inference. Реальные уловы придут
через POST /api/spots/{id}/catches.

    python make_demo_labels.py --out demo_catches.csv
    python train.py --features features_2023_2026.csv --labels demo_catches.csv
"""
from __future__ import annotations

import argparse

import numpy as np
import pandas as pd

COLUMNS = ["spot_id", "date", "catches", "demo"]


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--features", default="features_2023_2026.csv")
    ap.add_argument("--out", default="demo_catches.csv")
    ap.add_argument("--seed", type=int, default=42)
    args = ap.parse_args()

    feats = pd.read_csv(args.features, parse_dates=["date"])
    feats["spot_id"] = feats["spot_id"].astype(str)

    rng = np.random.default_rng(args.seed)
    # вероятность улова: базовый уровень + сигнал от rule-скора/сезона + шум
    p = 0.06 + 0.25 * (feats["doy"].values / 365.0) ** 2
    if "sst_c" in feats:
        sst = feats["sst_c"].fillna(feats["sst_c"].median())
        p = p + 0.30 * np.clip(1 - np.abs(sst.values - 14.0) / 16.0, 0, 1)
    p = np.clip(p + rng.normal(0, 0.05, len(feats)), 0, 0.8)

    catches = rng.poisson(p * 3.0)
    demo = feats.loc[catches > 0, ["spot_id", "date"]].copy()
    demo["catches"] = catches[catches > 0]
    demo["demo"] = True
    demo.to_csv(args.out, index=False)
    print(f"demo_catches: {len(demo)} дней с уловами из {len(feats)}")


if __name__ == "__main__":
    main()
