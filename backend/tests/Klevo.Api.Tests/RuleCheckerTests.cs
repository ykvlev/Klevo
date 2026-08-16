using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Klevo.Api.Tests;

public class RuleCheckerTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Conn =
        "Host=localhost;Port=5432;Database=klevo;Username=postgres;Password=klevo_dev_pwd";

    private const string BalticSpot = "a1111111-0000-4000-8000-000000000004"; // Финский залив, baltic_32
    private const string LenoblSpot = "a1111111-0000-4000-8000-000000000001"; // Лосево, lenobl_vodnye_obekty

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task AllowedCatch_ReturnsAllowed()
    {
        var root = await PostCheckAsync(new
        {
            spotId = BalticSpot,
            speciesName = "судак",
            weightKg = 2.5m,
            lengthCm = 55m,
            caughtAt = "2026-08-10T06:00:00Z",
        });

        Assert.True(root.GetProperty("allowed").GetBoolean());
        Assert.Equal("судак", root.GetProperty("species").GetProperty("nameRu").GetString());
        Assert.Equal("baltic_32", root.GetProperty("zone").GetProperty("id").GetString());
        foreach (var type in new[] { "banned_species", "season_ban", "min_size", "daily_limit" })
            Assert.True(FindCheck(root, type).GetProperty("ok").GetBoolean(), type);
    }

    [Fact]
    public async Task BannedSpecies_ResolvedByAlias_ReturnsNotAllowed()
    {
        var root = await PostCheckAsync(new
        {
            spotId = BalticSpot,
            speciesName = "семга", // алиас «лосось атлантический», запрещён в baltic_32
            caughtAt = "2026-08-10T06:00:00Z",
        });

        Assert.False(root.GetProperty("allowed").GetBoolean());
        Assert.False(FindCheck(root, "banned_species").GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task SeasonBan_FiresByMonthDay_RegardlessOfYear()
    {
        var root = await PostCheckAsync(new
        {
            spotId = BalticSpot,
            speciesName = "судак",
            lengthCm = 55m,
            caughtAt = "2026-05-25T06:00:00Z", // судак в baltic_32 запрещён 20.05–30.06
        });

        Assert.False(root.GetProperty("allowed").GetBoolean());
        var season = FindCheck(root, "season_ban");
        Assert.False(season.GetProperty("ok").GetBoolean());
        Assert.Contains("20.05", season.GetProperty("message").GetString());
    }

    [Fact]
    public async Task SeasonBan_NotFired_OutsidePeriod()
    {
        var root = await PostCheckAsync(new
        {
            spotId = BalticSpot,
            speciesName = "судак",
            lengthCm = 55m,
            caughtAt = "2026-07-15T06:00:00Z",
        });

        Assert.True(FindCheck(root, "season_ban").GetProperty("ok").GetBoolean());
        Assert.True(root.GetProperty("allowed").GetBoolean());
    }

    [Fact]
    public async Task MinSize_UnderLimit_ReturnsNotAllowed()
    {
        var root = await PostCheckAsync(new
        {
            spotId = BalticSpot,
            speciesName = "судак",
            lengthCm = 35m, // минимальный размер 40 см
            caughtAt = "2026-08-10T06:00:00Z",
        });

        Assert.False(root.GetProperty("allowed").GetBoolean());
        var size = FindCheck(root, "min_size");
        Assert.False(size.GetProperty("ok").GetBoolean());
        Assert.Contains("40", size.GetProperty("message").GetString());
    }

    [Fact]
    public async Task MinSize_MissingLength_IsOk()
    {
        var root = await PostCheckAsync(new
        {
            spotId = BalticSpot,
            speciesName = "судак",
            caughtAt = "2026-08-10T06:00:00Z",
        });

        Assert.True(FindCheck(root, "min_size").GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task DailyLimit_ByCount_Exceeded_ReturnsNotAllowed()
    {
        var date = "2026-08-10";
        var speciesId = await GetSpeciesIdAsync("судак");
        var ids = new List<Guid>();
        try
        {
            for (var i = 0; i < 5; i++)
                ids.Add(InsertCatch(BalticSpot, speciesId, "судак", null, $"{date}T0{i + 1}:00:00Z"));

            var root = await PostCheckAsync(new
            {
                spotId = BalticSpot,
                speciesId,
                lengthCm = 55m,
                caughtAt = $"{date}T06:00:00Z",
            });

            Assert.False(root.GetProperty("allowed").GetBoolean());
            var limit = FindCheck(root, "daily_limit");
            Assert.False(limit.GetProperty("ok").GetBoolean());
            Assert.Contains("5", limit.GetProperty("message").GetString());
        }
        finally
        {
            ids.ForEach(DeleteCatch);
        }
    }

    [Fact]
    public async Task DailyLimit_ByWeight_Exceeded_ReturnsNotAllowed()
    {
        var date = "2026-08-11";
        var speciesId = await GetSpeciesIdAsync("щука");
        var ids = new List<Guid>();
        try
        {
            ids.Add(InsertCatch(BalticSpot, speciesId, "щука", 3m, $"{date}T06:00:00Z"));
            ids.Add(InsertCatch(BalticSpot, speciesId, "щука", 2m, $"{date}T07:00:00Z")); // итого 5 кг из 5

            var root = await PostCheckAsync(new
            {
                spotId = BalticSpot,
                speciesId,
                weightKg = 1m,
                lengthCm = 60m,
                caughtAt = $"{date}T08:00:00Z",
            });

            Assert.False(root.GetProperty("allowed").GetBoolean());
            Assert.False(FindCheck(root, "daily_limit").GetProperty("ok").GetBoolean());
        }
        finally
        {
            ids.ForEach(DeleteCatch);
        }
    }

    [Fact]
    public async Task DailyLimit_NotExceeded_IsAllowed()
    {
        var date = "2026-08-12";
        var speciesId = await GetSpeciesIdAsync("судак");
        var ids = new List<Guid>();
        try
        {
            ids.Add(InsertCatch(BalticSpot, speciesId, "судак", 2m, $"{date}T06:00:00Z"));

            var root = await PostCheckAsync(new
            {
                spotId = BalticSpot,
                speciesId,
                weightKg = 1m,
                lengthCm = 55m,
                caughtAt = $"{date}T08:00:00Z",
            });

            Assert.True(FindCheck(root, "daily_limit").GetProperty("ok").GetBoolean());
            Assert.True(root.GetProperty("allowed").GetBoolean());
        }
        finally
        {
            ids.ForEach(DeleteCatch);
        }
    }

    [Fact]
    public async Task UnknownSpecies_ReturnsBadRequest()
    {
        var root = await PostCheckAsync(new
        {
            spotId = BalticSpot,
            speciesName = "неведомая рыба",
            caughtAt = "2026-08-10T06:00:00Z",
        }, HttpStatusCode.BadRequest);

        Assert.False(root.TryGetProperty("allowed", out _));
        Assert.NotNull(root.GetProperty("error").GetString());
    }

    [Fact]
    public async Task AreaRestrictedBans_AreInformationalOnly()
    {
        // В lenobl_vodnye_obekty все сезонные запреты щуки привязаны к участкам — проверить без координат нельзя,
        // поэтому они не должны блокировать, а должны вернуться отдельной информационной проверкой.
        var root = await PostCheckAsync(new
        {
            spotId = LenoblSpot,
            speciesName = "щука",
            lengthCm = 50m,
            caughtAt = "2026-05-10T06:00:00Z",
        });

        var area = FindCheck(root, "area_ban");
        Assert.True(area.GetProperty("ok").GetBoolean());
        Assert.True(FindCheck(root, "season_ban").GetProperty("ok").GetBoolean());
        Assert.True(root.GetProperty("allowed").GetBoolean());
    }

    [Fact]
    public async Task ResolvesSpeciesById()
    {
        var speciesId = await GetSpeciesIdAsync("судак");

        var root = await PostCheckAsync(new
        {
            spotId = BalticSpot,
            speciesId,
            lengthCm = 55m,
            caughtAt = "2026-08-10T06:00:00Z",
        });

        Assert.True(root.GetProperty("allowed").GetBoolean());
        Assert.Equal("судак", root.GetProperty("species").GetProperty("nameRu").GetString());
    }

    private async Task<JsonElement> PostCheckAsync(object body, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await _client.PostAsync("/api/rule-checks", JsonContent.Create(body));
        Assert.Equal(expected, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static JsonElement FindCheck(JsonElement root, string type)
        => root.GetProperty("checks").EnumerateArray()
            .Single(c => c.GetProperty("type").GetString() == type);

    private async Task<Guid> GetSpeciesIdAsync(string nameRu)
    {
        var response = await _client.GetAsync("/api/species");
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.EnumerateArray()
            .Single(s => s.GetProperty("nameRu").GetString() == nameRu)
            .GetProperty("id").GetGuid();
    }

    private static Guid InsertCatch(string spotId, Guid? speciesId, string speciesName, decimal? weightKg, string caughtAt)
    {
        using var conn = new NpgsqlConnection(Conn);
        conn.Open();
        var id = Guid.NewGuid();
        using var cmd = new NpgsqlCommand(
            "INSERT INTO catches (id, spot_id, species_id, species_name, weight_kg, caught_at, created_at) " +
            "VALUES (@id, @spot, @sp, @name, @w, @caught, @created)", conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("spot", Guid.Parse(spotId));
        cmd.Parameters.AddWithValue("sp", (object?)speciesId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("name", speciesName);
        cmd.Parameters.AddWithValue("w", (object?)weightKg ?? DBNull.Value);
        cmd.Parameters.AddWithValue("caught", DateTime.Parse(caughtAt).ToUniversalTime());
        cmd.Parameters.AddWithValue("created", DateTime.UtcNow);
        cmd.ExecuteNonQuery();
        return id;
    }

    private static void DeleteCatch(Guid id)
    {
        using var conn = new NpgsqlConnection(Conn);
        conn.Open();
        using var cmd = new NpgsqlCommand("DELETE FROM catches WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.ExecuteNonQuery();
    }
}
