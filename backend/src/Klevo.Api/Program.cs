using Klevo.Api;
using Klevo.Core.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Host=localhost;Port=5432;Database=klevo;Username=postgres;Password=klevo_dev_pwd";

builder.Services.AddDbContext<KlevoDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite()));

builder.Services.AddSingleton<MlModelRunner>();
builder.Services.AddSingleton<FishIdService>();
builder.Services.AddScoped<MlFeatureBuilder>(_ => new MlFeatureBuilder(connectionString));
builder.Services.AddScoped<SatelliteEstimator>();
builder.Services.AddScoped<RuleChecker>();

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
{
    var spots = await db.Spots
        .OrderBy(s => s.Name)
        .Select(s => new { s.Id, s.Name, s.WaterType, s.Region, s.ZoneId, s.Location })
        .ToListAsync();
    return spots.Select(s => new
    {
        id = s.Id,
        name = s.Name,
        waterType = s.WaterType,
        region = s.Region,
        zoneId = s.ZoneId,
        lat = s.Location.Y,
        lon = s.Location.X,
    });
});

app.MapGet("/api/spots/{id}/conditions", async (
    Guid id, DateOnly date, KlevoDbContext db, SatelliteEstimator sat) =>
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

    await using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();
    var features = new (string Column, string Label, string Unit, string[] Sources, int MaxDays, bool Model)[]
    {
        ("sst_c", "Температура воды", "°C", SatelliteEstimator.SstSources, SatelliteEstimator.SstMaxDays, true),
        ("chla_mgm3", "Хлорофилл", "мг/м³", SatelliteEstimator.ChlaSources, SatelliteEstimator.ChlaMaxDays, true),
        ("bottom_t_c", "Температура у дна", "°C", SatelliteEstimator.PhySources, SatelliteEstimator.OceanMaxDays, false),
        ("mlotst_m", "Глубина перемешивания", "м", SatelliteEstimator.PhySources, SatelliteEstimator.OceanMaxDays, false),
        ("salinity_psu", "Солёность", "PSU", SatelliteEstimator.PhySources, SatelliteEstimator.OceanMaxDays, false),
    };
    var summary = new List<object>();
    foreach (var f in features)
    {
        var e = await sat.EstimateAsync(conn, id, date, f.Column, f.Sources, f.MaxDays);
        if (e is null)
            continue;
        summary.Add(new
        {
            feature = f.Column,
            label = f.Label,
            value = e.Value,
            unit = f.Unit,
            source = e.Source,
            observedAt = e.ObservedAt,
            estimated = e.Estimated,
            confidence = e.Confidence,
            basis = e.Basis,
            usedInModel = f.Model,
        });
    }
    var sources = (await sat.SourcesAsync(conn, id))
        .Select(s => new { source = s.Key, label = s.Label, lastObservation = s.LastObservation, staleDays = s.StaleDays, status = s.Status })
        .ToList();

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
        satelliteSummary = summary,
        sources,
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

app.MapPost("/api/rule-checks", async (RuleCheckRequest req, KlevoDbContext db, RuleChecker checker) =>
{
    var r = await checker.CheckAsync(db, req);
    if (!r.Found)
        return Results.BadRequest(new { error = r.Error });

    return Results.Ok(new
    {
        allowed = r.Allowed,
        spot = new { name = r.SpotName },
        zone = new { id = r.ZoneId, name = r.ZoneName },
        species = new { id = r.SpeciesId, nameRu = r.SpeciesName, nameLatin = r.SpeciesLatin },
        day = r.Day,
        checks = r.Checks!.Select(c => new { type = c.Type, ok = c.Ok, message = c.Message }),
        summary = r.Summary,
    });
});

app.MapPost("/api/uploads", async (HttpRequest request, IWebHostEnvironment env) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest(new { error = "Ожидается multipart/form-data" });

    var file = request.Form.Files.FirstOrDefault();
    if (file is null || file.Length == 0)
        return Results.BadRequest(new { error = "Файл не выбран" });
    if (file.Length > 10 * 1024 * 1024)
        return Results.BadRequest(new { error = "Фото больше 10 МБ" });

    var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
    if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp"))
        return Results.BadRequest(new { error = "Поддерживаются форматы JPG, PNG, WebP" });

    var sub = DateTime.UtcNow.ToString("yyyy-MM-dd");
    var dir = Path.Combine(env.WebRootPath, "uploads", sub);
    Directory.CreateDirectory(dir);
    var name = $"{Guid.NewGuid():N}{ext}";
    var path = Path.Combine(dir, name);
    await using (var fs = File.Create(path))
        await file.CopyToAsync(fs);

    return Results.Ok(new { url = $"/uploads/{sub}/{name}" });
});

app.MapPost("/api/fish-id", async (HttpRequest request, FishIdService fishId, KlevoDbContext db) =>
{
    if (!fishId.IsAvailable)
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

    byte[]? bytes = null;
    if (request.HasFormContentType)
    {
        try
        {
            var file = request.Form.Files.FirstOrDefault();
            if (file is not null && file.Length > 0 && file.Length <= 10 * 1024 * 1024)
            {
                await using var ms = new MemoryStream();
                await file.CopyToAsync(ms);
                bytes = ms.ToArray();
            }
        }
        catch (InvalidDataException)
        {
            return Results.BadRequest(new { error = "Некорректное multipart-тело запроса" });
        }
    }
    else if (request.HasJsonContentType())
    {
        var body = await request.ReadFromJsonAsync<FishIdDataUrlRequest>();
        var comma = body?.DataUrl?.IndexOf(',') ?? -1;
        if (comma > 0 && body?.DataUrl is not null)
            bytes = Convert.FromBase64String(body.DataUrl[(comma + 1)..]);
    }

    if (bytes is null || bytes.Length == 0)
        return Results.BadRequest(new { error = "Изображение не передано (multipart file или JSON dataUrl)" });

    IReadOnlyList<FishIdPrediction> predictions;
    try
    {
        predictions = fishId.Predict(bytes);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = $"Не удалось распознать изображение: {ex.Message}" });
    }

    var species = await db.Species.ToListAsync();
    var top = predictions.Select(p =>
    {
        var s = species.FirstOrDefault(x =>
            (x.NameLatin ?? "").Equals(p.NameLatin, StringComparison.OrdinalIgnoreCase) ||
            (x.NameLatin ?? "").StartsWith(p.NameLatin + " ", StringComparison.OrdinalIgnoreCase) ||
            p.NameLatin.StartsWith((x.NameLatin ?? "") + " ", StringComparison.OrdinalIgnoreCase));
        return new
        {
            speciesId = s?.Id,
            nameRu = s?.NameRu ?? p.NameRu,
            nameLatin = p.NameLatin,
            confidence = Math.Round(p.Confidence, 4),
        };
    }).ToList();

    return Results.Ok(new
    {
        modelVersion = FishIdService.ModelVersion,
        top,
    });
});

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
    Guid id, DateOnly? date, KlevoDbContext db, MlFeatureBuilder features, MlModelRunner model,
    SatelliteEstimator sat) =>
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

    var (sources, satellite, dataConfidence, dataNote) =
        await BuildDataProvenanceAsync(id, day, db, sat, connectionString);

    return Results.Ok(new
    {
        spotId = id,
        date = day,
        score,
        bestStart = bestStart?.ToString("HH:mm"),
        bestEnd = bestEnd?.ToString("HH:mm"),
        modelVersion = version,
        dataConfidence,
        sources,
        satellite,
        dataNote,
    });
});

/// <summary>Собирает происхождение данных прогноза, оценки условий и достоверность.</summary>
static async Task<(List<object> Sources, List<object> Satellite, int Confidence, string Note)>
    BuildDataProvenanceAsync(Guid spotId, DateOnly day, KlevoDbContext db,
        SatelliteEstimator sat, string connectionString)
{
    await using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();

    var sst = await sat.EstimateAsync(conn, spotId, day, "sst_c",
        SatelliteEstimator.SstSources, SatelliteEstimator.SstMaxDays);
    var chla = await sat.EstimateAsync(conn, spotId, day, "chla_mgm3",
        SatelliteEstimator.ChlaSources, SatelliteEstimator.ChlaMaxDays);

    var sources = new List<object>();
    foreach (var s in await sat.SourcesAsync(conn, spotId))
        sources.Add(new { source = s.Key, label = s.Label, lastObservation = s.LastObservation, staleDays = s.StaleDays, status = s.Status });

    var lastWeather = await db.WeatherObservations
        .Where(o => o.SpotId == spotId)
        .Select(o => (DateOnly?)DateOnly.FromDateTime(o.ObservedAt))
        .OrderByDescending(o => o)
        .FirstOrDefaultAsync();
    var weatherStale = lastWeather is null ? int.MaxValue : day.DayNumber - lastWeather.Value.DayNumber;
    var weatherStaleDisplay = weatherStale == int.MaxValue ? weatherStale : Math.Max(0, weatherStale);
    sources.Add(new
    {
        source = "openmeteo",
        label = "Open-Meteo (погода)",
        lastObservation = lastWeather,
        staleDays = weatherStaleDisplay,
        status = weatherStale <= 3 ? "ok" : weatherStale <= 30 ? "warn" : "stale",
    });

    var hasSolunar = await db.SolunarDays.AnyAsync(d => d.SpotId == spotId && d.Date == day);
    sources.Add(new
    {
        source = "solunar",
        label = "Астрономия (солунар)",
        lastObservation = (DateOnly?)day,
        staleDays = 0,
        status = hasSolunar ? "ok" : "stale",
    });

    var satellite = new List<object>();
    if (sst is not null)
        satellite.Add(new
        {
            feature = "sst_c",
            label = "Температура воды",
            value = sst.Value,
            unit = "°C",
            source = sst.Source,
            observedAt = sst.ObservedAt,
            estimated = sst.Estimated,
            confidence = sst.Confidence,
            basis = sst.Basis,
        });
    if (chla is not null)
        satellite.Add(new
        {
            feature = "chla_mgm3",
            label = "Хлорофилл",
            value = chla.Value,
            unit = "мг/м³",
            source = chla.Source,
            observedAt = chla.ObservedAt,
            estimated = chla.Estimated,
            confidence = chla.Confidence,
            basis = chla.Basis,
        });

    var confidences = new List<int>();
    if (sst is not null) confidences.Add(sst.Confidence);
    if (chla is not null) confidences.Add(chla.Confidence);
    confidences.Add(weatherStale <= 3 ? 90 : weatherStale <= 30 ? 75 : 60);
    confidences.Add(hasSolunar ? 95 : 70);
    var confidence = (int)Math.Round(confidences.Average());

    var parts = new List<string>();
    if (sst is not null)
        parts.Add($"температура воды — {sst.Basis}{(sst.Estimated ? $" (достоверность {sst.Confidence}%)" : "")}");
    if (chla is not null)
        parts.Add($"хлорофилл — {chla.Basis}{(chla.Estimated ? $" (достоверность {chla.Confidence}%)" : "")}");
    parts.Add(weatherStale <= 3
        ? "погода — прогноз Open-Meteo"
        : "погода — нет актуальных данных");
    parts.Add(hasSolunar ? "солунар — расчётный" : "солунар — нет данных");
    var note = $"Данные: {string.Join("; ", parts)}. Достоверность данных ~{confidence}%.";

    return (sources, satellite, confidence, note);
}

app.Run();

record CreateCatchRequest(
    Guid? SpeciesId, string? SpeciesName, decimal? WeightKg,
    decimal? LengthCm, string? PhotoUrl, DateTime? CaughtAt, string? Notes);

public partial class Program;
