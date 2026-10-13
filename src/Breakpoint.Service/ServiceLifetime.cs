using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Breakpoint.Service;

public static class ServiceLifetime
{
    private const string Name = "Breakpoint";
    private static readonly CancellationTokenSource Stop = new();
    private static ServiceMainDelegate? main;
    private static HandlerDelegate? handler;
    private static IntPtr statusHandle;
    public static void Run(Func<CancellationToken, Task> run)
    {
        main = (_, _) =>
        {
            handler = (control, _, _, _) => { if (control is 1 or 5) { Report(3); Stop.Cancel(); } return 0; };
            statusHandle = RegisterServiceCtrlHandlerEx(Name, handler, IntPtr.Zero);
            if (statusHandle == IntPtr.Zero) return;
            Report(2);
            try { Report(4); run(Stop.Token).GetAwaiter().GetResult(); Report(1); }
            catch (OperationCanceledException) { Report(1); }
            catch { Report(1, 1066); }
        };
        var table = new[] { new ServiceTable { Name = Name, Main = Marshal.GetFunctionPointerForDelegate(main) }, new ServiceTable() };
        if (!StartServiceCtrlDispatcher(table)) throw new Win32Exception(Marshal.GetLastWin32Error());
        GC.KeepAlive(main); GC.KeepAlive(handler);
    }
    private static void Report(uint state, uint error = 0)
    {
        var status = new ServiceStatus { Type = 0x10, State = state, Controls = state == 4 ? 1u | 4u : 0, ExitCode = error, WaitHint = state is 2 or 3 ? 10000u : 0 };
        SetServiceStatus(statusHandle, ref status);
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void ServiceMainDelegate(uint argc, IntPtr argv);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate uint HandlerDelegate(uint control, uint eventType, IntPtr eventData, IntPtr context);
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] private struct ServiceTable { [MarshalAs(UnmanagedType.LPWStr)] public string? Name; public IntPtr Main; }
    [StructLayout(LayoutKind.Sequential)] private struct ServiceStatus { public uint Type, State, Controls, ExitCode, SpecificExitCode, Checkpoint, WaitHint; }
    [DllImport("advapi32.dll", EntryPoint="StartServiceCtrlDispatcherW", CharSet=CharSet.Unicode, SetLastError=true)] private static extern bool StartServiceCtrlDispatcher([In] ServiceTable[] table);
    [DllImport("advapi32.dll", EntryPoint="RegisterServiceCtrlHandlerExW", CharSet=CharSet.Unicode, SetLastError=true)] private static extern IntPtr RegisterServiceCtrlHandlerEx(string name, HandlerDelegate handler, IntPtr context);
    [DllImport("advapi32.dll")] private static extern bool SetServiceStatus(IntPtr handle, ref ServiceStatus status);
}
