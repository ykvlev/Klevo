using Klevo.Ingest;

namespace Klevo.Api.Tests;

public class AstroServiceTests
{
    private static readonly Guid SpotId = Guid.NewGuid();

    [Fact]
    public void ComputeDay_ReturnsPlausibleValues()
    {
        var day = AstroService.ComputeDay(SpotId, new DateOnly(2026, 8, 16), 60.6749, 30.2296);

        Assert.InRange(day.MoonPhase, 0m, 1m);
        Assert.InRange(day.MoonIllumination, 0m, 100m);
        Assert.NotNull(day.SunRise);
        Assert.NotNull(day.SunSet);
        Assert.True(day.SunRise < day.SunSet);
        Assert.NotNull(day.MoonRise);
        Assert.NotNull(day.MoonSet);
        Assert.NotNull(day.MoonTransit);
        Assert.NotNull(day.LowerTransit);
    }

    [Fact]
    public void LowerTransit_IsHalfLunarDayFromUpperTransit()
    {
        var day = AstroService.ComputeDay(SpotId, new DateOnly(2026, 8, 16), 60.6749, 30.2296);

        Assert.NotNull(day.MoonTransit);
        Assert.NotNull(day.LowerTransit);

        var diff = (day.MoonTransit - day.LowerTransit)!.Value.Duration();
        Assert.InRange(diff.TotalMinutes, 12 * 60 + 10, 12 * 60 + 40);
    }

    [Fact]
    public void MajorWindow_IsCenteredOnTransit()
    {
        var day = AstroService.ComputeDay(SpotId, new DateOnly(2026, 8, 16), 60.6749, 30.2296);

        Assert.NotNull(day.MoonTransit);
        Assert.NotNull(day.MajorStart);
        Assert.NotNull(day.MajorEnd);
        Assert.Equal(day.MoonTransit!.Value - TimeSpan.FromMinutes(90), day.MajorStart);
        Assert.Equal(day.MoonTransit!.Value + TimeSpan.FromMinutes(90), day.MajorEnd);
    }

    [Fact]
    public void MinorWindows_AreCenteredOnMoonRiseAndSet()
    {
        var day = AstroService.ComputeDay(SpotId, new DateOnly(2026, 8, 16), 60.6749, 30.2296);

        Assert.NotNull(day.MoonRise);
        Assert.NotNull(day.MoonSet);
        Assert.Equal(day.MoonRise!.Value - TimeSpan.FromMinutes(60), day.MinorStart);
        Assert.Equal(day.MoonRise!.Value + TimeSpan.FromMinutes(60), day.MinorEnd);
        Assert.Equal(day.MoonSet!.Value - TimeSpan.FromMinutes(60), day.Minor2Start);
        Assert.Equal(day.MoonSet!.Value + TimeSpan.FromMinutes(60), day.Minor2End);
    }
}
