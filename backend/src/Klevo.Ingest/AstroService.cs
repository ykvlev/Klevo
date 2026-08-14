using CosineKitty;
using Klevo.Core.Data;

namespace Klevo.Ingest;

public static class AstroService
{
    // Россия — постоянный UTC+3 (без перехода на летнее время с 2014 г.)
    private static readonly TimeSpan LocalOffset = TimeSpan.FromHours(3);

    private static readonly TimeSpan MajorHalfWindow = TimeSpan.FromMinutes(90);
    private static readonly TimeSpan MinorHalfWindow = TimeSpan.FromMinutes(60);

    public static SolunarDay ComputeDay(Guid spotId, DateOnly date, double lat, double lon)
    {
        var obs = new Observer(lat, lon, 0);
        var dayStartUtc = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Utc) - LocalOffset;
        var noonUtc = dayStartUtc + TimeSpan.FromHours(12);
        var start = new AstroTime(dayStartUtc);

        var moonPhaseDeg = Astronomy.MoonPhase(new AstroTime(noonUtc));
        var illum = Astronomy.Illumination(Body.Moon, new AstroTime(noonUtc));

        var day = new SolunarDay
        {
            SpotId = spotId,
            Date = date,
            MoonPhase = Math.Round((decimal)(moonPhaseDeg / 360.0), 3),
            MoonIllumination = Math.Round((decimal)(illum.phase_fraction * 100), 2),
        };

        day.MoonRise = FindTime(() => Astronomy.SearchRiseSet(Body.Moon, obs, Direction.Rise, start, 1.2, 0));
        day.MoonSet = FindTime(() => Astronomy.SearchRiseSet(Body.Moon, obs, Direction.Set, start, 1.2, 0));
        day.MoonTransit = FindTime(() => Astronomy.SearchHourAngle(Body.Moon, obs, 0, start, 1).time);
        day.LowerTransit = FindTime(() => Astronomy.SearchHourAngle(Body.Moon, obs, 12, start, 1).time);
        day.SunRise = FindTime(() => Astronomy.SearchRiseSet(Body.Sun, obs, Direction.Rise, start, 1.2, 0));
        day.SunSet = FindTime(() => Astronomy.SearchRiseSet(Body.Sun, obs, Direction.Set, start, 1.2, 0));
        day.Dawn = FindTime(() => Astronomy.SearchAltitude(Body.Sun, obs, Direction.Rise, start, 1.2, -6));
        day.Dusk = FindTime(() => Astronomy.SearchAltitude(Body.Sun, obs, Direction.Set, start, 1.2, -6));

        if (day.MoonTransit is DateTime t)
        {
            day.MajorStart = t - MajorHalfWindow;
            day.MajorEnd = t + MajorHalfWindow;
        }
        if (day.LowerTransit is DateTime t2)
        {
            day.Major2Start = t2 - MajorHalfWindow;
            day.Major2End = t2 + MajorHalfWindow;
        }
        if (day.MoonRise is DateTime r)
        {
            day.MinorStart = r - MinorHalfWindow;
            day.MinorEnd = r + MinorHalfWindow;
        }
        if (day.MoonSet is DateTime s)
        {
            day.Minor2Start = s - MinorHalfWindow;
            day.Minor2End = s + MinorHalfWindow;
        }

        return day;
    }

    private static DateTime? FindTime(Func<AstroTime> search)
    {
        try
        {
            return search().ToUtcDateTime();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
