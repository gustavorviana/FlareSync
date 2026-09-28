namespace FlareSync.Providers.Cloudflare;

/// <summary>
/// Dashboard link that opens the "create API token" form pre-filled with the permissions FlareSync needs.
/// See https://developers.cloudflare.com/fundamentals/api/how-to/account-owned-token-template/.
/// </summary>
public static class CloudflareTokenTemplate
{
    public const string TokenName = "FlareSync";

    /// <summary>Zone:Read (list zones) and DNS:Edit (update records).</summary>
    public const string PermissionGroupKeys = """[{"key":"zone","type":"read"},{"key":"dns","type":"edit"}]""";

    public static string BuildUrl() =>
        "https://dash.cloudflare.com/profile/api-tokens"
        + "?permissionGroupKeys=" + Uri.EscapeDataString(PermissionGroupKeys)
        + "&accountId=" + Uri.EscapeDataString("*")
        + "&zoneId=all"
        + "&name=" + Uri.EscapeDataString(TokenName);
}
