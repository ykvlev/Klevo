"""Seed правил рыболовства из JSON в БД klevo.

Запуск:
    set PGUSER=postgres & set PGPASSWORD=... & python backend/db/seed_rules.py
или через переменные окружения PGHOST/PGPORT/PGDATABASE.
"""
import json
import os
import re
from pathlib import Path

import psycopg2

ROOT = Path(__file__).resolve().parent.parent.parent
JSON_PATH = ROOT / "data" / "rules" / "zapadnyi_bassein_lenobl.json"

# Мастер-справочник видов: нормализованное имя -> (научное, список алиасов, ракообразное?)
SPECIES_MASTER = {
    "судак": ("Sander lucioperca", ["судак обыкновенный"], False),
    "щука": ("Esox lucius", ["щука обыкновенная"], False),
    "лещ": ("Abramis brama", ["лещ обыкновенный"], False),
    "корюшка": ("Osmerus eperlanus", ["корюшка европейская", "снеток"], False),
    "сиг": ("Coregonus lavaretus", ["сиг обыкновенный"], False),
    "хариус": ("Thymallus thymallus", ["хариус европейский"], False),
    "раки": ("Astacus astacus", ["рак речной", "рак"], True),
    "мотыль": ("Chironomidae", ["мотыль (хирономиды)", "хирономиды"], False),
    "лосось атлантический": ("Salmo salar", ["семга", "лосось"], False),
    "осетр атлантический": ("Acipenser sturio", ["осетр"], False),
    "кумжа": ("Salmo trutta", ["форель", "кумжа (форель)"], False),
    "озерная форель": ("Salmo trutta morpha lacustris", [], False),
    "лосось озерный": ("Salmo salar morpha sebago", [], False),
    "сиг волховский": ("Coregonus lavaretus baeri", [], False),
    "ладожская нерпа": ("Pusa hispida ladogensis", ["нерпа"], False),
}

# Ключи из JSON (min_size/daily_limit) -> имя в SPECIES_MASTER
JSON_KEY_TO_SPECIES = {
    "судак": "судак",
    "щука": "щука",
    "лещ": "лещ",
    "корюшка": "корюшка",
    "сиг": "сиг",
    "хариус": "хариус",
    "раки": "раки",
    "мотыль (хирономиды)": "мотыль",
}


def norm_key(key: str) -> str:
    return key.strip().lower()


def find_species(text: str) -> list[str]:
    """Находит виды из мастер-справочника внутри строки-запрета.

    Совпадения по самому длинному имени (чтобы «сиг» не дублировал «сиг волховский»).
    """
    lowered = text.lower()
    found = []
    for name in sorted(SPECIES_MASTER, key=len, reverse=True):
        if re.search(re.escape(name), lowered) and not any(
            name in other for other in found
        ):
            found.append(name)
    return found


def main() -> None:
    with open(JSON_PATH, encoding="utf-8") as f:
        data = json.load(f)

    conn = psycopg2.connect(
        host=os.getenv("PGHOST", "localhost"),
        port=os.getenv("PGPORT", "5432"),
        dbname=os.getenv("PGDATABASE", "klevo"),
        user=os.getenv("PGUSER", "postgres"),
        password=os.getenv("PGPASSWORD", "klevo_dev_pwd"),
    )
    cur = conn.cursor()

    # --- Полный сброс данных правил (скрипт идемпотентный) ---
    for table in (
        "zone_bans",
        "zone_limit_rules",
        "zone_size_rules",
        "zone_default_limits",
        "fishery_zones",
        "fish_species",
        "fishery_basins",
    ):
        cur.execute(f"TRUNCATE {table} CASCADE")

    # --- Справочники: бассейн, зоны, виды ---
    basin = data["basin"]
    cur.execute(
        "INSERT INTO fishery_basins (id, name) VALUES (%s, %s) ON CONFLICT (id) DO NOTHING",
        ("zapadny", basin["name"]),
    )

    zone_ids = {}
    for z in data["zones"]:
        zone_ids[z["id"]] = z
        cur.execute(
            """
            INSERT INTO fishery_zones (id, basin_id, name, section, source, pilot)
            VALUES (%s, %s, %s, %s, %s, %s)
            ON CONFLICT (id) DO NOTHING
            """,
            (
                z["id"],
                "zapadny",
                z["name"],
                z.get("section", ""),
                basin["source"],
                z.get("pilot_region", False),
            ),
        )

    for name, (latin, aliases, is_crust) in SPECIES_MASTER.items():
        cur.execute(
            """
            INSERT INTO fish_species (name_ru, name_latin, aliases, is_crustacean)
            VALUES (%s, %s, %s, %s)
            ON CONFLICT (name_ru) DO NOTHING
            """,
            (name, latin, aliases, is_crust),
        )

    cur.execute("SELECT name_ru, id FROM fish_species")
    species_ids = dict(cur.fetchall())

    # --- Размеры, нормы, дефолт ---
    for z in data["zones"]:
        zid = z["id"]

        for key, size in z.get("min_size_cm", {}).items():
            sp = JSON_KEY_TO_SPECIES.get(norm_key(key))
            if sp:
                cur.execute(
                    "INSERT INTO zone_size_rules (zone_id, species_id, min_size_cm) VALUES (%s, %s, %s) ON CONFLICT DO NOTHING",
                    (zid, species_ids[sp], size),
                )

        for key, lim in z.get("daily_limit", {}).items():
            sp = JSON_KEY_TO_SPECIES.get(norm_key(key))
            if sp:
                cur.execute(
                    "INSERT INTO zone_limit_rules (zone_id, species_id, limit_value, unit) VALUES (%s, %s, %s, %s) ON CONFLICT DO NOTHING",
                    (zid, species_ids[sp], lim["value"], "kg" if lim["unit"] == "кг" else "шт"),
                )

        dflt = z.get("daily_limit_default", {})
        cur.execute(
            "INSERT INTO zone_default_limits (zone_id, default_kg, note) VALUES (%s, %s, %s) ON CONFLICT (zone_id) DO NOTHING",
            (zid, dflt.get("value", 10), dflt.get("note", "")),
        )

        # --- Запреты: виды ---
        for banned in z.get("banned_species", []):
            for sp in find_species(banned):
                cur.execute(
                    """
                    INSERT INTO zone_bans (zone_id, ban_type, species_id, rule_text, permanent)
                    VALUES (%s, 'species', %s, %s, true)
                    """,
                    (zid, species_ids[sp], banned),
                )

        # --- Запреты: сезоны ---
        for ban in z.get("season_bans", []):
            species_names = ban.get("species", [])
            area = ban.get("area", "")
            rule = ban["period"].get("rule", "")
            p_from = ban["period"].get("from")
            p_to = ban["period"].get("to")

            if "все виды" in [s.lower() for s in species_names]:
                cur.execute(
                    """
                    INSERT INTO zone_bans (zone_id, ban_type, species_id, period_from, period_to, period_rule, area, rule_text)
                    VALUES (%s, 'season', NULL, %s, %s, %s, %s, %s)
                    """,
                    (zid, p_from, p_to, rule, area, rule),
                )
            else:
                for sname in species_names:
                    sp = JSON_KEY_TO_SPECIES.get(norm_key(sname))
                    if sp:
                        cur.execute(
                            """
                            INSERT INTO zone_bans (zone_id, ban_type, species_id, period_from, period_to, period_rule, area, rule_text)
                            VALUES (%s, 'season', %s, %s, %s, %s, %s, %s)
                            """,
                            (zid, species_ids[sp], p_from, p_to, rule, area, rule),
                        )

        # --- Запреты: районы ---
        for area in z.get("banned_areas", []):
            cur.execute(
                "INSERT INTO zone_bans (zone_id, ban_type, area, rule_text, permanent) VALUES (%s, 'area', %s, %s, true)",
                (zid, area, area),
            )

    conn.commit()
    cur.close()
    conn.close()
    print("Seed completed")


if __name__ == "__main__":
    main()
