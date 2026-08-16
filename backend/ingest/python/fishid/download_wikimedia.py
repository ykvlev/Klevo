"""Скачивает датасет изображений видов рыб с Wikimedia Commons.

Структура вывода (data/fishid/raw):
    data/fishid/raw/<latin>/<latin>_<n>.jpg

Использование:
    python download_wikimedia.py [--max-per-species N] [--thumb W]
"""

import argparse
import io
import json
import os
import sys
import time
import urllib.parse

import requests
from PIL import Image

API = "https://commons.wikimedia.org/w/api.php"
UA = "klevo-fishid/0.1 (dataset bootstrap; contact: dev@klevo.local)"
OUT = os.path.join("data", "fishid", "raw")

SPECIES = {
    "Sander_lucioperca": {"name_ru": "судак", "category": "Sander lucioperca"},
    "Esox_lucius": {"name_ru": "щука", "category": "Esox lucius"},
    "Abramis_brama": {"name_ru": "лещ", "category": "Abramis brama"},
    "Osmerus_eperlanus": {"name_ru": "корюшка", "category": "Osmerus eperlanus"},
    "Salmo_trutta": {"name_ru": "кумжа", "category": "Salmo trutta"},
    "Salmo_salar": {"name_ru": "лосось атлантический", "category": "Salmo salar"},
    "Coregonus_lavaretus": {"name_ru": "сиг", "category": "Coregonus lavaretus"},
    "Thymallus_thymallus": {"name_ru": "хариус", "category": "Thymallus thymallus"},
    "Acipenser_sturio": {"name_ru": "осетр атлантический", "category": "Acipenser sturio"},
    "Astacus_astacus": {"name_ru": "раки", "category": "Astacus astacus"},
}


def api_query(params):
    params = {k: v for k, v in params.items() if v is not None}
    params.update({"format": "json", "formatversion": "2"})
    while True:
        for attempt in range(6):
            r = requests.get(API, params=params, headers={"User-Agent": UA}, timeout=30)
            if r.status_code == 429:
                wait = int(r.headers.get("Retry-After", 3 * (attempt + 1)))
                time.sleep(wait)
                continue
            break
        r.raise_for_status()
        data = r.json()
        yield data
        if not data.get("continue"):
            return
        params.update(data["continue"])


def category_files(category, wanted):
    """Возвращает до `wanted` файлов (title -> thumb url) из категории."""
    files = {}
    try:
        for data in api_query(
            {
                "action": "query",
                "generator": "categorymembers",
                "gcmtitle": f"Category:{category}",
                "gcmtype": "file",
                "gcmlimit": "100",
                "prop": "imageinfo",
                "iiprop": "url|size|mime",
                "iiurlwidth": str(THUMB),
            }
        ):
            for page in data.get("query", {}).get("pages", []):
                ii = page.get("imageinfo")
                if not ii or len(files) >= wanted:
                    continue
                info = ii[0]
                mime = info.get("mime", "")
                if mime not in ("image/jpeg", "image/png", "image/webp"):
                    continue
                if info.get("size", 0) < 20_000:
                    continue
                files[page["title"]] = info.get("thumburl") or info.get("url")
                if len(files) >= wanted:
                    break
    except Exception as e:  # noqa: BLE001
        print(f"  [warn] category {category}: {e}", file=sys.stderr)
    return files


def search_files(query, wanted):
    """Дополнительный поиск по ключевым словам (fallback, если в категории мало файлов)."""
    files = {}
    try:
        for data in api_query(
            {
                "action": "query",
                "generator": "search",
                "gsrsearch": f"filetype:bitmap {query}",
                "gsrnamespace": "6",
                "gsrlimit": "50",
                "prop": "imageinfo",
                "iiprop": "url|size|mime",
                "iiurlwidth": str(THUMB),
            }
        ):
            for page in data.get("query", {}).get("pages", []):
                ii = page.get("imageinfo")
                if not ii or len(files) >= wanted:
                    continue
                info = ii[0]
                mime = info.get("mime", "")
                if mime not in ("image/jpeg", "image/png", "image/webp"):
                    continue
                if info.get("size", 0) < 20_000:
                    continue
                files[page["title"]] = info.get("thumburl") or info.get("url")
                if len(files) >= wanted:
                    break
    except Exception as e:  # noqa: BLE001
        print(f"  [warn] search {query}: {e}", file=sys.stderr)
    return files


def download(url, dest, retries=6):
    for attempt in range(retries):
        try:
            r = requests.get(url, headers={"User-Agent": UA}, timeout=60)
            if r.status_code == 429:
                wait = int(r.headers.get("Retry-After", 3 * (attempt + 1)))
                time.sleep(wait)
                continue
            r.raise_for_status()
            img = Image.open(io.BytesIO(r.content))
            img = img.convert("RGB")
            if img.width < 64 or img.height < 64:
                raise ValueError("too small")
            img.save(dest, "JPEG", quality=88)
            return dest
        except requests.RequestException:
            if attempt == retries - 1:
                raise
            time.sleep(1.5 * (attempt + 1))
    raise RuntimeError(f"download failed after {retries} attempts: {url}")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--max-per-species", type=int, default=150)
    parser.add_argument("--thumb", type=int, default=640)
    args = parser.parse_args()
    global THUMB
    THUMB = args.thumb

    os.makedirs(OUT, exist_ok=True)
    manifest = {}
    for latin, meta in SPECIES.items():
        dest = os.path.join(OUT, latin)
        os.makedirs(dest, exist_ok=True)
        existing = [f for f in os.listdir(dest) if f.endswith(".jpg")]
        if len(existing) >= args.max_per_species:
            print(f"{latin}: already {len(existing)} images, skip")
            manifest[latin] = existing
            continue

        print(f"{latin} ({meta['name_ru']}): fetching…")
        files = category_files(meta["category"], args.max_per_species)
        if len(files) < 40:
            extra = search_files(meta["category"], args.max_per_species - len(files))
            for k, v in extra.items():
                files.setdefault(k, v)
        print(f"  {len(files)} candidate files")

        saved, fails = [], 0
        for n, (title, url) in enumerate(files.items()):
            if saved and len(saved) >= args.max_per_species:
                break
            fname = f"{latin}_{n:03d}.jpg"
            fpath = os.path.join(dest, fname)
            try:
                download(url, fpath)
                saved.append(fname)
            except Exception:  # noqa: BLE001
                fails += 1
            if n % 20 == 0:
                print(f"  {n}/{len(files)}…")
            time.sleep(0.35)
        print(f"  saved {len(saved)}, failed {fails}")
        manifest[latin] = saved
        time.sleep(1.0)

    with open(os.path.join(OUT, "manifest.json"), "w", encoding="utf-8") as f:
        json.dump(manifest, f, ensure_ascii=False, indent=2)
    print("done.")


if __name__ == "__main__":
    main()
