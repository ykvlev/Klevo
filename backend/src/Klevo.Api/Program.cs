using Klevo.Core.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Host=localhost;Port=5432;Database=klevo;Username=postgres;Password=klevo_dev_pwd";

builder.Services.AddDbContext<KlevoDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite()));

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/api/zones", async (KlevoDbContext db) =>
    await db.Zones
        .OrderBy(z => z.Id)
        .Select(z => new { id = z.Id, name = z.Name, section = z.Section, pilot = z.Pilot })
        .ToListAsync());

app.MapGet("/api/zones/{id}/rules", async (string id, KlevoDbContext db) =>
{
    var zone = await db.Zones.FindAsync(id);
    if (zone is null)
        return Results.NotFound();

    var sizeRules = await db.SizeRules
        .Where(r => r.ZoneId == id)
        .Include(r => r.Species)
        .Select(r => new
        {
            species = r.Species!.NameRu,
            minSizeCm = r.MinSizeCm,
        })
        .ToListAsync();

    var limitRules = await db.LimitRules
        .Where(r => r.ZoneId == id)
        .Include(r => r.Species)
        .Select(r => new
        {
            species = r.Species!.NameRu,
            value = r.LimitValue,
            unit = r.Unit,
        })
        .ToListAsync();

    var defaultLimit = await db.DefaultLimits.FindAsync(id);

    var bans = await db.Bans
        .Where(b => b.ZoneId == id)
        .Include(b => b.Species)
        .OrderBy(b => b.BanType)
        .Select(b => new
        {
            type = b.BanType,
            species = b.Species != null ? b.Species.NameRu : null,
            periodFrom = b.PeriodFrom,
            periodTo = b.PeriodTo,
            periodRule = b.PeriodRule,
            area = b.Area,
            ruleText = b.RuleText,
            permanent = b.Permanent,
        })
        .ToListAsync();

    return Results.Ok(new
    {
        zone = new { id = zone.Id, name = zone.Name, section = zone.Section },
        minSizes = sizeRules,
        dailyLimits = limitRules,
        defaultDailyLimitKg = defaultLimit?.DefaultKg,
        defaultLimitNote = defaultLimit?.Note,
        bans,
    });
});

app.MapGet("/api/spots", async (KlevoDbContext db) =>
    await db.Spots
        .OrderBy(s => s.Name)
        .Select(s => new
        {
            id = s.Id,
            name = s.Name,
            waterType = s.WaterType,
            region = s.Region,
            zoneId = s.ZoneId,
            lat = s.Location.Y,
            lon = s.Location.X,
        })
        .ToListAsync());

app.MapGet("/api/spots/{id}/conditions", async (Guid id, DateOnly date, KlevoDbContext db) =>
{
    var spot = await db.Spots.FindAsync(id);
    if (spot is null)
        return Results.NotFound();

    var solunar = await db.SolunarDays
        .Where(d => d.SpotId == id && d.Date == date)
        .SingleOrDefaultAsync();

    var weather = await db.WeatherObservations
        .Where(o => o.SpotId == id && o.ObservedAt >= date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
                                      && o.ObservedAt < date.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc))
        .OrderBy(o => o.ObservedAt)
        .Select(o => new
        {
            time = o.ObservedAt,
            temperature = o.Temperature2m,
            pressure = o.PressureMsl,
            humidity = o.Humidity2m,
            windSpeed = o.WindSpeed10m,
            windDir = o.WindDir10m,
            windGusts = o.WindGusts10m,
            precip = o.Precip,
            cloudCover = o.CloudCover,
            snowDepth = o.SnowDepth,
        })
        .ToListAsync();

    var satellite = await db.SatelliteObservations
        .Where(o => o.SpotId == id && o.ObservedAt == date)
        .OrderBy(o => o.Source)
        .Select(o => new
        {
            source = o.Source,
            sstC = o.SstC,
            bottomTC = o.BottomTC,
            mlotstM = o.MlotstM,
            salinityPsu = o.SalinityPsu,
            chlaMgm3 = o.ChlaMgm3,
        })
        .ToListAsync();

    if (solunar is null && weather.Count == 0 && satellite.Count == 0)
        return Results.NotFound();

    return Results.Ok(new
    {
        spotId = id,
        date,
        solunar = solunar is null ? null : new
        {
            moonPhase = solunar.MoonPhase,
            moonIllumination = solunar.MoonIllumination,
            moonRise = solunar.MoonRise,
            moonSet = solunar.MoonSet,
            moonTransit = solunar.MoonTransit,
            lowerTransit = solunar.LowerTransit,
            sunRise = solunar.SunRise,
            sunSet = solunar.SunSet,
            dawn = solunar.Dawn,
            dusk = solunar.Dusk,
            majorWindow = new { start = solunar.MajorStart, end = solunar.MajorEnd },
            major2Window = new { start = solunar.Major2Start, end = solunar.Major2End },
            minorWindow = new { start = solunar.MinorStart, end = solunar.MinorEnd },
            minor2Window = new { start = solunar.Minor2Start, end = solunar.Minor2End },
        },
        weather,
        satellite,
    });
});

app.Run();

public partial class Program;
