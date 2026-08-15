using Npgsql;

namespace Klevo.Api;

/// <summary>
/// Строит вектор признаков ML-модели клёва для (spot, date) — один в один
/// с python/ml/features.py (см. MODEL_COLS в train.py).
/// </summary>
public sealed class MlFeatureBuilder(string connectionString)
{
    public const int FeatureCount = 24;
    public const string ModelVersion = "ml-v1";

    private static readonly string[] SstSources = ["cmems_bal_my_phy", "nasa_modis_aqua"];

    /// <summary>
    /// Признаки в порядке MODEL_COLS из train.py:
    /// moon_phase, moon_illumination, major_hours, major_best_h, minor_hours,
    /// t_min, t_mean, t_max, pressure_mean, pressure_amp, humidity_mean,
    /// wind_mean, wind_max, precip_sum, cloud_mean, snow_max, sst_c, chla_mgm3,
    /// t_delta, pressure_delta, doy, month, weekday, season
    /// </summary>
    public async Task<float[]> BuildAsync(Guid spotId, DateOnly date)
    {
        var v = new float[FeatureCount];
        for (var i = 0; i < v.Length; i++)
            v[i] = float.NaN;

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var weather = await LoadWeatherAsync(conn, spotId, date);
        var yesterday = await LoadWeatherAsync(conn, spotId, date.AddDays(-1));
        var solunar = await LoadSolunarAsync(conn, spotId, date);

        v[0] = Num(solunar?.MoonPhase);
        v[1] = Num(solunar?.MoonIllumination);
        v[2] = solunar is null ? 0f : (float)WindowHours(date, solunar.MajorStart, solunar.MajorEnd)
                                       + (float)WindowHours(date, solunar.Major2Start, solunar.Major2End);
        v[3] = solunar is null ? 0f : (float)Math.Max(
            WindowHours(date, solunar.MajorStart, solunar.MajorEnd),
            WindowHours(date, solunar.Major2Start, solunar.Major2End));
        v[4] = solunar is null ? 0f : (float)WindowHours(date, solunar.MinorStart, solunar.MinorEnd)
                                       + (float)WindowHours(date, solunar.Minor2Start, solunar.Minor2End);
        v[5] = Num(weather?.TMin);
        v[6] = Num(weather?.TMean);
        v[7] = Num(weather?.TMax);
        v[8] = Num(weather?.PressureMean);
        v[9] = Num(weather?.PressureAmp);
        v[10] = Num(weather?.HumidityMean);
        v[11] = Num(weather?.WindMean);
        v[12] = Num(weather?.WindMax);
        v[13] = Num(weather?.PrecipSum);
        v[14] = Num(weather?.CloudMean);
        v[15] = Num(weather?.SnowMax);
        v[16] = await LoadFfAsync(conn, spotId, date, "sst_c", SstSources, 30) ?? float.NaN;
        v[17] = await LoadFfAsync(conn, spotId, date, "chla_mgm3", ["cmems_bal_my_bgc"], 60) ?? float.NaN;
        v[18] = weather?.TMean is not null && yesterday?.TMean is not null
            ? (float)(weather.TMean.Value - yesterday.TMean.Value)
            : float.NaN;
        v[19] = weather?.PressureMean is not null && yesterday?.PressureMean is not null
            ? (float)(weather.PressureMean.Value - yesterday.PressureMean.Value)
            : float.NaN;
        v[20] = date.DayOfYear;
        v[21] = date.Month;
        v[22] = (int)date.DayOfWeek - 1; // понедельник = 0, как pandas weekday
        v[23] = date.Month switch
        {
            12 or 1 or 2 => 0,
            3 or 4 or 5 => 1,
            6 or 7 or 8 => 2,
            _ => 3,
        };
        return v;
    }

    /// <summary>Лучшее окно клёва дня (major/minor, иначе рассвет+3ч).</summary>
    public (TimeOnly? Start, TimeOnly? End) BestWindow(DateOnly date, SolunarRow? solunar)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time");
        (double Dur, DateTime Lo, DateTime Hi)? best = null;

        foreach (var (s, e) in new[]
        {
            (solunar?.MajorStart, solunar?.MajorEnd),
            (solunar?.Major2Start, solunar?.Major2End),
            (solunar?.MinorStart, solunar?.MinorEnd),
            (solunar?.Minor2Start, solunar?.Minor2End),
        })
        {
            if (s is null || e is null)
                continue;
            var day = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var a = s.Value.ToUniversalTime();
            var b = e.Value.ToUniversalTime();
            a = a < day ? day : a;
            b = b > day.AddDays(1) ? day.AddDays(1) : b;
            var dur = (b - a).TotalHours;
            if (dur <= 0)
                continue;
            if (best is null || dur > best.Value.Dur)
                best = (dur, a, b);
        }

        if (best is null)
        {
            if (solunar?.SunRise is DateTime sr)
            {
                var start = TimeZoneInfo.ConvertTime(sr.ToUniversalTime(), tz);
                return (TimeOnly.FromTimeSpan(start.TimeOfDay),
                        TimeOnly.FromTimeSpan(start.AddHours(3).TimeOfDay));
            }
            return (new TimeOnly(6, 0), new TimeOnly(9, 0));
        }

        var from = TimeZoneInfo.ConvertTime(best.Value.Lo, tz);
        var to = TimeZoneInfo.ConvertTime(best.Value.Hi, tz);
        return (TimeOnly.FromTimeSpan(from.TimeOfDay), TimeOnly.FromTimeSpan(to.TimeOfDay));
    }

    public async Task<SolunarRow?> LoadSolunarForWindowAsync(Guid spotId, DateOnly date)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        return await LoadSolunarAsync(conn, spotId, date);
    }

    private static double WindowHours(DateOnly date, DateTime? start, DateTime? end)
    {
        if (start is null || end is null)
            return 0;
        var day = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var next = day.AddDays(1);
        var a = start.Value.ToUniversalTime();
        var b = end.Value.ToUniversalTime();
        var lo = a < day ? day : a;
        var hi = b > next ? next : b;
        var hours = (hi - lo).TotalHours;
        return hours > 0 ? hours : 0;
    }

    private static float Num(decimal? d) => d.HasValue ? (float)d.Value : float.NaN;

    private static async Task<WeatherRow?> LoadWeatherAsync(
        NpgsqlConnection conn, Guid spotId, DateOnly date)
    {
        const string sql = """
            SELECT min(temperature_2m), avg(temperature_2m), max(temperature_2m),
                   avg(pressure_msl), max(pressure_msl) - min(pressure_msl),
                   avg(humidity_2m), avg(wind_speed_10m), max(wind_speed_10m),
                   sum(precip), avg(cloud_cover), max(snow_depth)
            FROM weather_obs
            WHERE spot_id = @spot
              AND (observed_at AT TIME ZONE 'Europe/Moscow')::date = @date
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@spot", spotId);
        cmd.Parameters.AddWithValue("@date", date);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync())
            return null;
        return new WeatherRow(
            TMin: Get(r, 0), TMean: Get(r, 1), TMax: Get(r, 2),
            PressureMean: Get(r, 3), PressureAmp: Get(r, 4),
            HumidityMean: Get(r, 5), WindMean: Get(r, 6), WindMax: Get(r, 7),
            PrecipSum: Get(r, 8), CloudMean: Get(r, 9), SnowMax: Get(r, 10));
    }

    private static async Task<SolunarRow?> LoadSolunarAsync(
        NpgsqlConnection conn, Guid spotId, DateOnly date)
    {
        const string sql = """
            SELECT moon_phase, moon_illumination,
                   major_start, major_end, major2_start, major2_end,
                   minor_start, minor_end, minor2_start, minor2_end,
                   sun_rise
            FROM solunar_daily
            WHERE spot_id = @spot AND date = @date
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@spot", spotId);
        cmd.Parameters.AddWithValue("@date", date);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync())
            return null;
        return new SolunarRow(
            MoonPhase: r.GetDecimal(0),
            MoonIllumination: r.GetDecimal(1),
            MajorStart: GetDt(r, 2), MajorEnd: GetDt(r, 3),
            Major2Start: GetDt(r, 4), Major2End: GetDt(r, 5),
            MinorStart: GetDt(r, 6), MinorEnd: GetDt(r, 7),
            Minor2Start: GetDt(r, 8), Minor2End: GetDt(r, 9),
            SunRise: GetDt(r, 10));
    }

    private static decimal? Get(NpgsqlDataReader r, int i) =>
        r.IsDBNull(i) ? null : r.GetDecimal(i);

    private static DateTime? GetDt(NpgsqlDataReader r, int i) =>
        r.IsDBNull(i) ? null : r.GetDateTime(i);

    private static async Task<float?> LoadFfAsync(
        NpgsqlConnection conn, Guid spotId, DateOnly date,
        string column, string[] sources, int maxDays)
    {
        const string sql = """
            SELECT observed_at, {0}
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
        if (!await r.ReadAsync())
            return null;
        var age = (date.ToDateTime(TimeOnly.MinValue) - r.GetDateTime(0).Date).Days;
        if (age > maxDays)
            return null;
        return r.IsDBNull(1) ? null : (float?)r.GetDecimal(1);
    }

    public sealed record WeatherRow(
        decimal? TMin, decimal? TMean, decimal? TMax,
        decimal? PressureMean, decimal? PressureAmp,
        decimal? HumidityMean, decimal? WindMean, decimal? WindMax,
        decimal? PrecipSum, decimal? CloudMean, decimal? SnowMax);

    public sealed record SolunarRow(
        decimal MoonPhase, decimal MoonIllumination,
        DateTime? MajorStart, DateTime? MajorEnd,
        DateTime? Major2Start, DateTime? Major2End,
        DateTime? MinorStart, DateTime? MinorEnd,
        DateTime? Minor2Start, DateTime? Minor2End,
        DateTime? SunRise);
}
