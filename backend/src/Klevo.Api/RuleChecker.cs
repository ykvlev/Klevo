using Klevo.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Klevo.Api;

public sealed record RuleCheckRequest(
    Guid SpotId,
    Guid? SpeciesId = null,
    string? SpeciesName = null,
    decimal? WeightKg = null,
    decimal? LengthCm = null,
    DateTime? CaughtAt = null);

public sealed record RuleCheckItem(
    string Type,
    bool Ok,
    string Message);

public sealed record RuleCheckOutcome(
    bool Found,
    bool Allowed,
    string? Error = null,
    string? SpotName = null,
    string? ZoneId = null,
    string? ZoneName = null,
    Guid? SpeciesId = null,
    string? SpeciesName = null,
    string? SpeciesLatin = null,
    DateOnly? Day = null,
    IReadOnlyList<RuleCheckItem>? Checks = null,
    string? Summary = null)
{
    public static RuleCheckOutcome Fail(string error) =>
        new(false, false, Error: error);
}

public sealed class RuleChecker
{
    public async Task<RuleCheckOutcome> CheckAsync(KlevoDbContext db, RuleCheckRequest req)
    {
        var spot = await db.Spots.FindAsync(req.SpotId);
        if (spot is null)
            return RuleCheckOutcome.Fail("Точка не найдена");

        if (spot.ZoneId is null)
            return RuleCheckOutcome.Fail("Для точки не задана рыбохозяйственная зона");
        var zone = await db.Zones.FindAsync(spot.ZoneId);
        if (zone is null)
            return RuleCheckOutcome.Fail("Рыбохозяйственная зона не найдена");

        var species = await ResolveSpeciesAsync(db, req);
        if (species is null)
            return RuleCheckOutcome.Fail("Вид не распознан — проверка невозможна");

        var day = DateOnly.FromDateTime((req.CaughtAt ?? DateTime.UtcNow).ToUniversalTime());
        var checks = new List<RuleCheckItem>();

        var speciesBan = await db.Bans.FirstOrDefaultAsync(b =>
            b.ZoneId == zone.Id && b.BanType == "species" && b.SpeciesId == species.Id);
        var speciesBanOk = speciesBan is null;
        checks.Add(new RuleCheckItem("banned_species", speciesBanOk,
            speciesBanOk
                ? "Вид разрешён к вылову в этой зоне"
                : $"Вид в запрете: {speciesBan!.RuleText}"));

        var seasonBans = await db.Bans
            .Where(b => b.ZoneId == zone.Id && b.BanType == "season"
                        && (b.SpeciesId == species.Id || b.SpeciesId == null)
                        && b.PeriodFrom != null && b.PeriodTo != null)
            .ToListAsync();

        var areaBans = seasonBans.Where(b => !string.IsNullOrEmpty(b.Area)).ToList();
        if (areaBans.Count > 0)
        {
            checks.Add(new RuleCheckItem("area_ban", true,
                $"В зоне есть запреты по конкретным участкам ({areaBans.Count}) — требуют точного места вылова"));
        }

        var activeSeason = seasonBans
            .Where(b => string.IsNullOrEmpty(b.Area))
            .FirstOrDefault(b => DayInPeriod(day, b.PeriodFrom!.Value, b.PeriodTo!.Value));
        checks.Add(new RuleCheckItem("season_ban", activeSeason is null,
            activeSeason is null
                ? "Сезонных запретов на этот период нет"
                : $"Нерестовый запрет {activeSeason.PeriodFrom:dd.MM}–{activeSeason.PeriodTo:dd.MM}: {activeSeason.RuleText}"));

        var sizeRule = await db.SizeRules.FirstOrDefaultAsync(r =>
            r.ZoneId == zone.Id && r.SpeciesId == species.Id);
        if (sizeRule is null)
        {
            checks.Add(new RuleCheckItem("min_size", true,
                "Промысловый размер для вида не установлен"));
        }
        else if (req.LengthCm is null)
        {
            checks.Add(new RuleCheckItem("min_size", true,
                $"Минимальный размер — {sizeRule.MinSizeCm:0.#} см (размер не указан)"));
        }
        else if (req.LengthCm >= sizeRule.MinSizeCm)
        {
            checks.Add(new RuleCheckItem("min_size", true,
                $"{req.LengthCm:0.#} см ≥ минимальные {sizeRule.MinSizeCm:0.#} см — размер ок"));
        }
        else
        {
            checks.Add(new RuleCheckItem("min_size", false,
                $"{req.LengthCm:0.#} см < минимального размера {sizeRule.MinSizeCm:0.#} см — рыбу нужно выпустить"));
        }

        var limitRule = await db.LimitRules.FirstOrDefaultAsync(r =>
            r.ZoneId == zone.Id && r.SpeciesId == species.Id);
        if (limitRule is null)
        {
            checks.Add(new RuleCheckItem("daily_limit", true,
                "Суточная норма для вида не установлена"));
        }
        else
        {
            var dayUtc = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var unit = limitRule.Unit == "kg" ? "кг" : "шт";
            if (limitRule.Unit == "kg")
            {
                var weightToday = await db.Catches
                    .Where(c => c.SpotId == req.SpotId && c.SpeciesId == species.Id
                                && c.CaughtAt >= dayUtc && c.CaughtAt < dayUtc.AddDays(1))
                    .Select(c => c.WeightKg ?? 0m)
                    .SumAsync();
                var over = weightToday >= limitRule.LimitValue;
                checks.Add(new RuleCheckItem("daily_limit", !over,
                    over
                        ? $"Суточная норма {limitRule.LimitValue:0.#} {unit} превышена (уже зафиксировано {weightToday:0.#} {unit})"
                        : $"Суточная норма — {limitRule.LimitValue:0.#} {unit} (сегодня зафиксировано {weightToday:0.#} {unit})"));
            }
            else
            {
                var countToday = await db.Catches.CountAsync(c =>
                    c.SpotId == req.SpotId && c.SpeciesId == species.Id
                    && c.CaughtAt >= dayUtc && c.CaughtAt < dayUtc.AddDays(1));
                var over = countToday >= (int)limitRule.LimitValue;
                checks.Add(new RuleCheckItem("daily_limit", !over,
                    over
                        ? $"Суточная норма {limitRule.LimitValue:0} {unit} исчерпана (зафиксировано {countToday})"
                        : $"Суточная норма — {limitRule.LimitValue:0} {unit} (зафиксировано {countToday})"));
            }
        }

        var violations = checks.Where(c => !c.Ok).Select(c => c.Message).ToList();
        var allowed = violations.Count == 0;

        return new RuleCheckOutcome(
            Found: true,
            Allowed: allowed,
            SpotName: spot.Name,
            ZoneId: zone.Id,
            ZoneName: zone.Name,
            SpeciesId: species.Id,
            SpeciesName: species.NameRu,
            SpeciesLatin: species.NameLatin,
            Day: day,
            Checks: checks,
            Summary: allowed
                ? "Нарушений правил не выявлено — рыбу можно оставить"
                : $"Нарушения: {string.Join("; ", violations)}");
    }

    private static async Task<FishSpecies?> ResolveSpeciesAsync(KlevoDbContext db, RuleCheckRequest req)
    {
        if (req.SpeciesId is not null)
            return await db.Species.FindAsync(req.SpeciesId.Value);

        if (string.IsNullOrWhiteSpace(req.SpeciesName))
            return null;

        var name = req.SpeciesName.Trim();
        var all = await db.Species.ToListAsync();
        return all.FirstOrDefault(s =>
            string.Equals(s.NameRu, name, StringComparison.OrdinalIgnoreCase) ||
            s.Aliases.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Проверяет, попадает ли дата в период запрета по месяцу/дню (период может переходить через Новый год).</summary>
    private static bool DayInPeriod(DateOnly day, DateOnly from, DateOnly to)
    {
        int md(DateOnly d) => d.Month * 100 + d.Day;
        var d = md(day);
        var f = md(from);
        var t = md(to);
        return f <= t
            ? d >= f && d <= t
            : d >= f || d <= t;
    }
}
