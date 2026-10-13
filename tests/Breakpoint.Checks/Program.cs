using System.Buffers.Binary;
using Breakpoint.Core;

var passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAILED: " + name); passed++; Console.WriteLine("PASS " + name); }
var schedule = new Schedule { Enabled = true, Start = "22:00", End = "06:00", ZoneId = "UTC" };
var midnight = new DateTimeOffset(2026, 10, 14, 0, 0, 0, TimeSpan.Zero);
var windows = ScheduleResolver.Resolve(schedule, midnight);
var active = windows.Single(w => w.Start <= midnight && midnight < w.End);
Check(active.Start == midnight.AddHours(-2) && active.End == midnight.AddHours(6), "overnight Start–End resolution");
Check(windows.Any(w => w.Start == active.Start) && !windows.Any(w => w.Start <= active.End && active.End < w.End && w.Id == active.Id), "release boundary is exclusive");
var sameDay = ScheduleResolver.Resolve(schedule with { Start = "08:00", End = "17:00" }, midnight).First(w => w.Start > midnight);
Check(sameDay.End - sameDay.Start == TimeSpan.FromHours(9), "same-day Start–End schedule");
Check(ScheduleResolver.Resolve(schedule with { Enabled = false }, midnight).Count == 0, "disabled schedule has no occurrences");
var monday = schedule with { Days = [1] };
Check(ScheduleResolver.Resolve(monday, midnight).All(w => w.Start.DayOfWeek == DayOfWeek.Monday), "selected weekdays refer to Start day");
foreach (var invalid in new[] { schedule with { Start = "bad" }, schedule with { End = "22:00" }, schedule with { Days = [] }, schedule with { Days = [7] } })
{
    var rejected = false; try { invalid.Validate(); } catch (ArgumentException) { rejected = true; }
    Check(rejected, "invalid schedule rejected");
}
var state = new PersistedState { Schedule = schedule, TimerStart = midnight.AddMinutes(20), TimerEnd = midnight.AddHours(8) };
Check(ScheduleResolver.Candidates(state, midnight).First(w => w.Start > midnight).Manual, "manual timer and recurring schedule coexist; earliest wins");
state.SkippedOccurrences.Add(active.Id);
Check(!ScheduleResolver.Candidates(state, midnight).Any(w => w.Id == active.Id), "skip affects one occurrence");
Check(ScheduleResolver.Candidates(state, midnight).Any(w => !w.Manual && w.Start > midnight), "skip preserves future schedule");
var eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
var fold = new DateTime(2026, 11, 1, 1, 30, 0);
Check(ScheduleResolver.ResolveBoundary(fold, eastern, true) - ScheduleResolver.ResolveBoundary(fold, eastern, false) == TimeSpan.FromHours(1), "DST fold uses earlier Start/later End");
var gap = ScheduleResolver.ResolveBoundary(new DateTime(2026, 3, 8, 2, 30, 0), eastern, false);
Check(TimeZoneInfo.ConvertTime(gap, eastern).Hour == 3 && TimeZoneInfo.ConvertTime(gap, eastern).Minute == 0, "DST gap advances to first valid instant");
var allowed=schedule with { Start="06:00",End="22:00",WindowKind="Allowed" };
var allowedBlocks=ScheduleResolver.Resolve(allowed,midnight);
Check(allowedBlocks.Any(w=>w.Start==midnight.AddHours(-2)&&w.End==midnight.AddHours(6)),"End blocks until next Start restores internet");
Check(!allowedBlocks.Any(w=>w.Start<=midnight.AddHours(6)&&midnight.AddHours(6)<w.End),"internet-on Start is inclusive");
Check(allowedBlocks.Any(w=>w.Start<=midnight.AddHours(22)&&midnight.AddHours(22)<w.End),"internet-off End is inclusive");
var overnightAllowed=ScheduleResolver.Resolve(allowed with { Start="22:00",End="06:00" },midnight);
Check(!overnightAllowed.Any(w=>w.Start<=midnight&&midnight<w.End),"overnight internet-on window spans midnight");
var weeklyBlocks=ScheduleResolver.Resolve(allowed with { Days=[1,2,3,4,5] },midnight);
Check(weeklyBlocks.Any(w=>w.End-w.Start==TimeSpan.FromHours(56)),"unselected weekend remains restricted until Monday Start");
var once=ScheduleResolver.Resolve(allowed with { OneTimeDate="2026-10-14" },midnight);
Check(once.Count==2&&once.Last().End==midnight.AddDays(1).AddHours(6),"once releases at the following Start and has no future repeat");
var owner = OwnerAuthentication.Create("246810");
Check(OwnerAuthentication.VerifyPin(owner.Secrets, "246810"), "correct PIN accepted");
Check(!OwnerAuthentication.VerifyPin(owner.Secrets, "123456"), "wrong PIN rejected");
Check(OwnerAuthentication.VerifyRecovery(owner.Secrets, owner.RecoveryCode.ToLowerInvariant()), "private recovery code accepted");
Check(!OwnerAuthentication.VerifyRecovery(owner.Secrets, "universal-backdoor"), "unknown recovery rejected");
Check(!owner.Secrets.PinHash.Contains("246810") && owner.RecoveryCode.Length == 40, "salted hash and high-entropy recovery");
using var stream = new MemoryStream();
await Protocol.WriteAsync(stream, new Request { Action = "Skip", OccurrenceId = "test" }, default);
stream.Position = 0;
Check((await Protocol.ReadAsync<Request>(stream, default)).OccurrenceId == "test", "bounded protocol round trip");
using var oversized = new MemoryStream();
var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, Protocol.MaxBytes + 1); oversized.Write(header); oversized.Position = 0;
var tooLarge = false; try { await Protocol.ReadAsync<Request>(oversized, default); } catch (InvalidDataException) { tooLarge = true; }
Check(tooLarge, "oversized IPC rejected before allocation");
passed += await EngineChecks.RunAsync();
Console.WriteLine($"{passed} checks passed. No OS enforcement executed.");
