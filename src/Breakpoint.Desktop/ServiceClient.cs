using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Breakpoint.Core;

namespace Breakpoint.Desktop;

public sealed class ServiceClient(string pipeName,bool preview=false)
{
    public async Task<Response> SendAsync(Request request)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation);
        await pipe.ConnectAsync(timeout.Token);
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var pid)) throw new IOException("Cannot verify service identity.");
        if(preview)VerifyPreviewProcess(pid);else VerifyRegisteredService(pid);
        await Protocol.WriteAsync(pipe, request, timeout.Token);
        return await Protocol.ReadAsync<Response>(pipe, timeout.Token);
    }
    private static void VerifyPreviewProcess(uint pid)
    {
        var expected = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Breakpoint.Service.exe"));
        // The installed Program Files location is protected by installer ACLs. Shared administrator access remains a limitation.
        var handle = OpenProcess(0x1000, false, pid);
        if (handle == IntPtr.Zero) throw new IOException("Cannot inspect service identity.");
        string actual;
        try
        {
            var buffer = new System.Text.StringBuilder(32768); var length = buffer.Capacity;
            if (!QueryFullProcessImageName(handle, 0, buffer, ref length)) throw new IOException("Cannot inspect service executable.");
            actual = buffer.ToString();
        }
        finally { CloseHandle(handle); }
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Unexpected process owns the Breakpoint service pipe.");
    }
    private static void VerifyRegisteredService(uint pid)
    {
        // Query the protected SCM registration; never require access to the SYSTEM process.
        var manager=OpenSCManager(null,null,1);
        if(manager==IntPtr.Zero)throw NativeError("Cannot query Windows Service Control Manager.");
        try
        {
            var service=OpenService(manager,"Breakpoint",4);
            if(service==IntPtr.Zero)throw NativeError("Cannot query the installed Breakpoint service.");
            try
            {
                if(!QueryServiceStatusEx(service,0,out var status,Marshal.SizeOf<ServiceStatusProcess>(),out _))throw NativeError("Cannot query Breakpoint service status.");
                const string key=@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\Breakpoint";
                var image=Microsoft.Win32.Registry.GetValue(key,"ImagePath",null) as string;
                var account=Microsoft.Win32.Registry.GetValue(key,"ObjectName",null) as string;
                CheckRegistration(pid,status.ProcessId,status.State,status.Type,image??"",account??"");
            }
            finally { CloseServiceHandle(service); }
        }
        finally { CloseServiceHandle(manager); }
    }
    private static void CheckRegistration(uint serverPid,uint servicePid,uint state,uint type,string image,string account)
    {
        var expected=Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Breakpoint","Breakpoint.Service.exe"));
        var quoted="\""+expected+"\"";
        if(serverPid==0||serverPid!=servicePid||state!=4||type!=0x10||!string.Equals(account,"LocalSystem",StringComparison.OrdinalIgnoreCase)||
            !image.StartsWith(quoted,StringComparison.OrdinalIgnoreCase)||(image.Length>quoted.Length&&!char.IsWhiteSpace(image[quoted.Length])))
            throw new IOException("Unexpected process owns the Breakpoint service pipe.");
    }
    private static IOException NativeError(string message)=>new(message,new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
    [StructLayout(LayoutKind.Sequential)] private struct ServiceStatusProcess { public uint Type,State,Controls,ExitCode,SpecificExitCode,Checkpoint,WaitHint,ProcessId,Flags; }
    [DllImport("advapi32.dll",EntryPoint="OpenSCManagerW",CharSet=CharSet.Unicode,SetLastError=true)]private static extern IntPtr OpenSCManager(string? machine,string? database,uint access);
    [DllImport("advapi32.dll",EntryPoint="OpenServiceW",CharSet=CharSet.Unicode,SetLastError=true)]private static extern IntPtr OpenService(IntPtr manager,string name,uint access);
    [DllImport("advapi32.dll",SetLastError=true)]private static extern bool QueryServiceStatusEx(IntPtr service,uint level,out ServiceStatusProcess status,int size,out int required);
    [DllImport("advapi32.dll")]private static extern bool CloseServiceHandle(IntPtr service);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint pid);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern IntPtr OpenProcess(uint access, bool inherit, uint process);
    [DllImport("kernel32.dll", EntryPoint="QueryFullProcessImageNameW", CharSet=CharSet.Unicode, SetLastError=true)] private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, System.Text.StringBuilder name, ref int length);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
