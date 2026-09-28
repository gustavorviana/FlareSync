using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FlareSync.Core.Models;

namespace FlareSync.Providers.DynDns2;

public enum DynDns2Status
{
    Good,
    NoChange,
    BadAuth,
    BadAgent,
    NotDonator,
    Abuse,
    NoHost,
    NotFqdn,
    NumHost,
    ServerError,
    DnsError,
    Unknown,
}

/// <summary>What the client must do after a response.</summary>
public enum DynDns2Action
{
    /// <summary>Success; schedule future updates normally.</summary>
    None,

    /// <summary>Stop every update of this credential until the user intervenes.</summary>
    BlockProvider,

    /// <summary>Stop updating this host until the user intervenes.</summary>
    BlockHost,

    /// <summary>Retry no sooner than <see cref="DynDns2Response.RetryDelay"/>.</summary>
    RetryLater,

    /// <summary>Failure without a special rule.</summary>
    Fail,
}

public sealed record DynDns2Response(DynDns2Status Status, string Code, string Message)
{
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(30);

    public bool Succeeded => Status is DynDns2Status.Good or DynDns2Status.NoChange;

    public DynDns2Action Action => Status switch
    {
        DynDns2Status.Good or DynDns2Status.NoChange => DynDns2Action.None,
        DynDns2Status.BadAuth or DynDns2Status.BadAgent or DynDns2Status.NotDonator or DynDns2Status.Abuse => DynDns2Action.BlockProvider,
        DynDns2Status.NoHost or DynDns2Status.NotFqdn or DynDns2Status.NumHost => DynDns2Action.BlockHost,
        DynDns2Status.ServerError or DynDns2Status.DnsError => DynDns2Action.RetryLater,
        _ => DynDns2Action.Fail,
    };
}

/// <summary>
/// DynDNS2 update protocol: <c>GET /nic/update?hostname=..&amp;myip=v4,v6</c> with Basic auth.
/// The HTTP client has no retry policy: a retried update could be counted as abusive by the service.
/// </summary>
public sealed class DynDns2Client(IHttpClientFactory httpClientFactory)
{
    public const string HttpClientName = "flaresync-dyndns2";

    public async Task<DynDns2Response> UpdateAsync(
        DynDns2Credentials credentials, string hostname, IReadOnlyDictionary<IpFamily, IPAddress> addresses, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUrl(credentials.Server, hostname, addresses));
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credentials.Username}:{credentials.Password}")));
        // The contact e-mail is not a valid product token, so skip header validation.
        request.Headers.TryAddWithoutValidation("User-Agent", credentials.UserAgent);

        using var response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return Parse(body, response.StatusCode);
    }

    public static Uri BuildUrl(string server, string hostname, IReadOnlyDictionary<IpFamily, IPAddress> addresses)
    {
        // IPv4 first, then IPv6, comma separated (dual-stack form of "myip").
        var myip = string.Join(',', Enum.GetValues<IpFamily>()
            .Where(addresses.ContainsKey)
            .Select(f => Uri.EscapeDataString(addresses[f].ToString())));

        var separator = server.Contains('?') ? '&' : '?';
        return new Uri($"{server}{separator}hostname={Uri.EscapeDataString(hostname)}&myip={myip}");
    }

    /// <summary>Interprets the first response line, regardless of the HTTP status (badauth usually comes with 401).</summary>
    public static DynDns2Response Parse(string body, HttpStatusCode statusCode)
    {
        var line = body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        var code = line.Split(' ', 2)[0].ToLowerInvariant();

        return code switch
        {
            "good" => new(DynDns2Status.Good, code, "updated (good)"),
            "nochg" => new(DynDns2Status.NoChange, code, "the service already had this address (nochg)"),
            "badauth" => new(DynDns2Status.BadAuth, code, "invalid username or password (badauth)"),
            "badagent" => new(DynDns2Status.BadAgent, code, "the client was rejected by the service; check the User-Agent (badagent)"),
            "!donator" => new(DynDns2Status.NotDonator, code, "the account does not have access to this feature (!donator)"),
            "abuse" => new(DynDns2Status.Abuse, code, "the account is blocked for abuse (abuse)"),
            "nohost" => new(DynDns2Status.NoHost, code, "the host name does not exist in this account (nohost)"),
            "notfqdn" => new(DynDns2Status.NotFqdn, code, "the host name is not a fully qualified domain name (notfqdn)"),
            "numhost" => new(DynDns2Status.NumHost, code, "too many host names in the request (numhost)"),
            "911" => new(DynDns2Status.ServerError, code, "server-side error (911)"),
            "dnserr" => new(DynDns2Status.DnsError, code, "server-side DNS error (dnserr)"),
            _ => new(DynDns2Status.Unknown, code,
                $"unexpected response (HTTP {(int)statusCode}): '{(line.Length > 80 ? line[..80] + "..." : line)}'"),
        };
    }
}
