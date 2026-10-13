using System.Globalization;

namespace Breakpoint.Core;

public static class AllowedWindows
{
    // Resolve the blocked gaps around the chosen internet-on windows.
    public static IReadOnlyList<Window> Resolve(Schedule schedule,DateTimeOffset now)
    {
        var zone=TimeZoneInfo.FindSystemTimeZoneById(schedule.ZoneId);
        var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now,zone).DateTime);
        var on=TimeOnly.ParseExact(schedule.Start,"HH:mm");
        var off=TimeOnly.ParseExact(schedule.End,"HH:mm");
        var openings=new List<(DateTimeOffset On,DateTimeOffset Off)>();
        if(schedule.OneTimeDate is { } single)
        {
            var date=DateOnly.ParseExact(single,"yyyy-MM-dd",CultureInfo.InvariantCulture);
            var start=ScheduleResolver.ResolveBoundary(date.ToDateTime(on),zone,true);
            var endDate=date.AddDays(off<on?1:0);
            var end=ScheduleResolver.ResolveBoundary(endDate.ToDateTime(off),zone,false);
            var release=ScheduleResolver.ResolveBoundary(endDate.AddDays(off>=on?1:0).ToDateTime(on),zone,true);
            var preceding=ScheduleResolver.ResolveBoundary(endDate.AddDays(-1).ToDateTime(off),zone,false);
            return [new Window($"once-before:{schedule.Revision}:{start.UtcTicks}",preceding,start),new Window($"once:{schedule.Revision}:{end.UtcTicks}",end,release)];
        }
        for(var offset=-8;offset<=15;offset++)
        {
            var date=today.AddDays(offset);
            if(!schedule.Days.Contains((int)date.DayOfWeek))continue;
            var start=ScheduleResolver.ResolveBoundary(date.ToDateTime(on),zone,true);
            var end=ScheduleResolver.ResolveBoundary(date.AddDays(off<on?1:0).ToDateTime(off),zone,false);
            // A DST transition may erase a short interval. It must not create an inverted restriction.
            openings.Add((start,end<start?start:end));
        }
        var blocks=new List<Window>();
        for(var i=0;i<openings.Count-1;i++)
        {
            var from=openings[i].Off;var until=openings[i+1].On;
            if(until>from)blocks.Add(new Window($"allowed:{schedule.Revision}:{from.UtcTicks}",from,until));
        }
        return blocks;
    }
}
