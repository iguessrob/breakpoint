using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Breakpoint.Service;

public interface IEnforcement
{
    bool Preview { get; }
    void CheckReady();
    string ApplyNetwork();
    string RemoveNetwork();
    void RequestShutdown();
}

public sealed class PreviewEnforcement : IEnforcement
{
    public bool Preview => true;
    public void CheckReady() { }
    public string ApplyNetwork() => "Simulated restriction";
    public string RemoveNetwork() => "Simulated restoration";
    public void RequestShutdown() { }
}

public sealed class WindowsEnforcement : IEnforcement
{
    public const string RulePrefix = "Breakpoint.Curfew.7B58EE17";
    private const string Addresses = "0.0.0.0-126.255.255.255,128.0.0.0-255.255.255.255,::-::,::2-ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff";
    public bool Preview => false;
    private static dynamic Policy() => Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2")!)!;
    private static dynamic? FindRule(dynamic policy,string name)
    {
        try { return policy.Rules.Item(name); }
        catch (Exception error) when(error.HResult==unchecked((int)0x80070002)) { return null; }
    }
    // Read-only diagnostic. Never adds, removes, or updates a firewall rule.
    public static string InspectOwnedRules()
    {
        dynamic policy=Policy();
        try
        {
            var results=new List<string>();
            foreach(var direction in new[]{1,2})
            {
                dynamic? rule=FindRule(policy,$"{RulePrefix}.{direction}");
                results.Add($"Owned rule {direction}: {(rule is null?"absent":"present")}");
                if(rule is not null)Marshal.FinalReleaseComObject(rule);
            }
            return string.Join(Environment.NewLine,results);
        }
        finally { Marshal.FinalReleaseComObject(policy); }
    }
    public void CheckReady()
    {
        dynamic policy = Policy();
        try
        {
            foreach (var profile in new[] { 1, 2, 4 })
                if (!(bool)policy.FirewallEnabled[profile]) throw new InvalidOperationException("Enable Windows Firewall in every profile before arming curfew.");
            if ((int)policy.LocalPolicyModifyState != 0)
                throw new InvalidOperationException("Managed firewall policy prevents reliable local enforcement.");
        }
        finally { Marshal.FinalReleaseComObject(policy); }
        EnableShutdownPrivilege();
    }
    public string ApplyNetwork()
    {
        CheckReady();
        dynamic policy = Policy();
        try
        {
            foreach (var direction in new[] { 1, 2 })
            {
                var name = $"{RulePrefix}.{direction}";
                dynamic? rule = FindRule(policy,name);
                if (rule is null)
                {
                    rule = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule")!)!;
                    rule.Name = name;
                    rule.Description = "Breakpoint managed curfew. Remove only through authorized recovery.";
                    rule.Grouping = RulePrefix;
                    rule.Protocol = 256;
                    rule.Direction = direction;
                    rule.Profiles = int.MaxValue;
                    rule.InterfaceTypes = "All";
                    rule.RemoteAddresses = Addresses;
                    rule.Action = 0;
                    rule.Enabled = true;
                    policy.Rules.Add(rule);
                }
                try
                {
                    if ((string)rule.Grouping != RulePrefix || (int)rule.Action != 0 ||
                        (int)rule.Direction != direction || !(bool)rule.Enabled || (int)rule.Protocol != 256 ||
                        (int)rule.Profiles != int.MaxValue || (string)rule.RemoteAddresses != Addresses)
                        throw new InvalidOperationException("Owned firewall rule failed verification.");
                }
                finally { Marshal.FinalReleaseComObject(rule); }
            }
            return "Firewall rules active";
        }
        finally { Marshal.FinalReleaseComObject(policy); }
    }
    public string RemoveNetwork()
    {
        dynamic policy = Policy();
        try
        {
            foreach (var direction in new[] { 1, 2 })
            {
                var name = $"{RulePrefix}.{direction}";
                dynamic? rule = FindRule(policy,name);
                if (rule is null) continue;
                try
                {
                    if ((string)rule.Grouping != RulePrefix) throw new InvalidOperationException("Firewall rule ownership mismatch; refusing removal.");
                    policy.Rules.Remove(name);
                }
                finally { Marshal.FinalReleaseComObject(rule); }
                dynamic? remaining = FindRule(policy,name);
                if (remaining is not null) { Marshal.FinalReleaseComObject(remaining); throw new InvalidOperationException("Restriction removal could not be verified."); }
            }
            return "Breakpoint rules removed";
        }
        finally { Marshal.FinalReleaseComObject(policy); }
    }
    public void RequestShutdown()
    {
        EnableShutdownPrivilege();
        // Service owns the 60-second countdown. No forced app closure or second OS countdown.
        if (!InitiateSystemShutdownEx(null, "Breakpoint curfew", 0, false, false, 0x80040001))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    private static void EnableShutdownPrivilege()
    {
        if (!OpenProcessToken(GetCurrentProcess(), 0x20 | 0x8, out var token)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            if (!LookupPrivilegeValue(null, "SeShutdownPrivilege", out var luid)) throw new Win32Exception(Marshal.GetLastWin32Error());
            var privileges = new TokenPrivileges { Count = 1, Luid = luid, Attributes = 2 };
            if (!AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero) || Marshal.GetLastWin32Error() != 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { CloseHandle(token); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] private struct TokenPrivileges { public uint Count; public Luid Luid; public uint Attributes; }
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("advapi32.dll", SetLastError=true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern bool LookupPrivilegeValue(string? system, string name, out Luid luid);
    [DllImport("advapi32.dll", SetLastError=true)] private static extern bool AdjustTokenPrivileges(IntPtr token, bool disable, ref TokenPrivileges privileges, uint size, IntPtr previous, IntPtr returned);
    [DllImport("advapi32.dll", EntryPoint="InitiateSystemShutdownExW", CharSet=CharSet.Unicode, SetLastError=true)] private static extern bool InitiateSystemShutdownEx(string? machine, string message, uint timeout, bool force, bool reboot, uint reason);
}
