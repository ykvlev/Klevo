#!/usr/bin/env python3
"""Klevo: загрузка SST MODIS Aqua (NASA OB.DAAC) в satellite_obs.

Продукт: AQUA_MODIS.<дата>.L3m.DAY.SST.sst.4km.nc (глобальная сетка 4 км).
Покрывает крупные озёра (Ладога), в отличие от CHL-продукта, который маскирует
внутренние воды. Для точек вне валидных пикселей ищется ближайшая ячейка
в радиусе max_km с качеством qual_sst <= max_qual (0 = best, 1 = good).

Источники (--mirror):
    podaac  — зеркало PODAAC Earthdata Cloud (archive.podaac.earthdata.nasa.gov),
              работает, когда oceandata.sci.gsfc.nasa.gov недоступен.
    ocean   — оригинальный getfile OB.DAAC (oceandata.sci.gsfc.nasa.gov).

Пример:
    python fetch_nasa.py --from 2025-08-01 --to 2025-08-31
    python fetch_nasa.py --mirror podaac --from 2026-05-27 --to 2026-08-15
"""
from __future__ import annotations

import argparse
import math
import os
import tempfile
from datetime import date, timedelta
from pathlib import Path

import numpy as np
import psycopg2
import requests
import xarray as xr

LEGACY_URL = "https://oceandata.sci.gsfc.nasa.gov/getfile"
PODAAC_BASE = ("https://archive.podaac.earthdata.nasa.gov/podaac-ops-cumulus-protected/"
               "MODIS_AQUA_L3_SST_THERMAL_DAILY_4KM_DAYTIME_V2019.0")
SOURCE = "nasa_modis_aqua"
KM_PER_DEG_LAT = 111.0
MAX_KM = 15.0
MAX_QUAL = 1
BOX_DEG = 2.0
PAD_DEG = 0.5

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


def nearest_valid(v: xr.DataArray, qual: xr.DataArray, lon: float, lat: float,
                  max_km: float) -> float | None:
    """Ближайшая не-NaN ячейка с качеством <= MAX_QUAL в радиусе max_km."""
    good = v.notnull().values & (qual.values <= MAX_QUAL)
    lat_arr = v.lat.values.astype(float)
    lon_arr = v.lon.values.astype(float)
    dlat_km = (lat_arr - lat) * KM_PER_DEG_LAT
    dlon_km = (lon_arr - lon) * KM_PER_DEG_LAT * math.cos(math.radians(lat))
    d = np.sqrt(dlat_km[:, None] ** 2 + dlon_km[None, :] ** 2)
    d = np.where(good, d, np.inf)
    j, i = np.unravel_index(int(np.argmin(d)), d.shape)
    dist_km = float(d[j, i])
    if math.isinf(dist_km) or dist_km > max_km:
        return None
    return float(v.values[j, i])


def upsert(conn, rows: list[tuple], spot_id: str) -> None:
    sql = """
        INSERT INTO satellite_obs (spot_id, observed_at, sst_c, source)
        VALUES (%s, %s, %s, %s)
        ON CONFLICT (spot_id, observed_at, source)
        DO UPDATE SET sst_c = EXCLUDED.sst_c;
    """
    with conn.cursor() as cur:
        cur.executemany(sql, [(spot_id, *r, SOURCE) for r in rows])
    conn.commit()


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--from", dest="d0", required=True, help="начало периода YYYY-MM-DD")
    ap.add_argument("--to", dest="d1", required=True, help="конец периода YYYY-MM-DD")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--max-km", type=float, default=MAX_KM)
    ap.add_argument("--mirror", choices=("ocean", "podaac"), default="podaac",
                    help="источник файлов (default: podaac)")
    args = ap.parse_args()

    load_dotenv()
    token = os.environ.get("EARTHDATA_TOKEN", "")
    if not token:
        sys_exit("Нет EARTHDATA_TOKEN в .env")

    conn = psycopg2.connect(**DB)
    with conn.cursor() as cur:
        cur.execute("SELECT id::text, ST_X(location::geometry), ST_Y(location::geometry), name FROM spots ORDER BY name")
        spots = [(r[0], float(r[1]), float(r[2]), r[3]) for r in cur.fetchall()]

    d0 = date.fromisoformat(args.d0)
    d1 = date.fromisoformat(args.d1)
    days = (d1 - d0).days + 1
    print(f"Точек: {len(spots)}; период {args.d0} .. {args.d1} ({days} дней)")
    if args.dry_run:
        print("DRY-RUN: скачивание файлов выполняется, записи в БД — нет")

    headers = {"Authorization": f"Bearer {token}"}
    sess = requests.Session()
    n_days = n_found = n_rows = 0
    day = d0
    with tempfile.TemporaryDirectory() as tmp:
        tmp = Path(tmp)
        while day <= d1:
            n_days += 1
            fn = f"AQUA_MODIS.{day.strftime('%Y%m%d')}.L3m.DAY.SST.sst.4km"
            if args.mirror == "ocean":
                urls = [f"{LEGACY_URL}/{fn}.nc"]
            else:
                urls = [
                    f"{PODAAC_BASE}/{fn}.nc",
                    f"{PODAAC_BASE}/{fn}.NRT.nc",
                ]
            path = content = None
            for url in urls:
                try:
                    r = sess.get(url, headers=headers, timeout=90)
                except requests.RequestException as exc:
                    print(f"  {day}: сеть ({exc}); пробую следующий источник")
                    continue
                if r.status_code == 200:
                    content = r.content
                    break
                if r.status_code == 404:
                    continue
                print(f"  {day}: HTTP {r.status_code} ({url}); пробую следующий источник")
            if content is None:
                day += timedelta(days=1)
                continue
            n_found += 1
            path = tmp / f"{fn}{'.NRT.nc' if '.NRT' in r.url else '.nc'}"
            path.write_bytes(content)
            try:
                ds = xr.open_dataset(path)
                with ds:
                    for spot_id, lon, lat, name in spots:
                        crop = ds.sel(
                            lat=slice(lat + BOX_DEG, lat - BOX_DEG),
                            lon=slice(lon - BOX_DEG, lon + BOX_DEG),
                        )
                        val = nearest_valid(crop["sst"], crop["qual_sst"], lon, lat, args.max_km)
                        if val is None:
                            continue
                        row = (day.strftime("%Y-%m-%d"), round(val, 2))
                        if args.dry_run:
                            print(f"  {day} {name}: sst={row[1]} C")
                        else:
                            upsert(conn, [row], spot_id)
                            n_rows += 1
            except Exception as exc:
                print(f"  {day}: parse error ({exc}); пропуск")
            path.unlink(missing_ok=True)
            day += timedelta(days=1)

    conn.close()
    print(f"Готово: дней {n_days}, файлов скачано {n_found}, строк записано {n_rows}")


def sys_exit(msg: str) -> None:
    print(msg)
    raise SystemExit(1)


if __name__ == "__main__":
    main()
