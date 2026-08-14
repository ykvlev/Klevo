using System.Globalization;
using System.Text.Json;

namespace Klevo.Ingest;

public sealed class OpenMeteoClient(HttpClient http)
{
    private const string ForecastUrl = "https://api.open-meteo.com/v1/forecast";
    private const string ArchiveUrl = "https://archive-api.open-meteo.com/v1/archive";

    private const string HourlyFields =
        "temperature_2m,relative_humidity_2m,pressure_msl,wind_speed_10m,wind_direction_10m," +
        "wind_gusts_10m,precipitation,cloud_cover,snow_depth";

    public async Task<IReadOnlyList<HourlyWeather>> GetForecastAsync(
        double lat, double lon, DateOnly start, DateOnly end, CancellationToken ct)
    {
        var query = $"?latitude={lat.ToString(CultureInfo.InvariantCulture)}" +
                    $"&longitude={lon.ToString(CultureInfo.InvariantCulture)}" +
                    $"&hourly={HourlyFields}&timezone=UTC" +
                    $"&start_date={start:yyyy-MM-dd}&end_date={end:yyyy-MM-dd}";
        return await FetchAsync(ForecastUrl + query, ct);
    }

    public async Task<IReadOnlyList<HourlyWeather>> GetArchiveAsync(
        double lat, double lon, DateOnly start, DateOnly end, CancellationToken ct)
    {
        var query = $"?latitude={lat.ToString(CultureInfo.InvariantCulture)}" +
                    $"&longitude={lon.ToString(CultureInfo.InvariantCulture)}" +
                    $"&hourly={HourlyFields}&timezone=UTC" +
                    $"&start_date={start:yyyy-MM-dd}&end_date={end:yyyy-MM-dd}";
        return await FetchAsync(ArchiveUrl + query, ct);
    }

    private async Task<IReadOnlyList<HourlyWeather>> FetchAsync(string url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        var hourly = doc.RootElement.GetProperty("hourly");

        var times = hourly.GetProperty("time").EnumerateArray()
            .Select(v => DateTime.ParseExact(v.GetString()!, "yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal))
            .ToArray();

        var rows = new HourlyWeather[times.Length];
        for (var i = 0; i < times.Length; i++)
        {
            rows[i] = new HourlyWeather
            {
                TimeUtc = times[i],
                Temperature2m = ReadDecimal(hourly, "temperature_2m", i),
                PressureMsl = ReadDecimal(hourly, "pressure_msl", i),
                Humidity2m = ReadDecimal(hourly, "relative_humidity_2m", i),
                WindSpeed10m = ReadDecimal(hourly, "wind_speed_10m", i),
                WindDir10m = ReadShort(hourly, "wind_direction_10m", i),
                WindGusts10m = ReadDecimal(hourly, "wind_gusts_10m", i),
                Precip = ReadDecimal(hourly, "precipitation", i),
                CloudCover = ReadShort(hourly, "cloud_cover", i),
                SnowDepth = ReadDecimal(hourly, "snow_depth", i),
            };
        }
        return rows;
    }

    private static decimal? ReadDecimal(JsonElement hourly, string key, int index)
    {
        if (!hourly.TryGetProperty(key, out var arr) || index >= arr.GetArrayLength())
            return null;
        var el = arr[index];
        return el.ValueKind == JsonValueKind.Number ? el.GetDecimal() : null;
    }

    private static short? ReadShort(JsonElement hourly, string key, int index)
    {
        var v = ReadDecimal(hourly, key, index);
        return v.HasValue ? (short)v.Value : null;
    }
}

public sealed class HourlyWeather
{
    public DateTime TimeUtc { get; set; }
    public decimal? Temperature2m { get; set; }
    public decimal? PressureMsl { get; set; }
    public decimal? Humidity2m { get; set; }
    public decimal? WindSpeed10m { get; set; }
    public short? WindDir10m { get; set; }
    public decimal? WindGusts10m { get; set; }
    public decimal? Precip { get; set; }
    public short? CloudCover { get; set; }
    public decimal? SnowDepth { get; set; }
}
