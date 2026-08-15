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
- Биогеохимия (Chl-a): `BALTICSEA_MULTIYEAR_BGC_003_012` (реанализ, месячные средние).
  Датасет `cmems_mod_bal_bgc_my_P1M-m`.

> Текущие ID в каталоге CLI (проверено `copernicusmarine describe -c`):
> физика — dataset `cmems_mod_bal_phy_my_P1D-m`, BGC — `cmems_mod_bal_bgc_my_P1M-m`.

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
- CMEMS: `copernicusmarine subset` → netCDF (bbox вокруг точки) → `xarray` →
  поиск ближайшей валидной ячейки в радиусе до 15 км (прибрежные точки лежат
  на сухопутной маске модели) → upsert в `satellite_obs` (`ON CONFLICT`).
- Скрипт: `backend/ingest/python/satellite/fetch_cmems.py`.
  ```bash
  python fetch_cmems.py --from 2026-05-01 --to 2026-05-31 [--dry-run]
  python fetch_cmems.py --bgc --from 2023-01-01 --to 2026-05-31   # Chl-a
  ```
- Нюанс BGC: в распреснённой Невской губе модель ERSEM даёт ~0 chl — значения
  < 0.05 мг/м³ отбрасываются (NULL), для Лахты с `--max-km 30` реальные
  значения есть только в ~24 месяцах из 41. NASA CHL озёра маскирует —
  хлорофилл для Ладоги через стандартные продукты недоступен (позже Sentinel-2).
- Схема (реализована, чуть шире предложенной):
  ```sql
  CREATE TABLE satellite_obs (
      id          bigserial PRIMARY KEY,
      spot_id     uuid NOT NULL REFERENCES spots(id) ON DELETE CASCADE,
      observed_at date NOT NULL,
      sst_c       numeric(5,2),
      bottom_t_c  numeric(5,2),
      mlotst_m    numeric(6,1),
      salinity_psu numeric(6,2),
      chla_mgm3   numeric(6,4),
      source      text NOT NULL,
      UNIQUE (spot_id, observed_at, source)
  );
  ```
- `fetch_nasa.py` (MODIS Aqua L3SMI SST для Ладоги) — скачивание с `--mirror podaac`
  (по умолчанию) или `--mirror ocean` (getfile OB.DAAC); фильтр `qual_sst<=1`,
  ближайшая валидная ячейка в радиусе ≤15 км, источник `nasa_modis_aqua`.
- PODAAC-зеркало: `archive.podaac.earthdata.nasa.gov` (коллекция
  `MODIS_AQUA_L3_SST_THERMAL_DAILY_4KM_DAYTIME_V2019.0`, CloudFront по EDL-токену);
  нужно, т.к. `oceandata.sci.gsfc.nasa.gov` перестал отвечать (TCP проходит, TLS/HTTP виснет).

## Доступ к данным (проверено)

### BALTICSEA_MULTIYEAR_PHY_003_011 (физика, SST/температура)
- Продукт: **Baltic Sea Physics Reanalysis** (история 1993 → ~31.05.2026, сутки)
- Датасет суточных средних: `cmems_mod_bal_phy_my_P1D-m_202303`
- STAC (публичный): `https://stac.marine.copernicus.eu/metadata/BALTICSEA_MULTIYEAR_PHY_003_011/cmems_mod_bal_phy_my_P1D-m_202303/dataset.stac.json`
- Данные:
  - Native NetCDF: `https://s3.waw3-1.cloudferro.com/mdl-native-11/native/BALTICSEA_MULTIYEAR_PHY_003_011/cmems_mod_bal_phy_my_P1D-m_202303`
  - ARCO Zarr (рекомендуется): `.../mdl-arco-time-002/arco/BALTICSEA_MULTIYEAR_PHY_003_011/cmems_mod_bal_phy_my_P1D-m_202303/timeChunked.zarr`
- Сетка: 774×763 точек, шаг 1/60° (≈2 км); 56 уровней глубины (−712…−0.5 м)
- Переменные: `thetao` (температура по глубинам), `bottomT` (температура у дна),
  `so`/`sob` (солёность), `mlotst` (глубина перемешивания), `sla`, `siconc`, `uo`/`vo` (течения)
- Метаданные продукта (ISO 19115): `C:\Users\Артём\Downloads\BALTICSEA_MULTIYEAR_PHY_003_011.xml`
- Для скачивания нужен CMEMS токен (логин в `copernicusmarine`)

## Статус
- [x] Open-Meteo (погода, история + прогноз) — в `Klevo.Ingest`
- [x] Солунар (Astronomy Engine) — в `Klevo.Ingest`
- [x] Бэкфилл погоды и солунара 2023-01-01 … 2026-08-20 (все 5 точек): погода 30096 ч/точка (0 пропусков), солунар 1254 дня/точка
- [x] Регистрация NASA Earthdata (токен в `.env`, авторизация работает)
- [x] Регистрация CMEMS (email подтверждён)
- [x] `copernicusmarine login` + актуальные dataset ID найдены через `describe -c`
- [x] Таблица `satellite_obs` в `schema.sql` и сущность в `Klevo.Core` (DbSet + индекс)
- [x] `fetch_cmems.py`: выгрузка + upsert в `satellite_obs` (проверено, идемпотентно)
- [x] Бэкфилл CMEMS 2023-01-01 … 2026-05-31: 2494 записи (Лахта, Кургальский)
- [x] Спутниковые данные в `/api/spots/{id}/conditions` (поле `satellite`)
- [x] `fetch_nasa.py` (MODIS Aqua L3SMI SST) — Ладога покрыта, 2513 записей
- [x] Дозалив NASA за лето 2026 (2026-05-27…08-13) через PODAAC-зеркало — `sst_c` свежий
      (до 08-13; 08-14/15 ещё не выложены, 08-10 нет гранулы)
- [x] CMEMS Chl-a (`cmems_mod_bal_bgc_my_P1M-m`, `--bgc`) — 65 записей (Лахта, Кургальский)

## Учётные записи — чек-лист

### 1. Copernicus Marine (CMEMS) — для Финского залива (~15 мин)
1. Регистрация: https://data.marine.copernicus.eu/register
   (или marine.copernicus.eu → "Create account").
   Поля: email, имя/фамилия, организация (можно "Other"), страна,
   тип пользователя (для стартапа — "Commercial" или "Other").
   Принять условия Copernicus Marine Free and Open Access Policy.
2. Подтвердить email по ссылке из письма.
3. Подписаться на продукты (кнопка "Subscribe" на странице продукта):
   - `BALTICSEA_MULTIYEAR_PHY_003_011` — физика, SST (история)
   - `BALTICSEA_MULTIYEAR_BGC_003_012` — биогеохимия, Chl-a (история)
4. В профиле сгенерировать **API-токен** (Copernicus Marine token)
   или взять логин/пароль.
5. Отдать мне токен (или логин/пароль) → кладу в `backend/ingest/python/satellite/.env`.

### 2. NASA Earthdata — для Ладоги (MODIS/VIIRS) (~15 мин)
1. Регистрация: https://urs.earthdata.nasa.gov/users/new
   Требования:
   - username: 4–30 симв., только строчные латиница/цифры/`.`/`_`, без пробелов
   - пароль: минимум 12 симв., заглавная + строчная + цифра + спецсимвол
   - имя/фамилия, email, страна (Russian Federation — есть в списке),
     Affiliation = Other/Commercial, User Type = Other/Application
2. Сгенерировать **Personal Access Token**:
   https://urs.earthdata.nasa.gov/users/<username> → "Generate Token" → копировать.
3. Отдать мне токен → `.env` (ключ `EARTHDATA_TOKEN`).

### Что сделано после получения учёток
- `backend/ingest/python/satellite/.env` (gitignored) — CMEMS_USERNAME/PASSWORD, EARTHDATA_USER/TOKEN
- `fetch_cmems.py` — CMEMS реанализ → `satellite_obs` (работает)
- `satellite_obs` в схеме, сущность `SatelliteObservation` в `Klevo.Core`

> Если регистрации не хочется: для SST-прогноза можно начать с Open-Meteo Marine
> API (без ключа), но это только прогноз на море и без Chl-a — историю не даёт.

