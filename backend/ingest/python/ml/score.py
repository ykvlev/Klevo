#!/usr/bin/env python3
"""Klevo, Фаза 3: правило-базовый скор клёва 0..100 в таблицу predictions.

Базлайн без ML-меток: солунар (окна major/minor + фаза луны) + погода
(давление, ветер, температура, осадки) + сезон. Позже служит нижней границей
для LightGBM (см. train.py).

Пример:
    python score.py --from 2026-08-14 --to 2026-08-20
    python score.py --from 2023-01-01 --to 2026-08-20 --dry-run
"""
from __future__ import annotations

import argparse
import math
import os
import sys
from datetime import time
from pathlib import Path

import pandas as pd
import psycopg2

from features import (_window_hours, load_dotenv, load_solunar, load_spots,
                      load_weather_daily)

MODEL_VERSION = "rule-v1"

DB = {
    "host": os.environ.get("KLEVO_DB_HOST", "localhost"),
    "port": int(os.environ.get("KLEVO_DB_PORT", "5432")),
    "dbname": os.environ.get("KLEVO_DB_NAME", "klevo"),
    "user": os.environ.get("KLEVO_DB_USER", "postgres"),
    "password": os.environ.get("PGPASSWORD", "klevo_dev_pwd"),
}


def _clip(v: float, lo: float, hi: float) -> float:
    return max(lo, min(hi, v))


def solunar_score(row: pd.Series) -> float:
    s = 0.0
    s += 15.0 * _clip(row.get("major_hours", 0) / 6.0, 0, 1)
    s += 8.0 * _clip(row.get("minor_hours", 0) / 4.0, 0, 1)
    phase = row.get("moon_phase")
    if phase is not None and not pd.isna(phase):
        wave = (1.0 + math.cos(2.0 * math.pi * float(phase))) / 2.0
        s += 12.0 * _clip(wave, 0, 1)
    return _clip(s, 0, 35)


def weather_score(row: pd.Series) -> float:
    s = 0.0
    p = row.get("pressure_mean")
    if p is not None and not pd.isna(p):
        s += 12.0 * _clip(1 - abs(float(p) - 1013.0) / 15.0, 0, 1)
    amp = row.get("pressure_amp")
    if amp is not None and not pd.isna(amp):
        a = float(amp)
        s += 8.0 if a < 3.0 else (5.0 if a < 6.0 else (3.0 if a < 10.0 else 0.0))
    w = row.get("wind_mean")
    if w is not None and not pd.isna(w):
        w = float(w)
        s += 10.0 if w <= 4.0 else (6.0 if w <= 8.0 else (3.0 if w <= 12.0 else 0.0))
    t = row.get("t_mean")
    if t is not None and not pd.isna(t):
        s += 10.0 * _clip(1 - abs(float(t) - 14.0) / 18.0, 0, 1)
    pr = row.get("precip_sum")
    if pr is not None and not pd.isna(pr):
        pr = float(pr)
        s += 5.0 if pr <= 2.0 else (3.0 if pr <= 5.0 else 0.0)
    return _clip(s, 0, 45)


def season_score(row: pd.Series) -> float:
    m = int(row.get("month", 1) or 1)
    season = {12: 4, 1: 4, 2: 4, 3: 6, 4: 8, 5: 8, 6: 9, 7: 10, 8: 10, 9: 8,
              10: 7, 11: 5}[m]
    t = row.get("t_mean")
    snow = row.get("snow_max")
    if t is not None and not pd.isna(t) and float(t) < 0 and \
            snow is not None and not pd.isna(snow) and float(snow) > 0.05:
        season -= 4
    return _clip(season, 0, 10)


def best_window(row: pd.Series) -> tuple[time | None, time | None]:
    day = pd.Timestamp(row["date"]).tz_localize("UTC")
    nxt = day + pd.Timedelta(days=1)
    best = None
    for c in ("major_start", "major_end", "major2_start", "major2_end",
              "minor_start", "minor_end", "minor2_start", "minor2_end"):
        s = pd.to_datetime(row.get(c), utc=True, errors="coerce")
        if pd.isna(s):
            continue
        e = pd.to_datetime(row.get(
            c.replace("start", "end")), utc=True, errors="coerce")
        if pd.isna(e):
            continue
        lo, hi = max(s, day), min(e, nxt)
        dur = (hi - lo).total_seconds()
        if dur <= 0:
            continue
        if best is None or dur > best[0]:
            best = (dur, lo, hi)
    if best is None:
        sr = pd.to_datetime(row.get("sun_rise"), utc=True, errors="coerce")
        if not pd.isna(sr):
            sr_local = sr.tz_convert("Europe/Moscow")
            return (sr_local.time(), (sr_local + pd.Timedelta(hours=3)).time())
        return (time(6, 0), time(9, 0))
    return (best[1].tz_convert("Europe/Moscow").time(),
            best[2].tz_convert("Europe/Moscow").time())


def compute_scores(df: pd.DataFrame) -> pd.DataFrame:
    df = df.copy()
    df["score"] = (
        df.apply(solunar_score, axis=1) +
        df.apply(weather_score, axis=1) +
        df.apply(season_score, axis=1)
    ).round().astype(int)
    df[["best_start", "best_end"]] = df.apply(
        lambda r: pd.Series(best_window(r)), axis=1)
    return df


def upsert(conn, rows: list[tuple]) -> None:
    sql = """
        INSERT INTO predictions (spot_id, date, score, best_start, best_end, model_version)
        VALUES (%s, %s, %s, %s, %s, %s)
        ON CONFLICT (spot_id, date)
        DO UPDATE SET score = EXCLUDED.score,
                      best_start = EXCLUDED.best_start,
                      best_end = EXCLUDED.best_end,
                      model_version = EXCLUDED.model_version,
                      created_at = now();
    """
    with conn.cursor() as cur:
        cur.executemany(sql, rows)
    conn.commit()


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--from", dest="d0", required=True)
    ap.add_argument("--to", dest="d1", required=True)
    ap.add_argument("--spots", nargs="*")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    load_dotenv()
    conn = psycopg2.connect(**DB)
    try:
        spots = load_spots(conn, args.spots)
        solunar = load_solunar(conn, args.d0, args.d1)
        weather = load_weather_daily(conn, args.d0, args.d1)
    finally:
        conn.close()

    if solunar.empty:
        print("Нет солунарных данных в диапазоне — запустите Klevo.Ingest solunar")
        sys.exit(1)

    idx = [(s["spot_id"], d) for _, s in spots.iterrows()
           for d in pd.date_range(args.d0, args.d1, freq="D")]
    base = pd.DataFrame(idx, columns=["spot_id", "date"])
    base["date"] = pd.to_datetime(base["date"]).astype("datetime64[us]")
    base = base.merge(solunar, on=["spot_id", "date"], how="left")
    base = base.merge(weather, on=["spot_id", "date"], how="left")
    base = _window_hours(base, "major")
    base = _window_hours(base, "minor")

    df = compute_scores(base)

    rows = [
        (r.spot_id, r.date.strftime("%Y-%m-%d"), int(r.score),
         r.best_start.strftime("%H:%M") if r.best_start else None,
         r.best_end.strftime("%H:%M") if r.best_end else None,
         MODEL_VERSION)
        for r in df.itertuples()
    ]
    if args.dry_run:
        for r in rows[:10]:
            print(f"  {r[0][:8]} {r[1]} score={r[2]} best={r[3]}-{r[4]}")
        print(f"DRY-RUN: {len(rows)} записей (не записано)")
        return

    conn = psycopg2.connect(**DB)
    try:
        upsert(conn, rows)
    finally:
        conn.close()
    print(f"predictions: {len(rows)} записей, model_version={MODEL_VERSION}")


if __name__ == "__main__":
    main()
