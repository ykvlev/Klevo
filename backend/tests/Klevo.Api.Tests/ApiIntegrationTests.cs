using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

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
}
