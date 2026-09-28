using System.Net;

namespace FlareSync.Core.Models;

public enum IpFamily
{
    IPv4,
    IPv6,
}

/// <summary>Address families a host name keeps in sync, as chosen with <c>--family</c>.</summary>
public enum IpFamilies
{
    IPv4,
    IPv6,
    Both,
}

/// <summary>A DNS host name managed by a provider, with the address families to keep in sync.</summary>
public sealed record DnsTarget(string Provider, string Hostname, bool IPv4, bool IPv6)
{
    /// <summary>Provider-specific data attached when the target was loaded (opaque to the core).</summary>
    public object? ProviderData { get; init; }

    public bool Uses(IpFamily family) => family == IpFamily.IPv4 ? IPv4 : IPv6;
}

public enum SyncOutcome
{
    Unchanged,
    Updated,
    Created,
    Skipped,
    Failed,
}

public sealed record SyncResult(SyncOutcome Outcome, string? Message = null)
{
    public bool Succeeded => Outcome is SyncOutcome.Unchanged or SyncOutcome.Updated or SyncOutcome.Created;

    public static SyncResult Unchanged(string? message = null) => new(SyncOutcome.Unchanged, message);

    public static SyncResult Updated(string? message = null) => new(SyncOutcome.Updated, message);

    public static SyncResult Created(string? message = null) => new(SyncOutcome.Created, message);

    public static SyncResult Skipped(string message) => new(SyncOutcome.Skipped, message);

    public static SyncResult Failed(string message) => new(SyncOutcome.Failed, message);
}

/// <summary>Result of detecting the public address of one family.</summary>
public sealed record IpDetection(IpFamily Family, IPAddress? Address, string? Source, IReadOnlyList<string> Errors)
{
    public bool Succeeded => Address is not null;
}
