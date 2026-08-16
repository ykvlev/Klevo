# Фаза 4. Fish ID (определение вида по фото)

ML-классификатор: MobileNetV3-Small (ImageNet-веса), fine-tune на 10 классов
видов, фигурирующих в правилах вылова (Ленобласть / Финский залив / Ладога).

Классы (индекс = порядок в `classes.txt`):

| Класс (папка)             | Русское название          | name_latin            |
|---------------------------|---------------------------|-----------------------|
| `Sander_lucioperca`       | судак                     | Sander lucioperca     |
| `Esox_lucius`             | щука                      | Esox lucius           |
| `Abramis_brama`           | лещ                       | Abramis brama         |
| `Osmerus_eperlanus`       | корюшка                   | Osmerus eperlanus     |
| `Salmo_trutta`            | кумжа                     | Salmo trutta          |
| `Salmo_salar`             | лосось атлантический      | Salmo salar           |
| `Coregonus_lavaretus`     | сиг                       | Coregonus lavaretus   |
| `Thymallus_thymallus`     | хариус                    | Thymallus thymallus   |
| `Acipenser_sturio`        | осетр атлантический       | Acipenser sturio      |
| `Astacus_astacus`         | раки (рак благородный)    | Astacus astacus       |

Подвиды (`лосось озерный` → `Salmo salar morpha sebago`, `озерная форель` →
`Salmo trutta morpha lacustris`, `сиг волховский` → `Coregonus lavaretus baeri`)
маппятся на базовый класс по префиксу `name_latin` в `FishIdService`.

## Датасет

- Скрипт: `backend/ingest/python/fishid/download_wikimedia.py`
  (Wikimedia Commons API: категории вида, thumbs 640px, JPEG 88).
- Структура: `data/fishid/raw/<latin>/*.jpg` (+ `manifest.json`).
- Исход: 1005 изображений на 10 видов (мин. 43/вид — хариус, макс. 150/вид).
- Каталог `data/fishid/` в `.gitignore` — в репозиторий не попадает.
- Rate limiting Wikimedia: скрипт ретраит 429 с backoff и резюмируется
  (пропускает уже скачанные файлы).

## Обучение

    python backend/ingest/python/fishid/train.py --data data/fishid/raw --epochs 14 --out data/fishid/runs/run1

- Fine-tune MobileNetV3-Small (`--arch mobilenet_v3_large` для точнее, но медленнее),
  `--lr 3e-4`, AdamW, cosine; split 80/10/10 стратифицированный; ToTensor+Normalize.
- Метрики в `data/fishid/runs/run1/train_summary.json` (test_top1/test_top3).
- CPU-обучение: ~60-100 с/эпоху на 1005 изображениях.

Результат пилотной модели: test_top1 ≈ 0.58-0.65, test_top3 ≈ 0.75-0.85
(зависит от рана). Точность ограничена качеством Wikimedia-фото (фон,
освещение, есть рисунки/скелеты). Для боевого качества нужен свой датасет
«рыба на руках рыболова».

## Экспорт ONNX

    python backend/ingest/python/fishid/onnx_export.py --weights data/fishid/runs/run1/best.pt --labels data/fishid/runs/run1/classes.json

- Вход графа: `float [1,224,224,3]`, пиксели 0..255, RGB, NHWC.
- Нормализация (ImageNet) и permute вшиты в граф (`PreprocBackbone`).
- Выход: `logits [1,10]` (softmax делает C#).
- Артефакты: `backend/src/Klevo.Api/wwwroot/models/fishid/model.onnx` + `classes.txt`
  (`index|name_latin|name_ru`). ONNX в `.gitignore` — модель локальная.

## API

- `POST /api/uploads` — multipart `file` (JPG/PNG/WebP, ≤10 МБ) → сохраняет в
  `wwwroot/uploads/<yyyy-MM-dd>/`, возвращает `{ url }` (статикой отдаётся из wwwroot).
- `POST /api/fish-id` — multipart `file` или JSON `{ dataUrl }` → топ-3 вида:
  `{ modelVersion, top: [{ speciesId, nameRu, nameLatin, confidence }] }`.
  `speciesId` маппится по `fish_species.name_latin` (точное совпадение или префикс).
  Если модель не развёрнута — `503`.

## Предобработка (C#)

Повторяет torchvision eval-пайплайн: resize короткой стороны до 256 (с
сохранением пропорций) → центральный кроп 224×224 → нормализация в графе ONNX.

## Переобучение

1. Пополнить/заменить `data/fishid/raw/<latin>/`.
2. `train.py` → выбрать `best.pt` по val_top1.
3. `onnx_export.py` → новый `model.onnx`.
4. Перезапуск API подхватит модель при старте (загрузка в `FishIdService`).
