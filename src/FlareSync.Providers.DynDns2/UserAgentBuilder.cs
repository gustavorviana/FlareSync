using System.Net.Mail;

namespace FlareSync.Providers.DynDns2;

/// <summary>
/// Builds the User-Agent required by DynDNS2 services: <c>Company Program/Version maintainer-contact@example.com</c>.
/// </summary>
public static class UserAgentBuilder
{
    /// <summary>Default "Company Program/Version" part, e.g. <c>FlareSync FlareSync/linux-1.0.0</c>.</summary>
    public static string DefaultProduct { get; } = $"FlareSync FlareSync/{Platform()}-{Version()}";

    public static string Build(string? product, string contact)
        => $"{(string.IsNullOrWhiteSpace(product) ? DefaultProduct : product.Trim())} {contact.Trim()}";

    public static string? ValidateContact(string? contact)
        => !string.IsNullOrWhiteSpace(contact) && MailAddress.TryCreate(contact.Trim(), out var address) && address.Address == contact.Trim()
            ? null
            : $"'{contact}' is not a valid e-mail address.";

    /// <summary>Expects <c>Company Program/Version</c>.</summary>
    public static string? ValidateProduct(string? product)
        => product is not null && System.Text.RegularExpressions.Regex.IsMatch(product.Trim(), @"^\S+ \S+/\S+$")
            ? null
            : $"'{product}' must look like 'Company Program/Version', e.g. '{DefaultProduct}'.";

    private static string Platform()
        => OperatingSystem.IsLinux() ? "linux"
            : OperatingSystem.IsWindows() ? "windows"
            : OperatingSystem.IsMacOS() ? "macos"
            : "other";

    private static string Version() => typeof(UserAgentBuilder).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
}
