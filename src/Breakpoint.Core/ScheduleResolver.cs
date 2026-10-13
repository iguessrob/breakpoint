namespace Breakpoint.Core;

public static class ScheduleResolver
{
    public static IReadOnlyList<Window> Resolve(Schedule schedule, DateTimeOffset now)
    {
        schedule.Validate();
        if (!schedule.Enabled) return [];
        if (schedule.WindowKind=="Allowed") return AllowedWindows.Resolve(schedule,now);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(schedule.ZoneId);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var start = TimeOnly.ParseExact(schedule.Start, "HH:mm");
        var end = TimeOnly.ParseExact(schedule.End, "HH:mm");
        var result = new List<Window>();
        for (var offset = -1; offset <= 8; offset++)
        {
            var day = today.AddDays(offset);
            if (!schedule.Days.Contains((int)day.DayOfWeek)) continue;
            var startUtc = ResolveBoundary(day.ToDateTime(start), zone, false);
            var endUtc = ResolveBoundary(day.AddDays(end < start ? 1 : 0).ToDateTime(end), zone, true);
            result.Add(new Window($"schedule:{schedule.Revision}:{startUtc.UtcTicks}", startUtc, endUtc));
        }
        return result;
    }

    public static DateTimeOffset ResolveBoundary(DateTime local, TimeZoneInfo zone, bool release)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        if (zone.IsAmbiguousTime(local))
        {
            var offsets = zone.GetAmbiguousTimeOffsets(local);
            return new DateTimeOffset(local, release ? offsets.Min() : offsets.Max()).ToUniversalTime();
        }
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone));
    }

    public static IReadOnlyList<Window> Candidates(PersistedState state, DateTimeOffset now)
    {
        var windows = Resolve(state.Schedule, now).Where(w=>state.Schedule.WindowKind!="Allowed"||state.ScheduleArmedAt is not { } armed||w.Start>=armed).ToList();
        if (state.TimerStart is { } timer && state.TimerEnd is { } end)
            windows.Add(new Window($"timer:{timer.UtcTicks}", timer, end, true));
        return windows.Where(w => !state.SkippedOccurrences.Contains(w.Id)).OrderBy(w => w.Start).ToArray();
    }
}
