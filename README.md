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
- [ ] Фаза 2 — пайплайн данных (спутник, метео, луна)
- [ ] Фаза 3 — модель клёва
- [ ] Фаза 4 — fish ID + правила
- [ ] Фаза 5 — мобильное приложение (Flutter)
- [ ] Фаза 6 — запуск

## Окружение
- git 2.55, dotnet 10, node 24, python 3.14 (ML), PostgreSQL 17 + PostGIS 3.6 (нативно)

## БД
- Локально: сервис `postgresql-x64-17`, порт 5432, БД `klevo` / `klevo_test`
- Dev-пароль postgres: `klevo_dev_pwd`
- Схема: `backend/db/schema.sql`; сид правил: `backend/db/seed_rules.py`

## API
- `backend/src/Klevo.Api` — минимальное Web API (EF Core 10 + Npgsql 10 + PostGIS/NetTopologySuite)
- Запуск: `dotnet run --project backend/src/Klevo.Api` → http://localhost:5178
- Эндпоинты: `GET /health`, `GET /api/zones`, `GET /api/zones/{id}/rules`
- Тесты: `dotnet test backend/Klevo.slnx` (интеграционные, нужен локальный PG)
