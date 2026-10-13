using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Breakpoint.Core;

namespace Breakpoint.Service;

public sealed class StateStore
{
    private readonly string path;
    public string DirectoryPath { get; }
    public StateStore(string directory, bool preview)
    {
        DirectoryPath = Path.GetFullPath(directory);
        Directory.CreateDirectory(DirectoryPath);
        if (!preview)
        {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
                security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(DirectoryPath).SetAccessControl(security);
        }
        path = Path.Combine(DirectoryPath, "state.json");
    }
    public PersistedState Load()
    {
        if (!File.Exists(path)) return new();
        var state = JsonSerializer.Deserialize<PersistedState>(File.ReadAllText(path), Protocol.Json)
            ?? throw new InvalidDataException("State file is empty.");
        if (state.SchemaVersion is not (1 or 2)) throw new InvalidDataException("Unsupported state version.");
        state.Schedule.Validate();
        return state;
    }
    public void Save(PersistedState state)
    {
        var temporary = path + ".new";
        using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            JsonSerializer.Serialize(file, state, Protocol.Json);
            file.Flush(true);
        }
        // Windows can deny replacement while a reader holds the destination without delete sharing.
        // Keep the commit atomic; briefly retry lock/access errors and preserve persistent failures.
        for(var attempt=0;;attempt++)
        {
            try{File.Move(temporary,path,true);break;}
            catch(Exception e) when(attempt<6 && e is IOException or UnauthorizedAccessException && (e.HResult&0xffff) is 5 or 32 or 33)
            {Thread.Sleep(40*(attempt+1));}
        }
    }
    public void Log(string eventName, string message)
    {
        try
        {
            var day = DateTime.UtcNow.ToString("yyyy-MM-dd");
            File.AppendAllText(Path.Combine(DirectoryPath, $"events-{day}.log"), $"{DateTimeOffset.UtcNow:O} {eventName} {message}\n");
            foreach (var file in Directory.EnumerateFiles(DirectoryPath, "events-*.log"))
                if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-7)) File.Delete(file);
        }
        catch (IOException) { }
    }
}
