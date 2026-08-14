using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Klevo.Core.Data;

[Table("spots")]
public class Spot
{
    [Key, Column("id")]
    public Guid Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = "";

    [Column("description")]
    public string Description { get; set; } = "";

    [Column("location")]
    public NetTopologySuite.Geometries.Point Location { get; set; } = default!;

    [Column("water_type")]
    public string WaterType { get; set; } = "lake";

    [Column("region")]
    public string Region { get; set; } = "";

    [Column("zone_id")]
    public string? ZoneId { get; set; }

    [Column("created_by")]
    public Guid? CreatedBy { get; set; }

    [Column("is_public")]
    public bool IsPublic { get; set; } = true;

    [Column("rating")]
    public decimal Rating { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}


[Table("fishery_basins")]
public class FisheryBasin
{
    [Key, Column("id")]
    public string Id { get; set; } = "";

    [Column("name")]
    public string Name { get; set; } = "";
}

[Table("fishery_zones")]
public class FisheryZone
{
    [Key, Column("id")]
    public string Id { get; set; } = "";

    [Column("basin_id")]
    public string BasinId { get; set; } = "";

    [Column("name")]
    public string Name { get; set; } = "";

    [Column("section")]
    public string Section { get; set; } = "";

    [Column("source")]
    public string Source { get; set; } = "";

    [Column("pilot")]
    public bool Pilot { get; set; }
}

[Table("fish_species")]
public class FishSpecies
{
    [Key, Column("id")]
    public Guid Id { get; set; }

    [Column("name_ru")]
    public string NameRu { get; set; } = "";

    [Column("name_latin")]
    public string? NameLatin { get; set; }

    [Column("aliases")]
    public string[] Aliases { get; set; } = [];

    [Column("is_crustacean")]
    public bool IsCrustacean { get; set; }
}

[Table("zone_size_rules")]
public class ZoneSizeRule
{
    [Column("zone_id")]
    public string ZoneId { get; set; } = "";

    [Column("species_id")]
    public Guid SpeciesId { get; set; }

    [Column("min_size_cm")]
    public decimal MinSizeCm { get; set; }

    public FishSpecies? Species { get; set; }
}

[Table("zone_limit_rules")]
public class ZoneLimitRule
{
    [Column("zone_id")]
    public string ZoneId { get; set; } = "";

    [Column("species_id")]
    public Guid SpeciesId { get; set; }

    [Column("limit_value")]
    public decimal LimitValue { get; set; }

    [Column("unit")]
    public string Unit { get; set; } = "";

    public FishSpecies? Species { get; set; }
}

[Table("zone_default_limits")]
public class ZoneDefaultLimit
{
    [Key, Column("zone_id")]
    public string ZoneId { get; set; } = "";

    [Column("default_kg")]
    public decimal DefaultKg { get; set; }

    [Column("note")]
    public string Note { get; set; } = "";
}

[Table("zone_bans")]
public class ZoneBan
{
    [Key, Column("id")]
    public Guid Id { get; set; }

    [Column("zone_id")]
    public string ZoneId { get; set; } = "";

    [Column("ban_type")]
    public string BanType { get; set; } = "";

    [Column("species_id")]
    public Guid? SpeciesId { get; set; }

    [Column("period_from")]
    public DateOnly? PeriodFrom { get; set; }

    [Column("period_to")]
    public DateOnly? PeriodTo { get; set; }

    [Column("period_rule")]
    public string PeriodRule { get; set; } = "";

    [Column("area")]
    public string Area { get; set; } = "";

    [Column("rule_text")]
    public string RuleText { get; set; } = "";

    [Column("permanent")]
    public bool Permanent { get; set; }

    public FishSpecies? Species { get; set; }
}

[Table("weather_obs")]
public class WeatherObservation
{
    [Key, Column("id")]
    public long Id { get; set; }

    [Column("spot_id")]
    public Guid SpotId { get; set; }

    [Column("observed_at")]
    public DateTime ObservedAt { get; set; }

    [Column("temperature_2m")]
    public decimal? Temperature2m { get; set; }

    [Column("pressure_msl")]
    public decimal? PressureMsl { get; set; }

    [Column("humidity_2m")]
    public decimal? Humidity2m { get; set; }

    [Column("wind_speed_10m")]
    public decimal? WindSpeed10m { get; set; }

    [Column("wind_dir_10m")]
    public short? WindDir10m { get; set; }

    [Column("wind_gusts_10m")]
    public decimal? WindGusts10m { get; set; }

    [Column("precip")]
    public decimal? Precip { get; set; }

    [Column("cloud_cover")]
    public short? CloudCover { get; set; }

    [Column("snow_depth")]
    public decimal? SnowDepth { get; set; }

    [Column("source")]
    public string Source { get; set; } = "open-meteo";
}

[Table("solunar_daily")]
public class SolunarDay
{
    [Column("spot_id")]
    public Guid SpotId { get; set; }

    [Column("date")]
    public DateOnly Date { get; set; }

    [Column("moon_phase")]
    public decimal MoonPhase { get; set; }

    [Column("moon_illumination")]
    public decimal MoonIllumination { get; set; }

    [Column("moon_rise")]
    public DateTime? MoonRise { get; set; }

    [Column("moon_set")]
    public DateTime? MoonSet { get; set; }

    [Column("moon_transit")]
    public DateTime? MoonTransit { get; set; }

    [Column("lower_transit")]
    public DateTime? LowerTransit { get; set; }

    [Column("sun_rise")]
    public DateTime? SunRise { get; set; }

    [Column("sun_set")]
    public DateTime? SunSet { get; set; }

    [Column("dawn")]
    public DateTime? Dawn { get; set; }

    [Column("dusk")]
    public DateTime? Dusk { get; set; }

    [Column("major_start")]
    public DateTime? MajorStart { get; set; }

    [Column("major_end")]
    public DateTime? MajorEnd { get; set; }

    [Column("major2_start")]
    public DateTime? Major2Start { get; set; }

    [Column("major2_end")]
    public DateTime? Major2End { get; set; }

    [Column("minor_start")]
    public DateTime? MinorStart { get; set; }

    [Column("minor_end")]
    public DateTime? MinorEnd { get; set; }

    [Column("minor2_start")]
    public DateTime? Minor2Start { get; set; }

    [Column("minor2_end")]
    public DateTime? Minor2End { get; set; }
}

