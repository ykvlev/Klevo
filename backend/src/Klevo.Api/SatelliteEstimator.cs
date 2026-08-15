using Npgsql;

namespace Klevo.Api;

/// <summary>
/// Оценка спутниковых условий для (spot, date) с указанием происхождения данных:
/// фактическое значение по последнему снимку, либо климатологическая оценка
/// (среднее за те же числа года из истории точки), когда данные устарели.
/// Для каждой оценки рассчитывается достоверность (0–100) по формуле
/// Conf(актуальное) = 70 + 25·(1 − stale/maxDays), Conf(оценка) =
/// 40 + 55·min(n/40,1)·(1 − min(stale,180)/180), где n — число выборок климатологии.
/// </summary>
public sealed class SatelliteEstimator
{
    public const int SstMaxDays = 30;
    public const int ChlaMaxDays = 60;
    public const int OceanMaxDays = 45;
    public const int ClimatologyWindowDays = 30;

    public static readonly string[] SstSources = ["cmems_bal_my_phy", "nasa_modis_aqua"];
    public static readonly string[] ChlaSources = ["cmems_bal_my_bgc"];
    public static readonly string[] PhySources = ["cmems_bal_my_phy"];

    private static readonly Dictionary<string, string> Labels = new()
    {
        ["nasa_modis_aqua"] = "NASA MODIS-Aqua (снимки SST)",
        ["cmems_bal_my_phy"] = "CMEMS реанализ Балтики (море)",
        ["cmems_bal_my_bgc"] = "CMEMS хлорофилл (море)",
    };

    /// <summary>Последняя дата наблюдения по каждому спутниковому источнику точки.</summary>
    public async Task<List<SourceInfo>> SourcesAsync(NpgsqlConnection conn, Guid spotId)
    {
        const string sql = """
            SELECT source, max(observed_at)::date
            FROM satellite_obs
            WHERE spot_id = @spot
            GROUP BY source
            ORDER BY source
            """;
        var list = new List<SourceInfo>();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@spot", spotId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var key = r.GetString(0);
            var last = r.GetFieldValue<DateOnly>(1);
            var stale = DateOnly.FromDateTime(DateTime.UtcNow).DayNumber - last.DayNumber;
            list.Add(new SourceInfo(key, Label(key), last, stale, Status(stale)));
        }
        return list;
    }

    /// <summary>
    /// Оценка признака {column} на дату date: актуальное значение последнего снимка
    /// (в пределах maxDays), иначе климатология по истории точки (±30 дней от дня года).
    /// Возвращает null, если истории по источнику нет вовсе.
    /// </summary>
    public async Task<FeatureEstimate?> EstimateAsync(
        NpgsqlConnection conn, Guid spotId, DateOnly date,
        string column, string[] sources, int maxDays)
    {
        var latest = await LatestAsync(conn, spotId, date, column, sources);
        int staleDays;
        if (latest is not null)
            staleDays = date.DayNumber - latest.ObservedAt.DayNumber;
        else
            staleDays = maxDays;

        if (latest is not null && staleDays <= maxDays)
        {
            return new FeatureEstimate(
                Value: (float)latest.Value,
                Source: latest.Source,
                ObservedAt: latest.ObservedAt,
                StaleDays: staleDays,
                Estimated: false,
                Confidence: Confidence(false, staleDays, maxDays, 1),
                Samples: 1,
                Basis: $"снимок от {latest.ObservedAt:dd.MM.yyyy}");
        }

        var clima = await ClimatologyAsync(conn, spotId, date, column, sources);
        if (clima is null || clima.Count == 0)
            return null;

        return new FeatureEstimate(
            Value: (float)clima.Mean,
            Source: latest?.Source,
            ObservedAt: latest?.ObservedAt,
            StaleDays: staleDays,
            Estimated: true,
            Confidence: Confidence(true, staleDays, maxDays, clima.Count),
            Samples: clima.Count,
            Basis: $"климатология {clima.MinYear}–{clima.MaxYear} (n={clima.Count})");
    }

    public static string Label(string key) => Labels.TryGetValue(key, out var l) ? l : key;

    private static string Status(int staleDays) =>
        staleDays <= 3 ? "ok" : staleDays <= 30 ? "warn" : "stale";

    private static int Confidence(bool estimated, int staleDays, int maxDays, int samples)
    {
        if (!estimated)
        {
            var f = Math.Clamp(1.0 - (double)staleDays / Math.Max(1, maxDays), 0.0, 1.0);
            return (int)Math.Round(70 + 25 * f);
        }
        var sampleFactor = Math.Min(1.0, samples / 40.0);
        var freshness = Math.Clamp(1.0 - (double)staleDays / 180.0, 0.0, 1.0);
        return (int)Math.Round(40 + 55 * sampleFactor * freshness);
    }

    private static async Task<LatestRow?> LatestAsync(
        NpgsqlConnection conn, Guid spotId, DateOnly date,
        string column, string[] sources)
    {
        const string sql = """
            SELECT observed_at, {0}, source
            FROM satellite_obs
            WHERE spot_id = @spot
              AND observed_at <= @date
              AND {0} IS NOT NULL
              AND source = ANY(@sources)
            ORDER BY observed_at DESC, array_position(@sources, source)
            LIMIT 1
            """;
        await using var cmd = new NpgsqlCommand(string.Format(sql, column), conn);
        cmd.Parameters.AddWithValue("@spot", spotId);
        cmd.Parameters.AddWithValue("@date", date);
        cmd.Parameters.AddWithValue("@sources", sources);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync() || r.IsDBNull(1))
            return null;
        return new LatestRow(r.GetFieldValue<DateOnly>(0), (double)r.GetDecimal(1), r.GetString(2));
    }

    private static async Task<ClimatologyRow?> ClimatologyAsync(
        NpgsqlConnection conn, Guid spotId, DateOnly date,
        string column, string[] sources)
    {
        const string sql = """
            SELECT avg({0})::float8, count({0}), min(observed_at), max(observed_at)
            FROM satellite_obs
            WHERE spot_id = @spot
              AND {0} IS NOT NULL
              AND source = ANY(@sources)
              AND (abs(extract(doy from observed_at) - @doy) <= @win
                   OR abs(abs(extract(doy from observed_at) - @doy) - 366) <= @win)
            """;
        await using var cmd = new NpgsqlCommand(string.Format(sql, column), conn);
        cmd.Parameters.AddWithValue("@spot", spotId);
        cmd.Parameters.AddWithValue("@doy", date.DayOfYear);
        cmd.Parameters.AddWithValue("@win", ClimatologyWindowDays);
        cmd.Parameters.AddWithValue("@sources", sources);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync() || r.IsDBNull(0) || r.IsDBNull(1))
            return null;
        var count = r.GetInt32(1);
        return new ClimatologyRow(r.GetDouble(0), count, r.GetDateTime(2).Year, r.GetDateTime(3).Year);
    }

    public sealed record SourceInfo(string Key, string Label, DateOnly LastObservation, int StaleDays, string Status);

    public sealed record FeatureEstimate(
        float? Value, string? Source, DateOnly? ObservedAt, int StaleDays,
        bool Estimated, int Confidence, int Samples, string Basis);

    private sealed record LatestRow(DateOnly ObservedAt, double Value, string Source);

    private sealed record ClimatologyRow(double Mean, int Count, int MinYear, int MaxYear);
}
