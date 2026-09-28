namespace FlareSync.Providers.DynDns2;

/// <summary>
/// A DynDNS2-compatible service. Each preset becomes its own provider module (name, configuration file and command group).
/// </summary>
/// <param name="Name">Lowercase module name: command group and <c>providers/&lt;name&gt;.json</c>.</param>
/// <param name="DisplayName">Name shown to the user.</param>
/// <param name="DefaultServer">Update URL; <c>null</c> when the user must supply one at login.</param>
/// <param name="CredentialHint">Explains which credentials to use.</param>
public sealed record DynDns2Preset(string Name, string DisplayName, string? DefaultServer, string CredentialHint)
{
    /// <summary>No-IP (https://www.noip.com/integrate/request).</summary>
    public static readonly DynDns2Preset NoIp = new(
        "noip",
        "No-IP",
        "https://dynupdate.no-ip.com/nic/update",
        "Use your No-IP account e-mail and password, or the username and password of a No-IP DDNS Key.");

    /// <summary>Any DynDNS2-compatible service; the update URL is given at login.</summary>
    public static readonly DynDns2Preset Generic = new(
        "dyndns2",
        "DynDNS2",
        null,
        "Use the credentials of your DynDNS2-compatible service; the update URL usually ends with /nic/update.");
}
