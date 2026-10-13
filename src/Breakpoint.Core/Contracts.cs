namespace Breakpoint.Core;

public sealed record Schedule
{
    public bool Enabled { get; set; }
    public string WindowKind { get; set; } = "Blocked"; // Missing in old policies: preserve their original meaning.
    public string? OneTimeDate { get; set; }
    public string Start { get; set; } = "22:00";
    public string End { get; set; } = "06:00";
    public string ZoneId { get; set; } = TimeZoneInfo.Local.Id;
    public int[] Days { get; set; } = [0, 1, 2, 3, 4, 5, 6];
    public long Revision { get; set; } = 1;

    public void Validate()
    {
        if (!TimeOnly.TryParseExact(Start, "HH:mm", out var start) ||
            !TimeOnly.TryParseExact(End, "HH:mm", out var end))
            throw new ArgumentException("Start and End must use HH:mm.");
        if (WindowKind is not ("Blocked" or "Allowed")) throw new ArgumentException("Unsupported window type.");
        if (OneTimeDate is not null && (WindowKind!="Allowed" || !DateOnly.TryParseExact(OneTimeDate,"yyyy-MM-dd",out _))) throw new ArgumentException("Invalid one-time date.");
        if (start == end) throw new ArgumentException("Start and End must be different.");
        if (Days.Length == 0 || Days.Any(d => d is < 0 or > 6) || Days.Distinct().Count() != Days.Length)
            throw new ArgumentException("Select at least one weekday, without duplicates.");
        _ = TimeZoneInfo.FindSystemTimeZoneById(ZoneId);
    }
}

public sealed record Window(string Id, DateTimeOffset Start, DateTimeOffset End, bool Manual = false);
public sealed record OwnerSecrets(string Salt, string PinHash, string RecoverySalt, string RecoveryHash);
public sealed record PersistedState
{
    public int SchemaVersion { get; set; } = 2;
    public Schedule Schedule { get; set; } = new();
    public DateTimeOffset? ScheduleArmedAt { get; set; }
    public OwnerSecrets? Owner { get; set; }
    public DateTimeOffset? TimerStart { get; set; }
    public DateTimeOffset? TimerEnd { get; set; }
    public List<string> SkippedOccurrences { get; set; } = [];
    public int FailedAttempts { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public string? ActiveId { get; set; }
    public DateTimeOffset? ShutdownAt { get; set; }
    public string ShutdownJournal { get; set; } = "None";
}

public sealed record Status
{
    public bool Preview { get; init; }
    public bool Configured { get; init; }
    public Schedule Schedule { get; init; } = new();
    public string Phase { get; init; } = "Starting";
    public string Network { get; init; } = "Unknown";
    public string Shutdown { get; init; } = "None";
    public DateTimeOffset Now { get; init; }
    public Window? Active { get; init; }
    public Window? Next { get; init; }
    public DateTimeOffset? ShutdownAt { get; init; }
    public DateTimeOffset? TimerStart { get; init; }
    public DateTimeOffset? LockedUntil { get; init; }
    public string? Error { get; init; }
    public long Sequence { get; init; }
}

public sealed record Request
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Action { get; init; } = "Status";
    public long Revision { get; init; }
    public string? Pin { get; init; }
    public string? RecoveryCode { get; init; }
    public string? NewPin { get; init; }
    public Schedule? Schedule { get; init; }
    public string? OccurrenceId { get; init; }
    public int Minutes { get; init; }
}

public sealed record Response(bool Success, string Message, Status? Status = null, string? RecoveryCode = null);
