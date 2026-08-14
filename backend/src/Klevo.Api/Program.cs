using Klevo.Api.Data;
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

app.Run();

public partial class Program;
