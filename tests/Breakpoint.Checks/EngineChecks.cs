using System.Diagnostics;
using System.IO.Pipes;
using Breakpoint.Core;
using Breakpoint.Service;

public static class EngineChecks
{
    public static async Task<int> RunAsync()
    {
        var passed = 0;
        void Check(bool condition, string name) { if (!condition) throw new Exception("FAILED: " + name); passed++; Console.WriteLine("PASS " + name); }
        var lookup=typeof(WindowsEnforcement).GetMethod("FindRule",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!;
        foreach(var missing in new Exception[]{new FileNotFoundException("Absent"),new System.Runtime.InteropServices.COMException("Absent",unchecked((int)0x80070002))})
            Check(lookup.Invoke(null,new object[]{new FirewallLookupFixture(missing),"missing"}) is null,"absent firewall rule is handled for "+missing.GetType().Name);
        var lookupDenied=false;try{lookup.Invoke(null,new object[]{new FirewallLookupFixture(new System.Runtime.InteropServices.COMException("Denied",unchecked((int)0x80070005))),"missing"});}
        catch(System.Reflection.TargetInvocationException error){lookupDenied=error.InnerException?.HResult==unchecked((int)0x80070005);}
        Check(lookupDenied,"firewall lookup preserves permission errors");
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../work/check-data", Guid.NewGuid().ToString("N")));
        var retryRoot=Path.Combine(root,"replace-lock");var retryStore=new StateStore(retryRoot,true);retryStore.Save(new PersistedState());
        using(var lease=new FileStream(Path.Combine(retryRoot,"state.json"),FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            var release=Task.Run(()=>{Thread.Sleep(120);lease.Dispose();});
            retryStore.Save(new PersistedState { Schedule=new Schedule { Start="07:00" } });
            await release;
        }
        Check(retryStore.Load().Schedule.Start=="07:00","atomic policy replacement survives a short-lived Windows file lock");
        var store = new StateStore(root, true);
        var clock = new ManualClock(new DateTimeOffset(2026, 10, 13, 21, 59, 0, TimeSpan.Zero));
        var adapter = new FakeEnforcement();
        var engine = new CurfewEngine(store, adapter, clock);
        var initial = await engine.HandleAsync(new Request(), false);
        Check(!initial.Status!.Configured && adapter.Applies == 0, "first boot is disarmed");
        var rejectedSetup = await engine.HandleAsync(new Request { Action = "Setup", NewPin = "246810" }, false);
        Check(!rejectedSetup.Success, "live first setup requires verified administrator");
        var setup = await engine.HandleAsync(new Request { Action = "Setup", NewPin = "246810" }, true);
        Check(setup.Success && setup.RecoveryCode is not null, "owner setup returns a private recovery code once");
        var schedule = new Schedule { Enabled = true, Start = "22:00", End = "06:00", ZoneId = "UTC" };
        var badSave = await engine.HandleAsync(new Request { Action = "SaveSchedule", Pin = "000000", Revision = 1, Schedule = schedule }, true);
        Check(!badSave.Success && !badSave.Status!.Schedule.Enabled, "wrong PIN cannot arm curfew");
        var save = await engine.HandleAsync(new Request { Action = "SaveSchedule", Pin = "246810", Revision = 1, Schedule = schedule }, false);
        Check(save.Success && save.Status!.Next?.Start == clock.GetUtcNow().AddMinutes(1), "PIN-authorized schedule is armed");
        async Task Advance(int seconds) { for (var i = 0; i < seconds; i++) { clock.Advance(TimeSpan.FromSeconds(1)); await engine.TickAsync(); } }
        await Advance(60);
        var curfew = (await engine.HandleAsync(new Request(), false)).Status!;
        Check(adapter.Applies == 1 && curfew.Shutdown == "Countdown", "Start applies networking and starts one-minute warning");
        await Advance(59);
        Check(adapter.Shutdowns == 0, "shutdown is not requested before the warning expires");
        var skipRequest = new Request { Action = "Skip", Pin = "246810", OccurrenceId = curfew.Active!.Id };
        var skip = await engine.HandleAsync(skipRequest, false);
        Check(skip.Success && skip.Status!.Active is null && adapter.Removes >= 2, "PIN override restores network and cancels countdown");
        var applyCount = adapter.Applies;
        var replay = await engine.HandleAsync(skipRequest, false);
        Check(replay.Success && adapter.Applies == applyCount, "duplicate owner command does not repeat side effects");
        await Advance(2);
        Check(adapter.Shutdowns == 0, "skipped deadline never requests shutdown");
        var next = (await engine.HandleAsync(new Request(), false)).Status!.Next;
        Check(next is not null && next.Start.Day == 14, "override preserves the next scheduled curfew");
        var stale = await engine.HandleAsync(new Request { Action = "StartTimer", Pin = "246810", Revision = 1, Minutes = 10 }, false);
        Check(!stale.Success, "stale revision cannot create a timer");
        var timer = await engine.HandleAsync(new Request { Action = "StartTimer", Pin = "246810", Revision = 2, Minutes = 1 }, false);
        Check(timer.Success && timer.Status!.Schedule.Enabled, "manual timer leaves recurring schedule enabled");
        await Advance(60);
        var beforeSleep = (await engine.HandleAsync(new Request(), false)).Status!;
        clock.Advance(TimeSpan.FromMinutes(5)); await engine.TickAsync();
        var afterSleep = (await engine.HandleAsync(new Request(), false)).Status!;
        Check(afterSleep.ShutdownAt == clock.GetUtcNow().AddSeconds(60) && adapter.Shutdowns == 0, "resume inside curfew gives a fresh save-work minute");
        await Advance(60);
        var accepted = (await engine.HandleAsync(new Request(), false)).Status!;
        Check(adapter.Shutdowns == 1 && accepted.Shutdown == "Accepted", "deadline requests graceful shutdown exactly once");
        await Advance(31);
        Check((await engine.HandleAsync(new Request(), false)).Status!.Shutdown == "Blocked or delayed", "accepted shutdown is never reported as power-off completion");
        var tooLate = await engine.HandleAsync(new Request { Action = "Skip", Pin = "246810", OccurrenceId = accepted.Active!.Id }, false);
        Check(tooLate.Success&&tooLate.Status!.Active is null&&tooLate.Message.Contains("may still")&&adapter.Shutdowns==1,"accepted shutdown does not prevent internet restoration or falsely promise cancellation");
        var reset = await engine.HandleAsync(new Request { Action = "RecoverPin", RecoveryCode = setup.RecoveryCode, NewPin = "135790" }, false);
        Check(reset.Success && reset.RecoveryCode != setup.RecoveryCode, "recovery rotates both PIN and recovery code");
        var oldRecovery = await engine.HandleAsync(new Request { Action = "RecoverPin", RecoveryCode = setup.RecoveryCode, NewPin = "111111" }, false);
        Check(!oldRecovery.Success, "previous recovery code is invalidated");
        for (var i = 0; i < 4; i++) await engine.HandleAsync(new Request { Action = "AuthorizeQuit", Pin = "000000" }, false);
        var locked = await engine.HandleAsync(new Request { Action = "AuthorizeQuit", Pin = "135790" }, false);
        Check(!locked.Success && locked.Status!.LockedUntil > clock.GetUtcNow(), "global PIN backoff blocks even correct attempts during lockout");
        var restarted = new CurfewEngine(store, adapter, clock);
        Check((await restarted.HandleAsync(new Request(), false)).Status!.LockedUntil == locked.Status!.LockedUntil, "lockout persists across service restart");
        var json = File.ReadAllText(Path.Combine(root, "state.json"));
        Check(!json.Contains("135790") && !json.Contains(setup.RecoveryCode!), "stored state contains no plaintext owner secrets");


        var beforeStartClock=new ManualClock(new DateTimeOffset(2026,10,14,20,37,0,TimeSpan.Zero));
        var beforeStartAdapter=new FakeEnforcement();var beforeStartStore=new StateStore(Path.Combine(root,"before-start"),true);
        var beforeStartEngine=new CurfewEngine(beforeStartStore,beforeStartAdapter,beforeStartClock);
        await beforeStartEngine.HandleAsync(new Request{Action="Setup",NewPin="246810"},true);
        var beforeStartSave=await beforeStartEngine.HandleAsync(new Request{Action="SaveSchedule",Pin="246810",Revision=1,Schedule=new Schedule{Enabled=true,WindowKind="Allowed",Start="20:38",End="20:39",ZoneId="UTC"}},false);
        Check(beforeStartSave.Success&&beforeStartSave.Status!.Active is null&&beforeStartAdapter.Applies==0,"saving at 8:37 PM waits for new 8:39 PM End instead of yesterday's restriction");
        Check((await new CurfewEngine(beforeStartStore,new FakeEnforcement(),beforeStartClock).HandleAsync(new Request(),false)).Status!.Active is null,"restart before first End preserves arming boundary");
        beforeStartClock.Advance(TimeSpan.FromMinutes(1));await beforeStartEngine.TickAsync();
        Check((await beforeStartEngine.HandleAsync(new Request(),false)).Status!.Active is null,"new Start at 8:38 PM does not start shutdown");
        beforeStartClock.Advance(TimeSpan.FromMinutes(1));await beforeStartEngine.TickAsync();
        Check((await beforeStartEngine.HandleAsync(new Request(),false)).Status!.Shutdown=="Countdown"&&beforeStartAdapter.Shutdowns==0,"new End at 8:39 PM starts the one-minute shutdown warning");
        beforeStartAdapter.RemoveFailures=1;
        var removalId=(await beforeStartEngine.HandleAsync(new Request(),false)).Status!.Active!.Id;
        var restorationFailure=await beforeStartEngine.HandleAsync(new Request{Action="Skip",Pin="246810",OccurrenceId=removalId},false);
        Check(!restorationFailure.Success&&restorationFailure.Status!.Network=="Failed restoration","override does not falsely report internet restored when removal fails");
        var retryOff=await beforeStartEngine.HandleAsync(new Request{Action="SaveSchedule",Pin="246810",Revision=restorationFailure.Status!.Schedule.Revision,Schedule=restorationFailure.Status.Schedule with{Enabled=false}},false);
        Check(retryOff.Success&&retryOff.Status!.Network=="Fake rules removed"&&!retryOff.Status.Schedule.Enabled,"owner can retry restoration by saving Curfew off without uninstalling");
        foreach(var journal in new[]{"Accepted","Unknown","Blocked or delayed"})
        {
            var rebootClock=new ManualClock(new DateTimeOffset(2026,10,14,22,5,0,TimeSpan.Zero));
            var rebootSchedule=new Schedule{Enabled=true,WindowKind="Allowed",Start="06:00",End="22:00",ZoneId="UTC"};
            var occurrence=ScheduleResolver.Resolve(rebootSchedule,rebootClock.GetUtcNow()).Single(w=>w.Start<=rebootClock.GetUtcNow()&&rebootClock.GetUtcNow()<w.End);
            var rebootStore=new StateStore(Path.Combine(root,"reboot-"+journal.Replace(" ","-")),true);
            rebootStore.Save(new PersistedState{Owner=OwnerAuthentication.Create("246810").Secrets,Schedule=rebootSchedule,ActiveId=occurrence.Id,ShutdownJournal=journal,ShutdownAt=rebootClock.GetUtcNow().AddMinutes(-3)});
            var rebootAdapter=new FakeEnforcement();var rebootEngine=new CurfewEngine(rebootStore,rebootAdapter,rebootClock);
            var blocked=(await rebootEngine.HandleAsync(new Request(),false)).Status!;
            var badOverride=await rebootEngine.HandleAsync(new Request{Action="Skip",Pin="000000",OccurrenceId=blocked.Active!.Id},false);
            Check(!badOverride.Success&&badOverride.Status!.Active is not null,"restart recovery still requires correct owner PIN for "+journal);
            var restoredByPin=await rebootEngine.HandleAsync(new Request{Action="Skip",Pin="246810",OccurrenceId=blocked.Active!.Id},false);
            Check(restoredByPin.Success&&restoredByPin.Status!.Active is null&&rebootAdapter.Removes>0&&rebootAdapter.Shutdowns==0,"owner PIN restores internet after restart with journal "+journal);
            var skippedState=rebootStore.Load();
            rebootStore.Save(new PersistedState{Owner=OwnerAuthentication.Create("246810").Secrets,Schedule=rebootSchedule,ActiveId=occurrence.Id,ShutdownJournal=journal,ShutdownAt=rebootClock.GetUtcNow().AddMinutes(-3)});
            var disableAdapter=new FakeEnforcement();var disableEngine=new CurfewEngine(rebootStore,disableAdapter,rebootClock);
            var disabled=await disableEngine.HandleAsync(new Request{Action="SaveSchedule",Pin="246810",Revision=1,Schedule=rebootSchedule with{Enabled=false}},false);
            Check(disabled.Success&&!disabled.Status!.Schedule.Enabled&&disabled.Status.Active is null&&disableAdapter.Removes>0,"owner can turn off curfew after restart with journal "+journal);
            rebootStore.Save(skippedState);
            var afterOverrideRestart=new CurfewEngine(rebootStore,new FakeEnforcement(),rebootClock);
            Check((await afterOverrideRestart.HandleAsync(new Request(),false)).Status!.Active is null,"restart preserves override after "+journal);
        }
        var allowedClock=new ManualClock(new DateTimeOffset(2026,10,14,6,0,0,TimeSpan.Zero));
        var allowedAdapter=new FakeEnforcement();var allowedStore=new StateStore(Path.Combine(root,"allowed"),true);
        var allowedEngine=new CurfewEngine(allowedStore,allowedAdapter,allowedClock);
        await allowedEngine.HandleAsync(new Request { Action="Setup",NewPin="246810" },true);
        var allowedSave=await allowedEngine.HandleAsync(new Request { Action="SaveSchedule",Pin="246810",Revision=1,Schedule=new Schedule { Enabled=true,Start="06:00",End="22:00",ZoneId="UTC",WindowKind="Allowed" } },true);
        Check(allowedSave.Success&&allowedSave.Status!.Active is null&&allowedAdapter.Applies==0,"new Start keeps internet available inside allowed hours");
        allowedClock.Advance(TimeSpan.FromHours(16));await allowedEngine.TickAsync();
        var atEnd=(await allowedEngine.HandleAsync(new Request(),false)).Status!;
        Check(atEnd.Active is not null&&atEnd.Shutdown=="Countdown"&&allowedAdapter.Applies>0&&allowedAdapter.Shutdowns==0,"new End restricts internet and gives one minute before shutdown");
        for(var second=0;second<60;second++){allowedClock.Advance(TimeSpan.FromSeconds(1));await allowedEngine.TickAsync();}
        Check(allowedAdapter.Shutdowns==1,"allowed-window End requests graceful shutdown after sixty seconds");
        allowedClock.Advance(TimeSpan.FromHours(8));await allowedEngine.TickAsync();
        var restored=(await allowedEngine.HandleAsync(new Request(),false)).Status!;
        Check(restored.Active is null&&allowedAdapter.Removes>0&&restored.Schedule.Enabled,"following Start restores internet and retains repeat");
        var onceStore=new StateStore(Path.Combine(root,"once"),true);
        onceStore.Save(new PersistedState { Owner=OwnerAuthentication.Create("246810").Secrets,Schedule=new Schedule { Enabled=true,WindowKind="Allowed",Start="06:00",End="22:00",ZoneId="UTC",OneTimeDate="2026-10-14" } });
        var onceEngine=new CurfewEngine(onceStore,new FakeEnforcement(),new ManualClock(new DateTimeOffset(2026,10,15,6,0,0,TimeSpan.Zero)));
        var onceDone=(await onceEngine.HandleAsync(new Request(),false)).Status!;
        Check(!onceDone.Schedule.Enabled&&onceDone.Active is null,"one-time window disarms at its following Start");

        // Exercise real named-pipe framing/impersonation with a no-op worker, never Windows adapters.
        var servicePath = Environment.GetEnvironmentVariable("BREAKPOINT_SERVICE_CHECK_PATH") ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Breakpoint.Service/bin/Release/net10.0-windows/Breakpoint.Service.exe"));
        var pipeName = "Breakpoint.Checks." + Guid.NewGuid().ToString("N");
        var start = new ProcessStartInfo(servicePath) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--preview"); start.ArgumentList.Add("--pipe"); start.ArgumentList.Add(pipeName);
        start.ArgumentList.Add("--data"); start.ArgumentList.Add(Path.Combine(root, "ipc"));
        using var worker = Process.Start(start)!;
        try
        {
            async Task<Response> Send(Request request)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous,
                    System.Security.Principal.TokenImpersonationLevel.Impersonation);
                await pipe.ConnectAsync(timeout.Token);
                await Protocol.WriteAsync(pipe, request, timeout.Token);
                return await Protocol.ReadAsync<Response>(pipe, timeout.Token);
            }
            var status = await Send(new Request());
            Check(status.Success && status.Status!.Preview, "real local named-pipe request reaches preview worker");
            var configured = await Send(new Request { Action = "Setup", NewPin = "246810" });
            Check(configured.Success, "named-pipe caller impersonation permits preview setup");
            var denied = await Send(new Request { Action = "AuthorizeQuit", Pin = "000000" });
            Check(!denied.Success, "named-pipe protected commands require correct PIN");
        }
        finally { if (!worker.HasExited) worker.Kill(); await worker.WaitForExitAsync(); }
        return passed;
    }
}

public sealed class ManualClock(DateTimeOffset now) : TimeProvider
{
    private long stamp;
    public override DateTimeOffset GetUtcNow() => now;
    public override long GetTimestamp() => stamp;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public void Advance(TimeSpan duration) { now += duration; stamp += duration.Ticks; }
}
public sealed class FakeEnforcement : IEnforcement
{
    public bool Preview => false;
    public int Applies, Removes, Shutdowns, RemoveFailures;
    public void CheckReady() { }
    public string ApplyNetwork() { Applies++; return "Fake rules active"; }
    public string RemoveNetwork() { Removes++; if(RemoveFailures>0){RemoveFailures--;throw new IOException("Simulated restoration failure");}return "Fake rules removed"; }
    public void RequestShutdown() { Shutdowns++; }
}


// Exercises the adapter's lookup seam without mutating Windows Firewall.
public sealed class FirewallLookupFixture(Exception failure)
{
    public FirewallRulesFixture Rules { get; }=new(failure);
}
public sealed class FirewallRulesFixture(Exception failure)
{
    public object Item(string name)=>throw failure;
}
