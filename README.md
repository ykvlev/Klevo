# Klevo

AI-прогноз клёва + инспектор улова (fish ID + правила рыболовства).

**Пилотный регион:** Ленинградская область (Западный рыбохозяйственный бассейн).

## Стек
- Мобилка: Flutter
- Бэкенд: C# / ASP.NET Core
- ML-обучение: Python (LightGBM, PyTorch), запуск через ONNX Runtime (C#)
- БД: PostgreSQL + PostGIS
- Хранилище фото: S3 (MinIO / Yandex Cloud)

## Структура
```
backend/    ASP.NET Core API + worker-пайплайн данных
data/       правила рыболовства, датасеты, фичи
docs/       планы, исследования
```

## Фазы
- [x] Фаза 0 — фундамент (регион, правила, окружение, репо)
- [x] Фаза 1 (БД) — PostgreSQL 17 + PostGIS, схема, сид правил
- [x] Фаза 1 (API) — ASP.NET Core 10, EF Core + Npgsql, эндпоинты правил
- [x] Фаза 2 (база) — пайплайн: погода (Open-Meteo) + солунар (Astronomy Engine), пилотные точки
- [x] Фаза 2 (спутник, CMEMS) — SST/температура/солёность по морским точкам
  (см. `docs/phase2/satellite-sources.md`); NASA (Ладога) — следующий шаг
- [x] Фаза 3 (rule-v1) — прогноз клёва: `ml/features.py` (матрица признаков), `ml/score.py`
  (правило-скор 0–100 → `predictions`), `ml/train.py` (LightGBM→ONNX, ждёт метки из `catches`),
  `GET /api/spots/{id}/forecast`; ML-модель — следующий шаг
- [ ] Фаза 4 — fish ID + правила
- [ ] Фаза 5 — мобильное приложение (Flutter)
- [ ] Фаза 6 — запуск

## Окружение
- git 2.55, dotnet 10, node 24, python 3.14 (ML), PostgreSQL 17 + PostGIS 3.6 (нативно)

## БД
- Локально: сервис `postgresql-x64-17`, порт 5432, БД `klevo` / `klevo_test`
- Dev-пароль postgres: `klevo_dev_pwd`
- Схема: `backend/db/schema.sql`; сид правил: `backend/db/seed_rules.py`; сид точек: `backend/db/seed_spots.sql`

## API
- `backend/src/Klevo.Api` — минимальное Web API (EF Core 10 + Npgsql 10 + PostGIS/NetTopologySuite)
- Запуск: `dotnet run --project backend/src/Klevo.Api` → http://localhost:5178
- Эндпоинты: `GET /health`, `GET /api/zones`, `GET /api/zones/{id}/rules`,
  `GET /api/spots`, `GET /api/spots/{id}/conditions?date=YYYY-MM-DD`,
  `GET /api/spots/{id}/forecast?date=YYYY-MM-DD`
- Тесты: `dotnet test backend/Klevo.slnx` (интеграционные + астро, нужен локальный PG)

## Пайплайн данных (`backend/src/Klevo.Ingest`)
- `solunar --days N | --from D --to T` — фаза луны, освещённость, восход/заход,
  кульминации, солунарные окна (major/minor) для всех точек
- `weather --days N | --from D --to T` — часовые наблюдения Open-Meteo
  (прогноз или архив) для всех точек
- `all` — погода + солунар за один запуск

## Спутниковые данные (`backend/ingest/python/satellite`)
- `fetch_cmems.py --from YYYY-MM-DD --to YYYY-MM-DD` — CMEMS Baltic Sea Reanalysis
  (SST, придонная T, глубина перемешивания, солёность) → `satellite_obs`
- venv: `backend/ingest/python/satellite/.venv`; учётка в `.env` (gitignored)
