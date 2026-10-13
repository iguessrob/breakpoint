using System.Security.Principal;
using System.Text.Json;
using Breakpoint.Core;
using Breakpoint.Service;

if(ServiceSetup.TryRun(args))return;

string Value(string flag, string fallback) => Array.IndexOf(args, flag) is var index && index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
var preview = args.Contains("--preview");
var data = Value("--data", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Breakpoint"));
var pipeName = Value("--pipe", Protocol.LivePipe);
var sid = Value("--owner-sid", WindowsIdentity.GetCurrent().User!.Value);
if (!preview && args.Contains("--console")) throw new InvalidOperationException("Live enforcement runs only as an installed Windows service.");
if (args.Contains("--cleanup"))
{
    if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
        throw new UnauthorizedAccessException("Cleanup requires administrator access.");
    Console.WriteLine(new WindowsEnforcement().RemoveNetwork());
    return;
}
async Task Run(CancellationToken token)
{
    var store = new StateStore(data, preview);
    var engine = new CurfewEngine(store, preview ? new PreviewEnforcement() : new WindowsEnforcement());
    await engine.TickAsync();
    var server = new PipeServer(engine, pipeName, sid);
    var scheduler = Task.Run(async () =>
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(token)) await engine.TickAsync();
    }, token);
    await Task.WhenAny(scheduler, server.RunAsync(token));
    token.ThrowIfCancellationRequested();
    throw new InvalidOperationException("A service worker stopped unexpectedly.");
}
if (preview)
{
    using var stop = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
    try { await Run(stop.Token); } catch (OperationCanceledException) { }
}
else ServiceLifetime.Run(Run);
