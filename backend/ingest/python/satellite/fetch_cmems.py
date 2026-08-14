#!/usr/bin/env python3
"""Klevo: загрузка спутниковых наблюдений CMEMS (Baltic Sea Reanalysis) в БД.

Источник (физика): BALTICSEA_MULTIYEAR_PHY_003_011, dataset cmems_mod_bal_phy_my_P1D-m.
Переменные: thetao (поверхность), so (поверхность), mlotst, bottomT.
Источник (биогеохимия, флаг --bgc): BALTICSEA_MULTIYEAR_BGC_003_012,
dataset cmems_mod_bal_bgc_my_P1M-m, переменная chl -> chla_mgm3.
Для точек вне маски моря берётся ближайшая валидная ячейка в радиусе max_km.

Пример:
    python fetch_cmems.py --from 2026-05-01 --to 2026-05-31
    python fetch_cmems.py --bgc --from 2023-01-01 --to 2026-05-31
"""
from __future__ import annotations

import argparse
import math
import os
import subprocess
import sys
import tempfile
from pathlib import Path

import numpy as np
import psycopg2
import xarray as xr

DS_ID = "cmems_mod_bal_phy_my_P1D-m"
SOURCE = "cmems_bal_my_phy"
BGC_DS_ID = "cmems_mod_bal_bgc_my_P1M-m"
BGC_SOURCE = "cmems_bal_my_bgc"
KM_PER_DEG_LAT = 111.0
MAX_KM = 15.0
PAD_DEG = 0.35

DB = {
    "host": os.environ.get("KLEVO_DB_HOST", "localhost"),
    "port": int(os.environ.get("KLEVO_DB_PORT", "5432")),
    "dbname": os.environ.get("KLEVO_DB_NAME", "klevo"),
    "user": os.environ.get("KLEVO_DB_USER", "postgres"),
    "password": os.environ.get("PGPASSWORD", "klevo_dev_pwd"),
}


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


def subset(spot_id: str, lon: float, lat: float, d0: str, d1: str,
           out_dir: Path, dry_run: bool, bgc: bool = False) -> Path | None:
    """Выгружает малый bbox вокруг точки через copernicusmarine subset."""
    exe = Path(sys.executable).parent / "copernicusmarine.exe"
    ds_id, var_args = (BGC_DS_ID, ["--variable", "chl"]) if bgc else \
        (DS_ID, ["--variable", "thetao", "--variable", "bottomT",
                 "--variable", "mlotst", "--variable", "so"])
    args = [
        str(exe), "subset",
        "-i", ds_id,
        *var_args,
        "--minimum-longitude", f"{lon - PAD_DEG:.3f}",
        "--maximum-longitude", f"{lon + PAD_DEG:.3f}",
        "--minimum-latitude", f"{lat - PAD_DEG:.3f}",
        "--maximum-latitude", f"{lat + PAD_DEG:.3f}",
        "--minimum-depth", "0", "--maximum-depth", "2",
        "--start-datetime", d0, "--end-datetime", d1,
        "--output-directory", str(out_dir),
        "--output-filename", f"{spot_id}",
        "--overwrite", "--file-format", "netcdf",
        "--disable-progress-bar", "--log-level", "ERROR",
    ]
    if dry_run:
        print("  [dry-run] subset:", " ".join(args[:8]), f"bbox={lon:.3f},{lat:.3f}", d0, d1)
        return None
    res = subprocess.run(args, capture_output=True, text=True)
    if res.returncode != 0:
        print(f"  subset failed ({spot_id}): {res.stderr.strip()[-500:]}")
        return None
    return out_dir / f"{spot_id}.nc"


def nearest_valid(da: xr.DataArray, lon: float, lat: float, max_km: float) -> float | None:
    """Ищет ближайшую не-NaN ячейку на временном срезе da (lat, lon)."""
    lat_arr = da.latitude.values.astype(float)
    lon_arr = da.longitude.values.astype(float)
    dlon_km = (lon_arr - lon) * KM_PER_DEG_LAT * math.cos(math.radians(lat))
    dlat_km = (lat_arr - lat) * KM_PER_DEG_LAT
    d = np.sqrt(dlon_km[None, :] ** 2 + dlat_km[:, None] ** 2)
    d = np.where(da.notnull().values, d, np.inf)
    j, i = np.unravel_index(int(np.argmin(d)), d.shape)
    dist_km = float(d[j, i])
    if math.isinf(dist_km) or dist_km > max_km:
        return None
    return float(da.values[j, i])


def extract(path: Path, lon: float, lat: float, max_km: float, bgc: bool = False) -> list[tuple]:
    """Возвращает кортежи (date, ...значения) для физики или биогеохимии."""
    ds = xr.open_dataset(path)
    rows: list[tuple] = []
    with ds:
        times = ds.time.values
        if bgc:
            chl = ds["chl"].isel(depth=0)
            for t_idx, t in enumerate(times):
                d = np.datetime64(t, "D").astype("datetime64[ns]")
                v = nearest_valid(chl[t_idx], lon, lat, max_km)
                if v is None or v < 0.05:
                    continue
                rows.append((d.astype("datetime64[D]").astype(object).strftime("%Y-%m-%d"),
                             round(v, 4)))
            return rows

        thetao = ds["thetao"].isel(depth=0)
        for t_idx, t in enumerate(times):
            d = np.datetime64(t, "D").astype("datetime64[ns]")
            sst = nearest_valid(thetao[t_idx], lon, lat, max_km)
            if sst is None:
                continue
            bottom = nearest_valid(ds["bottomT"][t_idx], lon, lat, max_km)
            mlotst = nearest_valid(ds["mlotst"][t_idx], lon, lat, max_km)
            so = nearest_valid(ds["so"].isel(depth=0)[t_idx], lon, lat, max_km)
            rows.append((
                d.astype("datetime64[D]").astype(object).strftime("%Y-%m-%d"),
                None if sst is None else round(sst, 2),
                None if bottom is None else round(bottom, 2),
                None if mlotst is None else round(mlotst, 1),
                None if so is None else round(so, 2),
            ))
    return rows


def upsert(conn, rows: list[tuple], spot_id: str, bgc: bool = False) -> None:
    if bgc:
        sql = """
            INSERT INTO satellite_obs (spot_id, observed_at, chla_mgm3, source)
            VALUES (%s, %s, %s, %s)
            ON CONFLICT (spot_id, observed_at, source)
            DO UPDATE SET chla_mgm3 = EXCLUDED.chla_mgm3;
        """
        params = [(spot_id, *r, BGC_SOURCE) for r in rows]
    else:
        sql = """
            INSERT INTO satellite_obs (spot_id, observed_at, sst_c, bottom_t_c, mlotst_m, salinity_psu, source)
            VALUES (%s, %s, %s, %s, %s, %s, %s)
            ON CONFLICT (spot_id, observed_at, source)
            DO UPDATE SET sst_c = EXCLUDED.sst_c,
                          bottom_t_c = EXCLUDED.bottom_t_c,
                          mlotst_m = EXCLUDED.mlotst_m,
                          salinity_psu = EXCLUDED.salinity_psu;
        """
        params = [(spot_id, *r, SOURCE) for r in rows]
    with conn.cursor() as cur:
        cur.executemany(sql, params)
    conn.commit()


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--from", dest="d0", required=True, help="начало периода YYYY-MM-DD")
    ap.add_argument("--to", dest="d1", required=True, help="конец периода YYYY-MM-DD")
    ap.add_argument("--spots", nargs="*", help="ограничить точки по UUID")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--bgc", action="store_true", help="биогеохимия (chl -> chla_mgm3)")
    ap.add_argument("--max-km", type=float, default=MAX_KM)
    args = ap.parse_args()

    load_dotenv()
    with tempfile.TemporaryDirectory() as tmp:
        out_dir = Path(tmp)

        if args.dry_run:
            print("DRY-RUN: записи в БД не выполняются")
        conn = psycopg2.connect(**DB)
        with conn.cursor() as cur:
            cur.execute("SELECT id::text, ST_X(location::geometry), ST_Y(location::geometry), name FROM spots ORDER BY name")
            spots = [(r[0], float(r[1]), float(r[2]), r[3]) for r in cur.fetchall()]
        if args.spots:
            wanted = set(args.spots)
            spots = [s for s in spots if s[0] in wanted]
        print(f"Точек: {len(spots)}; период {args.d0} .. {args.d1}")

        for spot_id, lon, lat, name in spots:
            nc = subset(spot_id, lon, lat, args.d0, args.d1, out_dir, args.dry_run, args.bgc)
            if nc is None:
                if args.dry_run:
                    print(f"  {name}: (dry-run) пропуск")
                else:
                    print(f"  {name}: выгрузка не удалась")
                continue
            rows = extract(nc, lon, lat, args.max_km, args.bgc)
            if not rows:
                print(f"  {name}: нет валидных данных (суша/вне маски)")
                continue
            if args.dry_run:
                print(f"  {name}: дней {len(rows)}, пример {rows[0]}")
            else:
                upsert(conn, rows, spot_id, args.bgc)
                label = f"chl={rows[-1][1]}" if args.bgc else f"sst={rows[-1][1]} C"
                print(f"  {name}: {len(rows)} записей ({label})")

    conn.close()


if __name__ == "__main__":
    main()
