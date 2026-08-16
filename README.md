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
  (см. `docs/phase2/satellite-sources.md`); NASA (Ладога) — MODIS Aqua SST через
  PODAAC-зеркало (`fetch_nasa.py --mirror podaac`), свежесть до 2026-08-13
- [x] Фаза 3 (rule-v1) — прогноз клёва: `ml/features.py` (матрица признаков), `ml/score.py`
  (правило-скор 0–100 → `predictions`), `ml/train.py` (LightGBM→ONNX, ждёт метки из `catches`),
  `GET /api/spots/{id}/forecast`; ML-модель — следующий шаг
- [x] Фаза 4 — fish ID + правила: инспектор правил (виды/запреты/сезоны, алиасы, минимальные
  размеры и нормы) + классификатор вида по фото (`MobileNetV3-Small`, 10 пилотных видов,
  top-1 0.74 / top-3 0.90 на тесте; см. `docs/phase4/fish-id.md`); загрузка фото, top-3 кандидатов,
  привязка фото к записи улова
- [x] Фаза 4.5 — продакшн-веб (частично): карта точек (`/map.html`, Leaflet vendored, тёмные тайлы CARTO,
  попап с прогнозом, панель правил, список для мобильных), навигация ЖУРНАЛ/КАРТА/ЛЕНТА,
  лента уловов с фото и вердиктом правил (`/feed.html`, `GET /api/catches/feed`); мобильная адаптация — дальше
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
  `GET /api/spots`, `GET /api/species`, `GET /api/spots/{id}/conditions?date=YYYY-MM-DD`,
  `GET /api/spots/{id}/forecast?date=YYYY-MM-DD`,
  `GET /api/spots/{id}/catches?from=&to=`, `POST /api/spots/{id}/catches`
  (улов = метка для ML), `GET /api/catches/feed?limit=N` (лента уловов всех точек
  со спотом, фото и вердиктом правил), `POST /api/uploads` (multipart JPG/PNG/WebP ≤10 МБ → `wwwroot/uploads/`),
  `POST /api/fish-id` (multipart `file` или JSON `{dataUrl}` → top-3 вида с уверенностью;
  503, если модель не развёрнута)
- Веб-страницы: `/` (журнал уловов: прогноз + правила + фото-определитель),
  `/map.html` (карта точек с прогнозом и правилами), `/feed.html` (лента уловов с фото)
- ML-прогноз в `/forecast`: C# строит 24 признака (`MlFeatureBuilder`, паритет с `features.py`)
  и считает скор через ONNX Runtime (`MlModelRunner`, `model.onnx`, версия `ml-v1`);
  при отсутствии модели/данных — фолбэк на правило-базовые строки `predictions` (`rule-v1`).
  Путь к модели: `ML:ModelPath` в конфиге или `wwwroot/models/model.onnx`
- Тесты: `dotnet test backend/Klevo.slnx` (интеграционные + астро, нужен локальный PG)

## Fish ID (`backend/ingest/python/fishid`, venv `.venv`)
- `download_wikimedia.py` — сбор датасета с Wikimedia Commons (640px thumbs, ретраи 429)
- `train.py` — fine-tune `MobileNetV3-Small` (`--arch mobilenet_v3_large`), split 80/10/10, top-1/top-3
- `onnx_export.py` — экспорт ONNX с предобработкой в графе (вход NHWC 0–255, `--order` = порядок классов обучения)
- Модель `wwwroot/models/fishid/{model.onnx, model.onnx.data, classes.txt}` — локальная (gitignored);
  C#-препроцессинг повторяет torchvision eval (Resize 256 + CenterCrop 224)

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

## ML (`backend/ingest/python/ml`, venv `.venv`, Python 3.14)
- `features.py` — ежедневная матрица признаков (погода + солунар + спутник + lag)
- `score.py` — правило-базовый скор 0–100 → `predictions` (model_version `rule-v1`)
- `train.py --features X.csv --labels Y.csv` — LightGBM → ONNX (паритет 1e-5);
  без меток завершается корректно; реальные метки = уловы из `catches`
- `onnx_export.py` — свой экспортёр TreeEnsemble→ONNX (skl2onnx/hummingbird
  не имеют wheels для Python 3.14)
- `make_demo_labels.py` — демо-метки для проверки пайплайна (не для продакшена)
- `parity_check.py [spot_id] [date]` — сверка скоров C# vs Python (ONNX Runtime, тот же вектор)
