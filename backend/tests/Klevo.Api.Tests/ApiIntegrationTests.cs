using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Klevo.Api.Tests;

public class ApiIntegrationTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var response = await _client.GetAsync("/health");
        response.EnsureSuccessStatusCode();
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Zones_ReturnsAtLeastThreePilotZones()
    {
        var response = await _client.GetAsync("/api/zones");
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var zones = doc.RootElement.EnumerateArray().ToList();

        Assert.True(zones.Count >= 3);
        Assert.Contains(zones, z => z.GetProperty("id").GetString() == "baltic_32");
        Assert.Contains(zones, z => z.GetProperty("id").GetString() == "ladoga");
        Assert.Contains(zones, z => z.GetProperty("id").GetString() == "lenobl_vodnye_obekty");
    }

    [Fact]
    public async Task ZoneRules_SudakHasMinSize()
    {
        var response = await _client.GetAsync("/api/zones/baltic_32/rules");
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var minSizes = doc.RootElement.GetProperty("minSizes").EnumerateArray().ToList();

        var sudak = minSizes.Single(m => m.GetProperty("species").GetString() == "судак");
        Assert.Equal(40, sudak.GetProperty("minSizeCm").GetDecimal());
    }

    [Fact]
    public async Task Spots_ReturnCoordinatesInPilotRegion()
    {
        var response = await _client.GetAsync("/api/spots");
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var spots = doc.RootElement.EnumerateArray().ToList();

        Assert.True(spots.Count >= 3);
        foreach (var spot in spots)
        {
            Assert.True(spot.GetProperty("lat").TryGetDouble(out var lat));
            Assert.True(spot.GetProperty("lon").TryGetDouble(out var lon));
            Assert.InRange(lat, 58, 62);
            Assert.InRange(lon, 28, 34);
            Assert.False(string.IsNullOrEmpty(spot.GetProperty("name").GetString()));
        }
    }

    [Fact]
    public async Task PostCatch_ThenGet_ReturnsCatch()
    {
        var spotId = "a1111111-0000-4000-8000-000000000001";
        var body = JsonContent.Create(new
        {
            speciesName = "судак",
            weightKg = 2.5m,
            lengthCm = 55m,
            caughtAt = "2026-08-01T06:30:00Z",
            notes = "api-test",
        });

        var post = await _client.PostAsync($"/api/spots/{spotId}/catches", body);
        Assert.Equal(HttpStatusCode.Created, post.StatusCode);
        var createdId = Guid.Parse(post.Headers.Location!.ToString().Split('/').Last());

        try
        {
            var list = await _client.GetAsync($"/api/spots/{spotId}/catches?from=2026-08-01&to=2026-08-01");
            list.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
            var mine = doc.RootElement.EnumerateArray()
                .SingleOrDefault(c => c.GetProperty("id").GetGuid() == createdId);
            Assert.NotNull(mine);
            Assert.Equal("судак", mine.GetProperty("speciesName").GetString());
            Assert.Equal(55, mine.GetProperty("lengthCm").GetDecimal());
        }
        finally
        {
            using var conn = new NpgsqlConnection(
                "Host=localhost;Port=5432;Database=klevo;Username=postgres;Password=klevo_dev_pwd");
            conn.Open();
            using var cmd = new NpgsqlCommand("DELETE FROM catches WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("@id", createdId);
            cmd.ExecuteNonQuery();
        }
    }
}
