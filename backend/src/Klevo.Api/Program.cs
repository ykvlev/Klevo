using Klevo.Api;
using Klevo.Core.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Host=localhost;Port=5432;Database=klevo;Username=postgres;Password=klevo_dev_pwd";

builder.Services.AddDbContext<KlevoDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite()));

builder.Services.AddSingleton<MlModelRunner>();
builder.Services.AddScoped<MlFeatureBuilder>(_ => new MlFeatureBuilder(connectionString));

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        if (ctx.File.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            ctx.Context.Response.ContentType = "text/html; charset=utf-8";
    },
});

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

app.MapGet("/api/species", async (KlevoDbContext db) =>
    await db.Species
        .OrderBy(s => s.NameRu)
        .Select(s => new
        {
            id = s.Id,
            nameRu = s.NameRu,
            nameLatin = s.NameLatin,
            isCrustacean = s.IsCrustacean,
        })
        .ToListAsync());

app.MapGet("/api/spots/{id}/catches", async (Guid id, DateOnly? from, DateOnly? to, KlevoDbContext db) =>
{
    var spot = await db.Spots.FindAsync(id);
    if (spot is null)
        return Results.NotFound();

    var query = db.Catches
        .Where(c => c.SpotId == id)
        .AsQueryable();
    if (from is not null)
        query = query.Where(c => c.CaughtAt >= DateTime.SpecifyKind(
            from.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc));
    if (to is not null)
        query = query.Where(c => c.CaughtAt < DateTime.SpecifyKind(
            to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc));

    return Results.Ok(await query
        .OrderByDescending(c => c.CaughtAt)
        .Select(c => new
        {
            id = c.Id,
            speciesId = c.SpeciesId,
            speciesName = c.SpeciesName,
            weightKg = c.WeightKg,
            lengthCm = c.LengthCm,
            photoUrl = c.PhotoUrl,
            caughtAt = c.CaughtAt,
            notes = c.Notes,
        })
        .ToListAsync());
});

app.MapPost("/api/spots/{id}/catches", async (Guid id, CreateCatchRequest req, KlevoDbContext db) =>
{
    var spot = await db.Spots.FindAsync(id);
    if (spot is null)
        return Results.NotFound();

    var speciesName = req.SpeciesName ?? "";
    if (req.SpeciesId is not null)
    {
        var species = await db.Species.FindAsync(req.SpeciesId.Value);
        if (species is not null)
            speciesName = species.NameRu;
    }

    var caughtAt = (req.CaughtAt ?? DateTime.UtcNow).ToUniversalTime();
    var catchEntity = new Catch
    {
        Id = Guid.NewGuid(),
        SpotId = id,
        SpeciesId = req.SpeciesId,
        SpeciesName = speciesName,
        WeightKg = req.WeightKg,
        LengthCm = req.LengthCm,
        PhotoUrl = req.PhotoUrl,
        CaughtAt = caughtAt,
        Notes = req.Notes,
        CreatedAt = DateTime.UtcNow,
    };
    db.Catches.Add(catchEntity);
    await db.SaveChangesAsync();

    return Results.Created($"/api/spots/{id}/catches/{catchEntity.Id}", new
    {
        id = catchEntity.Id,
        speciesName = catchEntity.SpeciesName,
        caughtAt = catchEntity.CaughtAt,
    });
});

app.MapGet("/api/spots/{id}/forecast", async (
    Guid id, DateOnly? date, KlevoDbContext db, MlFeatureBuilder features, MlModelRunner model) =>
{
    var spot = await db.Spots.FindAsync(id);
    if (spot is null)
        return Results.NotFound();

    var day = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
    var prediction = await db.Predictions
        .Where(p => p.SpotId == id && p.Date == day)
        .SingleOrDefaultAsync();

    int? score = null;
    var version = "rule-v1";
    TimeOnly? bestStart = null;
    TimeOnly? bestEnd = null;

    if (model.IsAvailable)
    {
        try
        {
            var vector = await features.BuildAsync(id, day);
            var solunar = await features.LoadSolunarForWindowAsync(id, day);
            var prob = model.Predict(vector);
            score = (int)Math.Round(Math.Clamp(prob * 100, 0, 100), 0);
            version = MlFeatureBuilder.ModelVersion;
            (bestStart, bestEnd) = features.BestWindow(day, solunar);
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "ML forecast failed, falling back to rule-v1");
        }
    }

    if (score is null && prediction is not null)
    {
        score = prediction.Score;
        version = prediction.ModelVersion;
        bestStart = prediction.BestStart;
        bestEnd = prediction.BestEnd;
    }

    if (score is null)
        return Results.NotFound();

    return Results.Ok(new
    {
        spotId = id,
        date = day,
        score,
        bestStart = bestStart?.ToString("HH:mm"),
        bestEnd = bestEnd?.ToString("HH:mm"),
        modelVersion = version,
    });
});

app.Run();

record CreateCatchRequest(
    Guid? SpeciesId, string? SpeciesName, decimal? WeightKg,
    decimal? LengthCm, string? PhotoUrl, DateTime? CaughtAt, string? Notes);

public partial class Program;
