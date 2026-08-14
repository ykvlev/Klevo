using System.Globalization;
using Klevo.Core.Data;
using Klevo.Ingest;
using Microsoft.EntityFrameworkCore;

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
    ?? "Host=localhost;Port=5432;Database=klevo;Username=postgres;Password=klevo_dev_pwd";

var command = args.Length > 0 ? args[0].ToLowerInvariant() : "all";
var days = ParseInt(args, "--days", 7);
var from = ParseDate(args, "--from");
var to = ParseDate(args, "--to");

var options = new DbContextOptionsBuilder<KlevoDbContext>()
    .UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite())
    .Options;

using var db = new KlevoDbContext(options);

switch (command)
{
    case "solunar":
        await RunSolunarAsync(db, from, to, days);
        break;
    case "weather":
        await RunWeatherAsync(db, from, to, days);
        break;
    case "all":
        await RunWeatherAsync(db, from, to, days);
        await RunSolunarAsync(db, from, to, days);
        break;
    default:
        Console.Error.WriteLine($"Неизвестная команда: {command} (ожидается solunar | weather | all)");
        return 1;
}

return 0;

static async Task RunSolunarAsync(KlevoDbContext db, DateOnly? from, DateOnly? to, int days)
{
    var spots = await LoadSpotsAsync(db);
    var (startDate, endDate) = ResolveRange(from, to, days);

    foreach (var spot in spots)
    {
        for (var d = startDate; d <= endDate; d = d.AddDays(1))
        {
            var day = AstroService.ComputeDay(spot.Id, d, spot.Lat, spot.Lon);
            db.SolunarDays.RemoveRange(db.SolunarDays.Where(x => x.SpotId == spot.Id && x.Date == d));
            db.SolunarDays.Add(day);
        }
        Console.WriteLine($"solunar: {spot.Name} ({startDate:yyyy-MM-dd}..{endDate:yyyy-MM-dd})");
    }

    await db.SaveChangesAsync();
    Console.WriteLine($"solunar: загружено строк на {spots.Count} точек x {endDate.DayNumber - startDate.DayNumber + 1} дней");
}

static async Task RunWeatherAsync(KlevoDbContext db, DateOnly? from, DateOnly? to, int days)
{
    var spots = await LoadSpotsAsync(db);
    var (startDate, endDate) = ResolveRange(from, to, days);

    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    var client = new OpenMeteoClient(http);

    foreach (var spot in spots)
    {
        var useArchive = startDate < DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-1));
        var rows = useArchive
            ? await client.GetArchiveAsync(spot.Lat, spot.Lon, startDate, endDate, CancellationToken.None)
            : await client.GetForecastAsync(spot.Lat, spot.Lon, startDate, endDate, CancellationToken.None);

        var fromUtc = startDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = endDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        db.WeatherObservations.RemoveRange(db.WeatherObservations.Where(
            o => o.SpotId == spot.Id && o.ObservedAt >= fromUtc && o.ObservedAt < toUtc));

        foreach (var r in rows)
        {
            db.WeatherObservations.Add(new WeatherObservation
            {
                SpotId = spot.Id,
                ObservedAt = r.TimeUtc,
                Temperature2m = r.Temperature2m,
                PressureMsl = r.PressureMsl,
                Humidity2m = r.Humidity2m,
                WindSpeed10m = r.WindSpeed10m,
                WindDir10m = r.WindDir10m,
                WindGusts10m = r.WindGusts10m,
                Precip = r.Precip,
                CloudCover = r.CloudCover,
                SnowDepth = r.SnowDepth,
                Source = useArchive ? "open-meteo-archive" : "open-meteo",
            });
        }

        Console.WriteLine($"weather: {spot.Name} ({rows.Count} ч)");
    }

    await db.SaveChangesAsync();
    Console.WriteLine("weather: готово");
}

static async Task<List<SpotGeo>> LoadSpotsAsync(KlevoDbContext db)
{
    var spots = await db.Spots.AsNoTracking().ToListAsync();
    return spots
        .Select(s => new SpotGeo(s.Id, s.Name, s.Location.Y, s.Location.X))
        .ToList();
}

static (DateOnly start, DateOnly end) ResolveRange(DateOnly? from, DateOnly? to, int days)
{
    var start = from ?? DateOnly.FromDateTime(DateTime.UtcNow);
    var end = to ?? start.AddDays(days - 1);
    return (start, end);
}

static int ParseInt(string[] args, string key, int fallback)
{
    for (var i = 0; i < args.Length - 1; i++)
        if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(args[i + 1], out var v))
            return v;
    return fallback;
}

static DateOnly? ParseDate(string[] args, string key)
{
    for (var i = 0; i < args.Length - 1; i++)
        if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase) &&
            DateOnly.TryParseExact(args[i + 1], "yyyy-MM-dd", out var v))
            return v;
    return null;
}

record SpotGeo(Guid Id, string Name, double Lat, double Lon);
