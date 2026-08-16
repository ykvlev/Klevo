#!/usr/bin/env python3
"""Klevo, Фаза 3: обучение LightGBM-модели клёва и экспорт в ONNX.

Метки: уловы из таблицы catches (target = 1, если в точке в этот день был улов).
Признаки: ежедневная матрица из features.py.

Пока catches пуст — скрипт сообщает, что меток нет, и завершается без обучения.
Как появятся уловы (или вы подадите CSV через --labels), обучение заработает:
    python features.py --from 2023-01-01 --to 2026-08-20 --out features_2023_2026.csv
    python train.py --features features_2023_2026.csv --out-dir models
    python train.py --labels catches_demo.csv
    python train.py --features X.csv --deploy ../src/Klevo.Api/wwwroot/models

Выход: models/model.lgb (LightGBM), models/model.onnx (ONNX для C#/ONNX Runtime).
--deploy копирует model.onnx в wwwroot API (там его подхватит MlModelRunner).
"""
from __future__ import annotations

import argparse
import os
import shutil
import sys
from pathlib import Path

import pandas as pd
import psycopg2

from features import DB, load_dotenv, load_spots

MODEL_COLS = [
    "moon_phase", "moon_illumination", "major_hours", "major_best_h",
    "minor_hours", "t_min", "t_mean", "t_max", "pressure_mean", "pressure_amp",
    "humidity_mean", "wind_mean", "wind_max", "precip_sum", "cloud_mean",
    "snow_max", "sst_c", "chla_mgm3", "t_delta", "pressure_delta", "doy",
    "month", "weekday", "season",
]


def load_catches(conn, only: list[str] | None = None) -> pd.DataFrame:
    sql = """
        SELECT spot_id::text, (caught_at AT TIME ZONE 'Europe/Moscow')::date AS date,
               count(*) AS catches
        FROM catches
        WHERE caught_at IS NOT NULL
        GROUP BY spot_id, (caught_at AT TIME ZONE 'Europe/Moscow')::date
    """
    with conn.cursor() as cur:
        cur.execute(sql)
        cols = [c.name for c in cur.description]
        df = pd.DataFrame(cur.fetchall(), columns=cols)
    if only:
        df = df[df["spot_id"].isin(only)]
    df["date"] = pd.to_datetime(df["date"])
    return df


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--features", default=None,
                    help="CSV матрицы признаков (иначе строится из БД)")
    ap.add_argument("--labels", default=None,
                    help="CSV меток (spot_id,date,catches); иначе — из таблицы catches")
    ap.add_argument("--out-dir", default="models")
    ap.add_argument("--deploy", default=None,
                    help="куда скопировать model.onnx после экспорта "
                         "(например ../src/Klevo.Api/wwwroot/models)")
    ap.add_argument("--test-after", default="2026-01-01", help="временной сплит")
    ap.add_argument("--spots", nargs="*")
    args = ap.parse_args()

    load_dotenv()

    if args.features:
        feats = pd.read_csv(args.features, parse_dates=["date"])
        feats["spot_id"] = feats["spot_id"].astype(str)
    else:
        from features import build_features
        feats = build_features("2023-01-01", "2026-08-20", args.spots)

    if args.labels:
        labels = pd.read_csv(args.labels, parse_dates=["date"])
    else:
        conn = psycopg2.connect(**DB)
        try:
            labels = load_catches(conn, args.spots)
        finally:
            conn.close()

    print(f"Признаков: {len(feats)} строк x {len(feats.columns)} колонок")
    print(f"Меток (дней с уловами): {len(labels)}")

    if labels.empty:
        print("Меток нет (catches пуст). Обучение не запущено.")
        print("Наполните таблицу catches или передайте --labels CSV с колонками spot_id,date,catches.")
        sys.exit(0)

    df = feats.merge(labels, on=["spot_id", "date"], how="left")
    df["catches"] = df["catches"].fillna(0)
    df["target"] = (df["catches"] > 0).astype(int)
    print(f"Положительных дней: {df['target'].sum()} ({df['target'].mean():.3%})")

    train = df[df["date"] < pd.Timestamp(args.test_after)]
    test = df[df["date"] >= pd.Timestamp(args.test_after)]
    if len(train) < 200 or test["target"].sum() < 10:
        print("Слишком мало данных для валидации — обучите вручную, когда накопятся метки.")
        sys.exit(0)

    import lightgbm as lgb
    from sklearn.metrics import roc_auc_score

    X_train, y_train = train[MODEL_COLS], train["target"]
    X_test, y_test = test[MODEL_COLS], test["target"]

    model = lgb.LGBMClassifier(
        n_estimators=300, learning_rate=0.05, num_leaves=31,
        min_child_samples=20, subsample=0.8, colsample_bytree=0.8,
        random_state=42, verbose=-1)
    model.fit(X_train, y_train,
              eval_X=X_test, eval_y=y_test,
              callbacks=[lgb.early_stopping(30, verbose=False)])

    proba = model.predict_proba(X_test)[:, 1]
    print(f"AUC (test, после {args.test_after}): {roc_auc_score(y_test, proba):.3f}")
    print(f"Позитив в test: {y_test.sum()}/{len(y_test)}")

    out = Path(args.out_dir)
    out.mkdir(parents=True, exist_ok=True)
    model.booster_.save_model(str(out / "model.lgb"))
    print(f"Модель: {out / 'model.lgb'}")

    try:
        import numpy as np
        import onnxruntime as ort

        from onnx_export import lgbm_to_onnx

        lgbm_to_onnx(model, str(out / "model.onnx"), MODEL_COLS)
        print(f"ONNX: {out / 'model.onnx'}")

        if args.deploy:
            deploy_dir = Path(args.deploy)
            deploy_dir.mkdir(parents=True, exist_ok=True)
            shutil.copy2(out / "model.onnx", deploy_dir / "model.onnx")
            print(f"ONNX развёрнут: {deploy_dir / 'model.onnx'} (ml-v1)")

        sess = ort.InferenceSession(str(out / "model.onnx"),
                                    providers=["CPUExecutionProvider"])
        x = X_test.head(5).fillna(0).values.astype(np.float32)
        onnx_p = sess.run(None, {"input": x})[1][:, 1]
        lgb_p = model.predict_proba(x)[:, 1]
        print(f"Проверка ONNX vs LightGBM (первые 5): "
              f"{np.round(np.abs(onnx_p - lgb_p), 5).max():.5f} макс. расхождение")
    except Exception as exc:  # noqa: BLE001
        print(f"ONNX-экспорт не удался: {exc}")


if __name__ == "__main__":
    main()
