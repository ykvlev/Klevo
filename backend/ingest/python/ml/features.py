#!/usr/bin/env python3
"""Klevo, Фаза 3: построение ежедневной матрицы признаков для модели клёва.

Источники (БД klevo):
  - weather_obs    -> суточные агрегаты погоды (Open-Meteo, часовые)
  - solunar_daily  -> солунар на день (фаза луны, окна клёва)
  - satellite_obs  -> SST (CMEMS/NASA, forward-fill), Chl-a (CMEMS BGC, месячное)

Выход: CSV с одной строкой на (spot_id, date). См. --out.

Пример:
    python features.py --from 2023-01-01 --to 2026-05-31
    python features.py --from 2026-08-14 --to 2026-08-20 --spots <uuid>
"""
from __future__ import annotations

import argparse
import os
import sys
from pathlib import Path

import pandas as pd
import psycopg2

DB = {
    "host": os.environ.get("KLEVO_DB_HOST", "localhost"),
    "port": int(os.environ.get("KLEVO_DB_PORT", "5432")),
    "dbname": os.environ.get("KLEVO_DB_NAME", "klevo"),
    "user": os.environ.get("KLEVO_DB_USER", "postgres"),
    "password": os.environ.get("PGPASSWORD", "klevo_dev_pwd"),
}

LOCAL_TZ = "Europe/Moscow"
SST_MAX_DAYS = 30
CHLA_MAX_DAYS = 60
OCEAN_MAX_DAYS = 30


def load_dotenv() -> None:
    env_path = Path(__file__).parent / ".env"
    if not env_path.exists():
        return
    for line in env_path.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, value = line.split("=", 1)
        os.environ.setdefault(key, value)


def _conn() -> psycopg2.extensions.connection:
    return psycopg2.connect(**DB)


def load_spots(conn, only: list[str] | None = None) -> pd.DataFrame:
    with conn.cursor() as cur:
        cur.execute(
            "SELECT id::text, name, ST_Y(location::geometry), ST_X(location::geometry) "
            "FROM spots ORDER BY name"
        )
        df = pd.DataFrame(cur.fetchall(), columns=["spot_id", "name", "lat", "lon"])
    if only:
        df = df[df["spot_id"].isin(only)]
    return df


def load_weather_daily(conn, d0: str, d1: str) -> pd.DataFrame:
    sql = """
        SELECT spot_id::text,
               (observed_at AT TIME ZONE %s)::date AS date,
               min(temperature_2m)        AS t_min,
               avg(temperature_2m)        AS t_mean,
               max(temperature_2m)        AS t_max,
               avg(pressure_msl)          AS pressure_mean,
               max(pressure_msl) - min(pressure_msl) AS pressure_amp,
               avg(humidity_2m)           AS humidity_mean,
               avg(wind_speed_10m)        AS wind_mean,
               max(wind_speed_10m)        AS wind_max,
               sum(precip)                AS precip_sum,
               avg(cloud_cover)           AS cloud_mean,
               max(snow_depth)            AS snow_max
        FROM weather_obs
        WHERE observed_at >= %s::timestamptz AND observed_at < (%s::date + 1)::timestamptz
        GROUP BY spot_id, (observed_at AT TIME ZONE %s)::date
    """
    with conn.cursor() as cur:
        cur.execute(sql, (LOCAL_TZ, f"{d0}T00:00:00Z", d1, LOCAL_TZ))
        cols = [c.name for c in cur.description]
        df = pd.DataFrame(cur.fetchall(), columns=cols)
    for c in df.columns:
        if c not in ("spot_id", "date"):
            df[c] = pd.to_numeric(df[c], errors="coerce")
    df["date"] = pd.to_datetime(df["date"])
    return df


def load_solunar(conn, d0: str, d1: str) -> pd.DataFrame:
    sql = """
        SELECT spot_id::text, date::date AS date, moon_phase, moon_illumination,
               major_start, major_end, major2_start, major2_end,
               minor_start, minor_end, minor2_start, minor2_end,
               sun_rise, sun_set, dawn, dusk
        FROM solunar_daily
        WHERE date BETWEEN %s AND %s
    """
    with conn.cursor() as cur:
        cur.execute(sql, (d0, d1))
        cols = [c.name for c in cur.description]
        df = pd.DataFrame(cur.fetchall(), columns=cols)
    df["date"] = pd.to_datetime(df["date"])
    df["moon_phase"] = pd.to_numeric(df["moon_phase"], errors="coerce")
    df["moon_illumination"] = pd.to_numeric(df["moon_illumination"], errors="coerce")
    return df


def load_satellite(conn) -> pd.DataFrame:
    sql = """
        SELECT spot_id::text, observed_at::date AS date,
               sst_c, bottom_t_c, mlotst_m, salinity_psu, chla_mgm3, source
        FROM satellite_obs
    """
    with conn.cursor() as cur:
        cur.execute(sql)
        cols = [c.name for c in cur.description]
        df = pd.DataFrame(cur.fetchall(), columns=cols)
    df["date"] = pd.to_datetime(df["date"])
    return df


def _fuse(df: pd.DataFrame, sat: pd.DataFrame, key: str, sources: list[str],
          max_days: int) -> pd.DataFrame:
    """Forward-fill значения признака из satellite_obs (приоритет по sources)."""
    part = sat[sat["source"].isin(sources)][["spot_id", "date", "source", key]].dropna()
    part = part[part["date"].notna()]
    if part.empty:
        df[key] = pd.NA
        return df
    if len(sources) > 1:
        order = {s: i for i, s in enumerate(sources)}
        part = part.sort_values("date").drop_duplicates(
            ["spot_id", "date"], keep="last")
        part["_prio"] = part["source"].map(order)
        part = part.sort_values(["spot_id", "date", "_prio"]).drop_duplicates(
            ["spot_id", "date"], keep="first")
    part = part.rename(columns={"date": "date_src", key: f"{key}_raw"})
    df["date"] = pd.to_datetime(df["date"]).astype("datetime64[us]")
    part["date_src"] = pd.to_datetime(part["date_src"]).astype("datetime64[us]")
    out = pd.merge_asof(
        df.sort_values("date"),
        part.sort_values("date_src"),
        left_on="date", right_on="date_src", by="spot_id", direction="backward",
        allow_exact_matches=True,
    )
    out[key] = out[f"{key}_raw"].where(
        (out["date"] - out["date_src"]).dt.days <= max_days, pd.NA)
    out.drop(columns=[f"{key}_raw", "date_src", "source", "_prio",
                      "_prio_x", "_prio_y"], inplace=True, errors="ignore")
    return out


def build_features(d0: str, d1: str, only: list[str] | None = None) -> pd.DataFrame:
    conn = _conn()
    try:
        spots = load_spots(conn, only)
        weather = load_weather_daily(conn, d0, d1)
        solunar = load_solunar(conn, d0, d1)
        sat = load_satellite(conn)
    finally:
        conn.close()

    idx = []
    days = pd.date_range(d0, d1, freq="D")
    for _, s in spots.iterrows():
        for d in days:
            idx.append((s["spot_id"], d))
    base = pd.DataFrame(idx, columns=["spot_id", "date"])

    base = base.merge(solunar, on=["spot_id", "date"], how="left")
    base = base.merge(weather, on=["spot_id", "date"], how="left")

    base = _fuse(base, sat, "sst_c", ["cmems_bal_my_phy", "nasa_modis_aqua"], SST_MAX_DAYS)
    base = _fuse(base, sat, "chla_mgm3", ["cmems_bal_my_bgc"], CHLA_MAX_DAYS)
    base = _fuse(base, sat, "mlotst_m", ["cmems_bal_my_phy"], OCEAN_MAX_DAYS)
    base = _fuse(base, sat, "salinity_psu", ["cmems_bal_my_phy"], OCEAN_MAX_DAYS)

    base = _window_hours(base, "major")
    base = _window_hours(base, "minor")

    base = base.sort_values(["spot_id", "date"]).reset_index(drop=True)

    g = base.groupby("spot_id", sort=False)
    base["t_mean_lag1"] = g["t_mean"].shift(1)
    base["pressure_lag1"] = g["pressure_mean"].shift(1)
    base["wind_lag1"] = g["wind_mean"].shift(1)
    base["t_delta"] = base["t_mean"] - base["t_mean_lag1"]
    base["pressure_delta"] = base["pressure_mean"] - base["pressure_lag1"]

    base["doy"] = base["date"].dt.dayofyear
    base["month"] = base["date"].dt.month
    base["weekday"] = base["date"].dt.weekday
    base["season"] = base["month"].map({12: 0, 1: 0, 2: 0, 3: 1, 4: 1, 5: 1,
                                        6: 2, 7: 2, 8: 2, 9: 3, 10: 3, 11: 3})
    base["spot_id"] = base["spot_id"].astype(str)
    return base


def _window_hours(df: pd.DataFrame, kind: str) -> pd.DataFrame:
    """Сколько часов окна (major/minor) приходится на сутки, и длительность лучшего."""
    cols = [f"{kind}_start", f"{kind}_end", f"{kind}2_start", f"{kind}2_end"]
    df[f"{kind}_hours"] = 0.0
    df[f"{kind}_best_h"] = 0.0
    for i in range(2):
        start_c, end_c = f"{kind}_start" if i == 0 else f"{kind}2_start", \
                         f"{kind}_end" if i == 0 else f"{kind}2_end"
        s = pd.to_datetime(df[start_c], utc=True, errors="coerce")
        e = pd.to_datetime(df[end_c], utc=True, errors="coerce")
        day = pd.to_datetime(df["date"], utc=True)
        nxt = day + pd.Timedelta(days=1)
        lo = s.clip(lower=day, upper=nxt)
        hi = e.clip(lower=day, upper=nxt)
        hours = ((hi - lo).dt.total_seconds() / 3600).clip(lower=0)
        df[f"{kind}_hours"] += hours.fillna(0.0)
        df[f"{kind}_best_h"] = df[f"{kind}_best_h"].where(
            df[f"{kind}_best_h"] >= hours.fillna(0.0), hours.fillna(0.0))
    df[f"{kind}_count"] = 0
    for i in range(2):
        start_c = f"{kind}_start" if i == 0 else f"{kind}2_start"
        s = pd.to_datetime(df[start_c], utc=True, errors="coerce")
        day = pd.to_datetime(df["date"], utc=True)
        nxt = day + pd.Timedelta(days=1)
        df[f"{kind}_count"] += ((s >= day) & (s < nxt)).astype(int).fillna(0)
    return df


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--from", dest="d0", required=True)
    ap.add_argument("--to", dest="d1", required=True)
    ap.add_argument("--spots", nargs="*", help="ограничить точки по UUID")
    ap.add_argument("--out", default=None, help="путь CSV (по умолчанию stdout)")
    args = ap.parse_args()

    load_dotenv()
    df = build_features(args.d0, args.d1, args.spots)
    if args.out:
        df.to_csv(args.out, index=False)
        print(f"{len(df)} строк -> {args.out}")
    else:
        df.to_csv(sys.stdout, index=False)


if __name__ == "__main__":
    main()
