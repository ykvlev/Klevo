-- Klevo: начальная схема БД (PostgreSQL 17 + PostGIS 3.6)
-- Применение: psql -U postgres -d klevo -f schema.sql

CREATE EXTENSION IF NOT EXISTS postgis;
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

-- ============================================================
-- Юзеры и подписки
-- ============================================================

CREATE TABLE IF NOT EXISTS users (
    id            uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    email         text UNIQUE,
    phone         text UNIQUE,
    password_hash text NOT NULL,
    display_name  text NOT NULL DEFAULT '',
    avatar_url    text,
    role          text NOT NULL DEFAULT 'user' CHECK (role IN ('user', 'admin')),
    created_at    timestamptz NOT NULL DEFAULT now(),
    updated_at    timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS subscriptions (
    id            uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id       uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    plan          text NOT NULL DEFAULT 'free' CHECK (plan IN ('free', 'pro')),
    status        text NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'canceled', 'expired')),
    provider      text NOT NULL DEFAULT 'internal',
    external_id   text,
    started_at    timestamptz NOT NULL DEFAULT now(),
    expires_at    timestamptz,
    created_at    timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_subs_user ON subscriptions(user_id);

-- ============================================================
-- Точки рыбалки (гео)
-- ============================================================

CREATE TABLE IF NOT EXISTS spots (
    id             uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    name           text NOT NULL,
    description    text NOT NULL DEFAULT '',
    location       geography(Point, 4326) NOT NULL,
    water_type     text NOT NULL DEFAULT 'lake' CHECK (water_type IN ('river', 'lake', 'reservoir', 'sea', 'pond')),
    region         text NOT NULL DEFAULT '',
    zone_id        text REFERENCES fishery_zones(id),
    created_by     uuid REFERENCES users(id) ON DELETE SET NULL,
    is_public      boolean NOT NULL DEFAULT true,
    rating         numeric(3,2) NOT NULL DEFAULT 0,
    created_at     timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_spots_geo ON spots USING GIST (location);
CREATE INDEX IF NOT EXISTS idx_spots_zone ON spots(zone_id);

-- ============================================================
-- Уловы (исторические данные для обучения модели)
-- ============================================================

CREATE TABLE IF NOT EXISTS catches (
    id            uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id       uuid REFERENCES users(id) ON DELETE SET NULL,
    spot_id       uuid REFERENCES spots(id) ON DELETE SET NULL,
    species_id    uuid REFERENCES fish_species(id) ON DELETE SET NULL,
    species_name  text NOT NULL,
    weight_kg     numeric(6,2),
    length_cm     numeric(6,2),
    photo_url     text,
    caught_at     timestamptz NOT NULL DEFAULT now(),
    weather       jsonb,
    notes         text,
    created_at    timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_catches_spot_time ON catches(spot_id, caught_at);
CREATE INDEX IF NOT EXISTS idx_catches_species ON catches(species_id);

-- ============================================================
-- Прогнозы (выход ML-модели)
-- ============================================================

CREATE TABLE IF NOT EXISTS predictions (
    id         uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    spot_id    uuid NOT NULL REFERENCES spots(id) ON DELETE CASCADE,
    date       date NOT NULL,
    score      smallint NOT NULL CHECK (score BETWEEN 0 AND 100),
    best_start time,
    best_end   time,
    model_version text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (spot_id, date)
);

CREATE INDEX IF NOT EXISTS idx_predictions_spot_date ON predictions(spot_id, date DESC);

-- ============================================================
-- Справочник правил рыболовства
-- ============================================================

CREATE TABLE IF NOT EXISTS fishery_basins (
    id   text PRIMARY KEY,
    name text NOT NULL
);

CREATE TABLE IF NOT EXISTS fishery_zones (
    id        text PRIMARY KEY,
    basin_id  text NOT NULL REFERENCES fishery_basins(id),
    name      text NOT NULL,
    section   text NOT NULL DEFAULT '',
    source    text NOT NULL DEFAULT '',
    pilot     boolean NOT NULL DEFAULT false
);

CREATE TABLE IF NOT EXISTS fish_species (
    id          uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    name_ru     text NOT NULL UNIQUE,
    name_latin  text,
    aliases     text[] NOT NULL DEFAULT '{}',
    is_crustacean boolean NOT NULL DEFAULT false
);

CREATE TABLE IF NOT EXISTS zone_size_rules (
    zone_id     text NOT NULL REFERENCES fishery_zones(id) ON DELETE CASCADE,
    species_id  uuid NOT NULL REFERENCES fish_species(id) ON DELETE CASCADE,
    min_size_cm numeric(6,1) NOT NULL,
    PRIMARY KEY (zone_id, species_id)
);

CREATE TABLE IF NOT EXISTS zone_limit_rules (
    zone_id     text NOT NULL REFERENCES fishery_zones(id) ON DELETE CASCADE,
    species_id  uuid NOT NULL REFERENCES fish_species(id) ON DELETE CASCADE,
    limit_value numeric(8,2) NOT NULL,
    unit        text NOT NULL CHECK (unit IN ('kg', 'шт')),
    PRIMARY KEY (zone_id, species_id)
);

CREATE TABLE IF NOT EXISTS zone_default_limits (
    zone_id     text PRIMARY KEY REFERENCES fishery_zones(id) ON DELETE CASCADE,
    default_kg  numeric(8,2) NOT NULL DEFAULT 10,
    note        text NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS zone_bans (
    id          uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    zone_id     text NOT NULL REFERENCES fishery_zones(id) ON DELETE CASCADE,
    ban_type    text NOT NULL CHECK (ban_type IN ('species', 'season', 'area', 'gear')),
    species_id  uuid REFERENCES fish_species(id) ON DELETE CASCADE,
    period_from date,
    period_to   date,
    period_rule text NOT NULL DEFAULT '',
    area        text NOT NULL DEFAULT '',
    rule_text   text NOT NULL DEFAULT '',
    permanent   boolean NOT NULL DEFAULT false
);

CREATE INDEX IF NOT EXISTS idx_zone_bans_zone ON zone_bans(zone_id);
CREATE INDEX IF NOT EXISTS idx_zone_bans_species ON zone_bans(species_id);
