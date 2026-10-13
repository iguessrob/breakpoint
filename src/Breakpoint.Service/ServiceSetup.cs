using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Breakpoint.Core;

namespace Breakpoint.Service;

public static class ServiceSetup
{
    public static bool TryRun(string[] args)
    {
        var install=args.Contains("--install");var remove=args.Contains("--uninstall");var validate=args.Contains("--validate-owner");
        var preflight=args.Contains("--preflight");var diagnose=args.Contains("--diagnose-install");
        if(!install&&!remove&&!validate&&!preflight&&!diagnose)return false;
        var stage="validating setup arguments";
        try
        {
            if(validate){_ = OwnerSid(args);return true;}
            if(diagnose){Console.WriteLine("Read-only service setup diagnosis");Console.WriteLine("Administrator: "+new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator));Console.WriteLine("Executable folder: "+AppContext.BaseDirectory);Console.WriteLine("Expected install folder: "+Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Breakpoint"));stage="resolving the Windows account";_ = OwnerSid(args);Console.WriteLine("Windows account: valid");stage="opening Windows Service Control Manager";using var probe=Open();Console.WriteLine("Service exists: "+probe.Exists);stage="reading owned firewall rule presence";Console.WriteLine(WindowsEnforcement.InspectOwnedRules());return true;}
            stage="checking administrator approval";
            if(!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))throw new UnauthorizedAccessException("Windows administrator approval is required.");
            stage="checking the protected installation path";
            var root=Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Breakpoint"));
            if(install&&!string.Equals(root.TrimEnd(Path.DirectorySeparatorChar),AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Service installation is restricted to Program Files\\Breakpoint.");
            stage="resolving the Windows account";
            if(preflight){_ = OwnerSid(args);stage="opening Windows Service Control Manager";using var probe=Open();if(probe.Exists)throw new InvalidOperationException("Uninstall the existing Breakpoint service first.");Console.WriteLine("Read-only installation preflight passed.");return true;}
            if(install)Install(root,OwnerSid(args),value=>stage=value);else {stage="removing the service and owned restrictions";Remove(root);}
        }
        catch(Exception error){Console.Error.WriteLine($"Service setup failed while {stage}: {error.Message}");Environment.ExitCode=1;}
        return true;
    }
    private static string OwnerSid(string[] args)
    {
        var index=Array.IndexOf(args,"--owner-account");
        if(index<0||index+1>=args.Length)throw new ArgumentException("Windows account is required.");
        var sid=(SecurityIdentifier)new NTAccount(args[index+1]).Translate(typeof(SecurityIdentifier));
        if(!sid.IsAccountSid())throw new ArgumentException("Choose a Windows user account.");return sid.Value;
    }
    private static void Install(string root,string sid,Action<string> stage)
    {
        stage("checking for an existing service");using var service=Open();if(service.Exists)throw new InvalidOperationException("Uninstall the existing Breakpoint service first.");
        var executable=Path.Combine(root,"Breakpoint.Service.exe");
        var data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Breakpoint");
        stage("preparing protected owner policy");var store=new StateStore(data,false);var policy=store.Load();
        policy.SchemaVersion=2;policy.ScheduleArmedAt=null;policy.Schedule.Enabled=false;policy.Schedule.Revision++;
        policy.TimerStart=null;policy.TimerEnd=null;policy.ActiveId=null;policy.ShutdownAt=null;policy.ShutdownJournal="None";store.Save(policy);
        _=new WindowsEnforcement().RemoveNetwork();
        var created=false;
        try
        {
            stage("registering the Windows service");Sc("create","Breakpoint","binPath=",$"\"{executable}\" --owner-sid {sid}","start=","auto","DisplayName=","Breakpoint Curfew");created=true;
            stage("configuring service recovery");Sc("description","Breakpoint","Owner-controlled internet hours and shutdown warnings.");
            Sc("failure","Breakpoint","reset=","86400","actions=","restart/5000/restart/15000/restart/30000");
            Sc("failureflag","Breakpoint","1");stage("starting the Windows service");Sc("start","Breakpoint");
            stage("waiting for the Windows service to run");using var started=Open();started.WaitFor(4);
        }
        catch(Exception failure){if(created){try{using var partial=Open();partial.Stop();Sc("delete","Breakpoint");}catch(Exception cleanup){throw new InvalidOperationException($"{failure.Message} Service cleanup also failed: {cleanup.Message}",failure);}}throw;}
    }
    private static void Remove(string root)
    {
        using var service=Open();
        if(service.Exists)
        {
            var image=Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\Breakpoint","ImagePath","") as string;
            if(image is null||!image.StartsWith('"'+Path.Combine(root,"Breakpoint.Service.exe")+'"',StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Unexpected process owns the Breakpoint service.");
            service.Stop();
        }
        _=new WindowsEnforcement().RemoveNetwork();
        if(service.Exists)Sc("delete","Breakpoint");
        // Inno removes only files it installed. Protected owner policy is deliberately retained.
    }
    private static void Sc(params string[] arguments)
    {
        var info=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"sc.exe")) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
        foreach(var argument in arguments)info.ArgumentList.Add(argument);
        using var process=Process.Start(info)!;var output=process.StandardOutput.ReadToEnd();var errors=process.StandardError.ReadToEnd();process.WaitForExit();
        if(process.ExitCode!=0)throw new InvalidOperationException($"Windows service operation failed ({process.ExitCode}): {output} {errors}");
    }
    private static ServiceHandle Open()
    {
        var manager=OpenSCManager(null,null,1);if(manager==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());
        try{var handle=OpenService(manager,"Breakpoint",0x24);if(handle==IntPtr.Zero&&Marshal.GetLastWin32Error()!=1060)throw new Win32Exception(Marshal.GetLastWin32Error());return new ServiceHandle(handle);}
        finally{CloseServiceHandle(manager);}
    }
    private sealed class ServiceHandle(IntPtr handle):IDisposable
    {
        public bool Exists=>handle!=IntPtr.Zero;
        public void WaitFor(uint expected)
        {
            var timer=Stopwatch.StartNew();while(timer.Elapsed<TimeSpan.FromSeconds(20)){if(!QueryServiceStatus(handle,out var status))throw new Win32Exception(Marshal.GetLastWin32Error());if(status.State==expected)return;Thread.Sleep(150);}throw new TimeoutException("Windows service did not reach the expected state.");
        }
        public void Stop(){if(!Exists)return;if(!QueryServiceStatus(handle,out var status))throw new Win32Exception(Marshal.GetLastWin32Error());if(status.State==1)return;if(!ControlService(handle,1,out _)&&Marshal.GetLastWin32Error()!=1062)throw new Win32Exception(Marshal.GetLastWin32Error());WaitFor(1);}
        public void Dispose(){if(handle!=IntPtr.Zero)CloseServiceHandle(handle);}
    }
    [StructLayout(LayoutKind.Sequential)]private struct ServiceStatus{public uint Type,State,Accepted,Win32Exit,SpecificExit,Checkpoint,WaitHint;}
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true,EntryPoint="OpenSCManagerW")]private static extern IntPtr OpenSCManager(string? machine,string? database,uint access);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true,EntryPoint="OpenServiceW")]private static extern IntPtr OpenService(IntPtr manager,string name,uint access);
    [DllImport("advapi32.dll",SetLastError=true)]private static extern bool QueryServiceStatus(IntPtr service,out ServiceStatus status);
    [DllImport("advapi32.dll",SetLastError=true)]private static extern bool ControlService(IntPtr service,uint control,out ServiceStatus status);
    [DllImport("advapi32.dll")]private static extern bool CloseServiceHandle(IntPtr handle);
}
