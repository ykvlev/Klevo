-- Пилотные точки рыбалки (Ленинградская область) для Фазы 2
-- Идемпотентно: ON CONFLICT (id) DO UPDATE.
-- Зоны: baltic_32, ladoga, lenobl_vodnye_obekty

INSERT INTO spots (id, name, description, location, water_type, region, zone_id, is_public) VALUES
    ('a1111111-0000-4000-8000-000000000001', 'Лосево, Лосевская протока (Вуокса)',
     'Спортивная рыбалка на течении: щука, судак, лещ', ST_SetSRID(ST_MakePoint(30.2296, 60.6749), 4326)::geography,
     'river', 'Ленинградская область, Приозерский район', 'lenobl_vodnye_obekty', true),
    ('a1111111-0000-4000-8000-000000000002', 'Приозерск, устье Вуоксы (Ладога)',
     'Вылет судака и сига, глубокие свалы', ST_SetSRID(ST_MakePoint(30.1290, 61.0360), 4326)::geography,
     'river', 'Ленинградская область, Приозерский район', 'ladoga', true),
    ('a1111111-0000-4000-8000-000000000003', 'Ладога, бухта Моторная',
     'Летний лов окуня и леща, прогреваемые прибрежные бровки', ST_SetSRID(ST_MakePoint(30.2900, 60.9440), 4326)::geography,
     'lake', 'Ленинградская область, Приозерский район', 'ladoga', true),
    ('a1111111-0000-4000-8000-000000000004', 'Финский залив, Лахта (СПб)',
     'Джиг и троллинг на судака, щуку в прибрежной зоне', ST_SetSRID(ST_MakePoint(30.1500, 59.9900), 4326)::geography,
     'sea', 'Санкт-Петербург, Приморский район', 'baltic_32', true),
    ('a1111111-0000-4000-8000-000000000005', 'Финский залив, Кургальский полуостров',
     'Береговой спиннинг, горбуша и судак на выходах из глубин', ST_SetSRID(ST_MakePoint(28.1800, 59.6400), 4326)::geography,
     'sea', 'Ленинградская область, Кингисеппский район', 'baltic_32', true)
ON CONFLICT (id) DO UPDATE SET
    name = EXCLUDED.name,
    description = EXCLUDED.description,
    location = EXCLUDED.location,
    water_type = EXCLUDED.water_type,
    region = EXCLUDED.region,
    zone_id = EXCLUDED.zone_id,
    is_public = EXCLUDED.is_public;
