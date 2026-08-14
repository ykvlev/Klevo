# Фаза 2 — источники спутниковых данных (SST / Chl-a)

Дополнение к воркеру `Klevo.Ingest`: погода (Open-Meteo) и солунар (Astronomy Engine)
уже в БД. Ниже — план и источники для спутниковых данных на пилотный регион.

## Что дают спутники для прогноза клёва
- **SST** (температура поверхности воды) — термические фронты, преднерестовые и
  посленерестовые скопления хищника.
- **Chl-a** (концентрация хлорофилла) — прокси фитопланктона → кормовая база
  (зоопланктон → мелочь → судак/щука/лещ).

## Источники

### Финский залив (морская часть) — Copernicus Marine (CMEMS)
Продукты (сетка ~2–4 км, ежедневно):
- Физика (SST): `BALTICSEA_MULTIYEAR_PHY_003_011` (реанализ, история)
  и `BALTICSEA_ANALYSISFORECAST_PHY_003_006` / NRT `BALTICSEA_NRT_PHY_003_013` (текущее + прогноз).
- Биогеохимия (Chl-a): `BALTICSEA_MULTIYEAR_BGC_003_011` (реанализ).

Доступ: бесплатная регистрация на marine.copernicus.eu, подписка на продукт,
учётка в `.env` (CMEMS_USERNAME / CMEMS_PASSWORD). Скачивание через
`copernicusmarine` CLI (Python) или motuclient.

### Ладожское озеро (озёрная часть) — NASA Ocean Color (MODISA / VIIRS)
CMEMS озёра не покрывает. Для Ладоги нужны L3 продукты NASA:
- `MODIS Aqua L3SMI` (SST + Rrs/Chl-a), `VIIRS SNPP/JPSS1`.
- Доступ: регистрация NASA Earthdata Login, токен в `.env` (EARTHDATA_TOKEN).

### Береговые наблюдения
Можно дополнительно агрегировать in-situ: GLOBOOS/ГГИ, но для MVP — не критично.

## Формат и пайплайн
- Файлы netCDF → Python (`xarray`, `netCDF4`, `numpy`, `shapely`) →
  интерполяция (Nearest/Distance) SST и Chl-a к координатам пилотных точек →
  запись в `weather_obs`-подобную таблицу спутниковых наблюдений.
- Предложение схемы:
  ```sql
  CREATE TABLE satellite_obs (
      id          bigserial PRIMARY KEY,
      spot_id     uuid NOT NULL REFERENCES spots(id) ON DELETE CASCADE,
      observed_at date NOT NULL,
      sst_c       numeric(5,2),
      chla_mgm3   numeric(6,4),
      source      text NOT NULL,
      UNIQUE (spot_id, observed_at, source)
  );
  ```
- Скрипты: `backend/ingest/python/satellite/fetch_cmems.py`, `fetch_nasa.py`
  (будут реализованы после получения учёток).

## Статус
- [x] Open-Meteo (погода, история + прогноз) — в `Klevo.Ingest`
- [x] Солунар (Astronomy Engine) — в `Klevo.Ingest`
- [ ] Регистрация на CMEMS + NASA Earthdata (нужны учётки пользователя)
- [ ] `fetch_cmems.py` / `fetch_nasa.py` → `satellite_obs`
- [ ] Таблица `satellite_obs` в `schema.sql` и сущность в `Klevo.Core`
