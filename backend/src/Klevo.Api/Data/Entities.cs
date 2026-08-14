using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Klevo.Api.Data;

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
