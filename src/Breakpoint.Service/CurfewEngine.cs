using System.Diagnostics;
using Breakpoint.Core;

namespace Breakpoint.Service;

public sealed class CurfewEngine
{
    private readonly StateStore store;
    private readonly IEnforcement enforcement;
    private readonly PersistedState state;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, Response> results = [];
    private Window? active;
    private string network = "Unknown", error = "";
    private long sequence;
    private readonly TimeProvider time;
    private DateTimeOffset clock;
    private long clockStamp, deadlineStamp, lastTickStamp;
    private DateTimeOffset? networkRetryAt, shutdownRetryAt;
    private DateTimeOffset networkCheckAt;
    private int networkAttempts, shutdownAttempts;
    private bool initialized;

    public CurfewEngine(StateStore store, IEnforcement enforcement, TimeProvider? timeProvider = null)
    {
        this.store = store; this.enforcement = enforcement; state = store.Load();
        time = timeProvider ?? TimeProvider.System; clock = time.GetUtcNow(); clockStamp = time.GetTimestamp(); lastTickStamp = clockStamp;
        if (state.ShutdownJournal == "Requesting") state.ShutdownJournal = "Unknown";
    }

    private DateTimeOffset Now()
    {
        var stamp = time.GetTimestamp();
        var elapsed = time.GetElapsedTime(clockStamp, stamp);
        var civil = time.GetUtcNow();
        clock = civil > clock + elapsed ? civil : clock + elapsed;
        clockStamp = stamp;
        return clock;
    }

    public async Task TickAsync()
    {
        await gate.WaitAsync();
        try { Tick(Now()); }
        catch (Exception e) { error = "Enforcement needs attention: " + e.Message; store.Log("engine-error", e.GetType().Name); }
        finally { gate.Release(); }
    }

    private void Tick(DateTimeOffset now)
    {
        var stamp = time.GetTimestamp();
        var resumed = time.GetElapsedTime(lastTickStamp, stamp).TotalSeconds > 10;
        lastTickStamp = stamp;
        if (resumed && active is not null && now < active.End && state.ShutdownJournal == "Countdown")
        {
            state.ShutdownAt = now.AddSeconds(60); deadlineStamp = stamp; store.Save(state);
        }
        if(state.Schedule.Enabled && state.Schedule.OneTimeDate is not null && ScheduleResolver.Resolve(state.Schedule,now).All(w=>w.End<=now))
        {state.Schedule.Enabled=false;state.Schedule.Revision++;store.Save(state);}
        var candidates = ScheduleResolver.Candidates(state, now);
        var current = candidates.FirstOrDefault(w => w.Start <= now && now < w.End);
        if (active is not null && now < active.End && !state.SkippedOccurrences.Contains(active.Id)) current = active;
        if (current is null)
        {
            if (!initialized || active is not null || network is "Failed restoration" || state.ActiveId is not null)
            {
                if (network != "Failed restoration" || (networkAttempts < 4 && networkRetryAt is { } removeAt && now >= removeAt))
                {
                    TryRemove(now);
                    if (network is not "Failed restoration")
                    {
                        active = null; state.ActiveId = null; state.ShutdownAt = null;
                        state.ShutdownJournal = "None"; store.Save(state);
                    }
                }
            }
            initialized = true;
            if (state.TimerEnd <= now) { state.TimerStart = null; state.TimerEnd = null; store.Save(state); }
            return;
        }
        if (!initialized || active?.Id != current.Id)
        {
            var sameOccurrence = state.ActiveId == current.Id || (active is not null && state.ShutdownJournal is "Accepted" or "Unknown" or "Blocked or delayed");
            active = current; state.ActiveId = current.Id; networkAttempts = 0; shutdownAttempts = 0; networkRetryAt = null; shutdownRetryAt = null;
            // A restart inside curfew gives a fresh save-work minute unless a request may already be pending.
            if (!sameOccurrence || state.ShutdownJournal is not ("Accepted" or "Unknown" or "Blocked or delayed"))
            {
                state.ShutdownAt = now.AddSeconds(60);
                deadlineStamp = time.GetTimestamp();
                state.ShutdownJournal = "Countdown";
            }
            store.Save(state); TryApply(now); initialized = true;
            store.Log("curfew-start", current.Id);
        }
        else if (network == "Failed restriction" && networkRetryAt is { } at && now >= at && networkAttempts < 4) TryApply(now);
        else if (network != "Failed restriction" && now >= networkCheckAt) TryApply(now);
        if (state.ShutdownJournal == "Countdown" && (now >= state.ShutdownAt || time.GetElapsedTime(deadlineStamp).TotalSeconds >= 60))
            Shutdown(now);
        else if (state.ShutdownJournal == "Failed" && shutdownRetryAt is { } retry && now >= retry && shutdownAttempts < 4)
            Shutdown(now);
        if (state.ShutdownJournal == "Accepted" && state.ShutdownAt is { } accepted && now >= accepted.AddSeconds(30))
        {
            state.ShutdownJournal = "Blocked or delayed"; store.Save(state);
        }
    }

    private void TryApply(DateTimeOffset now)
    {
        networkAttempts++;
        try { network = enforcement.ApplyNetwork(); error = ""; networkRetryAt = null; networkCheckAt = now.AddSeconds(30); networkAttempts = 0; }
        catch (Exception e)
        {
            network = "Failed restriction"; error = e.Message;
            networkRetryAt = networkAttempts < 4 ? now.AddSeconds(new[] { 1, 2, 7 }[networkAttempts - 1]) : null;
            store.Log("network-apply-failed", e.GetType().Name);
        }
    }
    private void TryRemove(DateTimeOffset now)
    {
        networkAttempts++;
        try { network = enforcement.RemoveNetwork(); error = ""; networkRetryAt = null; networkAttempts = 0; }
        catch (Exception e)
        {
            network = "Failed restoration"; error = e.Message;
            networkRetryAt = networkAttempts < 4 ? now.AddSeconds(new[] { 1, 2, 7 }[networkAttempts - 1]) : null;
            store.Log("network-remove-failed", e.GetType().Name);
        }
    }
    private void Shutdown(DateTimeOffset now)
    {
        if (active is null || now >= active.End) return;
        state.ShutdownJournal = "Requesting"; store.Save(state);
        shutdownAttempts++;
        try
        {
            enforcement.RequestShutdown(); state.ShutdownJournal = enforcement.Preview ? "Preview complete" : "Accepted";
            state.ShutdownAt = now; error = ""; shutdownRetryAt = null;
        }
        catch (Exception e)
        {
            state.ShutdownJournal = "Failed"; error = "Shutdown failed: " + e.Message;
            shutdownRetryAt = shutdownAttempts < 4 ? now.AddSeconds(new[] { 5, 10, 15 }[shutdownAttempts - 1]) : null;
        }
        store.Save(state); store.Log("shutdown", state.ShutdownJournal);
    }

    private Status Snapshot(DateTimeOffset now)
    {
        var next = ScheduleResolver.Candidates(state, now).FirstOrDefault(w => w.Start > now);
        var phase = state.Owner is null ? "Set up owner PIN" : active is not null ? "Curfew" : next is not null ? "Scheduled" : "Curfew off";
        if (next is not null && next.Start - now <= TimeSpan.FromMinutes(15) && active is null) phase = "Warning";
        return new Status { Preview = enforcement.Preview, Configured = state.Owner is not null,
            Schedule = state.Schedule with { Days = state.Schedule.Days.ToArray() }, Phase = phase, Network = network,
            Shutdown = state.ShutdownJournal, Now = now, Active = active, Next = next, ShutdownAt = state.ShutdownAt,
            TimerStart = state.TimerStart, LockedUntil = state.LockedUntil,
            Error = string.IsNullOrWhiteSpace(error) ? null : error, Sequence = ++sequence };
    }

    public async Task<Response> HandleAsync(Request request, bool administrator)
    {
        await gate.WaitAsync();
        try
        {
            var now = Now();
            Tick(now);
            if (request.Action == "Status") return new(true, "", Snapshot(now));
            if (results.TryGetValue(request.Id, out var previous)) return previous with { Status = Snapshot(now), RecoveryCode = null };
            Response response;
            try { response = Handle(request, administrator, now); }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or
                System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException)
            { response = new(false, e.Message, Snapshot(now)); }
            if (results.Count > 256) results.Clear();
            // Never retain PIN/recovery request material; recovery is returned once.
            results[request.Id] = response with { RecoveryCode = null };
            return response;
        }
        finally { gate.Release(); }
    }

    private Response Handle(Request request, bool administrator, DateTimeOffset now)
    {
        if (request.Action == "Setup")
        {
            if (state.Owner is not null) return new(false, "Owner PIN is already configured.", Snapshot(now));
            if (!administrator && !enforcement.Preview) return new(false, "First setup requires Windows administrator approval.", Snapshot(now));
            var created = OwnerAuthentication.Create(request.NewPin ?? "");
            state.Owner = created.Secrets; store.Save(state);
            return new(true, "Store your recovery code privately. It is shown only once.", Snapshot(now), created.RecoveryCode);
        }
        if (state.Owner is null) return new(false, "Set up the owner PIN first.", Snapshot(now));
        if (state.LockedUntil is { } locked && now < locked)
            return new(false, $"Too many attempts. Try again at {locked.ToLocalTime():t}.", Snapshot(now));
        var correct = request.Action == "RecoverPin" ? OwnerAuthentication.VerifyRecovery(state.Owner, request.RecoveryCode)
            : OwnerAuthentication.VerifyPin(state.Owner, request.Pin);
        if (!correct)
        {
            state.FailedAttempts++;
            if (state.FailedAttempts >= 5) state.LockedUntil = now.AddSeconds(Math.Min(900, 30 * Math.Pow(2, Math.Min(5, state.FailedAttempts - 5))));
            store.Save(state); store.Log("authentication-denied", request.Action);
            return new(false, "Incorrect PIN or recovery code.", Snapshot(now));
        }
        state.FailedAttempts = 0; state.LockedUntil = null;
        if (request.Action is "SaveSchedule" or "StartTimer" or "CancelTimer" && request.Revision != state.Schedule.Revision)
            return new(false, "Settings changed. Refresh and try again.", Snapshot(now));
        string? recoveryCode = null;
        var shutdownMayBePending=state.ShutdownJournal is "Accepted" or "Unknown" or "Blocked or delayed" or "Requesting";
        switch (request.Action)
        {
            case "SaveSchedule":
                var schedule = request.Schedule ?? throw new ArgumentException("Schedule is required.");
                schedule.Validate();
                if (schedule.Enabled) enforcement.CheckReady();
                state.SchemaVersion=2;
                if(request.Schedule.WindowKind=="Allowed"){state.TimerStart=null;state.TimerEnd=null;}
                state.Schedule = schedule with { Revision = state.Schedule.Revision + 1 };
                state.ScheduleArmedAt=schedule.Enabled&&schedule.WindowKind=="Allowed"?now:null;
                active = null; state.ActiveId = null; state.ShutdownAt = null; state.ShutdownJournal = "None";
                networkAttempts=0;networkRetryAt=now;shutdownRetryAt=null;initialized = false; store.Save(state); Tick(now);
                break;
            case "StartTimer":
                if (request.Minutes is < 1 or > 1440) throw new ArgumentException("Timer must be between 1 and 1,440 minutes.");
                enforcement.CheckReady(); state.TimerStart = now.AddMinutes(request.Minutes); state.TimerEnd = state.TimerStart.Value.AddHours(8);
                break;
            case "CancelTimer":
                if (active?.Manual == true) throw new InvalidOperationException("Use Skip this curfew to end an active timer.");
                state.TimerStart = null; state.TimerEnd = null;
                break;
            case "Skip":
                if (request.OccurrenceId is null || request.OccurrenceId != (active?.Id ?? ScheduleResolver.Candidates(state, now).FirstOrDefault(w => w.Start > now)?.Id))
                    throw new InvalidOperationException("The curfew changed. Refresh before skipping.");
                var skip = ScheduleResolver.Candidates(state, now).Where(w => w.Start <= now && now < w.End).Select(w => w.Id).ToList();
                if (skip.Count == 0) skip.Add(request.OccurrenceId);
                state.SkippedOccurrences.AddRange(skip);
                if (active?.Manual == true) { state.TimerStart = null; state.TimerEnd = null; }
                active = null; state.ActiveId = null; state.ShutdownAt = null; state.ShutdownJournal = "None";
                networkAttempts = 0; networkRetryAt = now; shutdownRetryAt = null; initialized = false; store.Save(state); Tick(now);
                break;
            case "ChangePin": case "RecoverPin":
                var created = OwnerAuthentication.Create(request.NewPin ?? "");
                state.Owner = created.Secrets; recoveryCode = created.RecoveryCode;
                break;
            case "AuthorizeQuit": break;
            case "PreviewWarning":
                if (!enforcement.Preview) throw new InvalidOperationException("Preview warning is available only in preview mode.");
                state.TimerStart = now; state.TimerEnd = now.AddMinutes(10); initialized = false; state.ShutdownJournal = "None";
                store.Save(state); Tick(now);
                break;
            default: throw new ArgumentException("Unsupported action.");
        }
        state.SkippedOccurrences = state.SkippedOccurrences.TakeLast(100).ToList();
        store.Save(state); store.Log("owner-command", request.Action);
        if((request.Action is "Skip" or "SaveSchedule") && network=="Failed restoration")
            return new(false,"Override saved, but internet restoration failed: "+error+". Try saving Curfew off again.",Snapshot(now),recoveryCode);
        var message=request.Action=="Skip"?"Internet restored; this curfew is skipped.":request.Action=="SaveSchedule"?"Curfew saved.":"Saved.";
        if(shutdownMayBePending&&(request.Action is "Skip" or "SaveSchedule"))message+=" Windows may still complete an earlier shutdown request.";
        return new(true,message,Snapshot(now),recoveryCode);
    }
}
