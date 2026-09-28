using System.Globalization;
using FlareSync.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace FlareSync.Core.Commands;

public static class CommandHelpers
{
    public static T Service<T>(this CommandContext context)
        where T : notnull
        => context.Services.GetRequiredService<T>();

    public static string Format(this DateTimeOffset? value)
        => value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "-";

    public static string Label(this IpFamily family) => family == IpFamily.IPv4 ? "IPv4" : "IPv6";

    public static string YesNo(bool value) => value ? "yes" : "no";

    public static string FormatFamilies(bool ipv4, bool ipv6) => (ipv4, ipv6) switch
    {
        (true, true) => "IPv4 + IPv6",
        (true, false) => "IPv4 only",
        (false, true) => "IPv6 only",
        _ => "none",
    };

    /// <summary>Lower-case host name without surrounding spaces or trailing dot.</summary>
    public static string NormalizeHostname(string hostname) => hostname.Trim().TrimEnd('.').ToLowerInvariant();

    /// <summary>Argument for a fully qualified host name, validated.</summary>
    public static ArgumentDefinition<string> HostnameArgument(string description = "Fully qualified host name, e.g. home.example.com.") => new()
    {
        Name = "hostname",
        Description = description,
        Validator = v => Uri.CheckHostName(NormalizeHostname(v)) == UriHostNameType.Dns && v.Contains('.')
            ? null
            : $"'{v}' is not a valid host name.",
    };

    /// <summary>Parses durations such as <c>30s</c>, <c>5m</c>, <c>1h</c> or <c>00:05:00</c>.</summary>
    public static bool TryParseDuration(string? value, out TimeSpan duration)
    {
        duration = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim().ToLowerInvariant();
        var unit = text[^1];
        if (unit is 's' or 'm' or 'h' or 'd'
            && double.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var amount)
            && amount > 0)
        {
            duration = unit switch
            {
                's' => TimeSpan.FromSeconds(amount),
                'm' => TimeSpan.FromMinutes(amount),
                'h' => TimeSpan.FromHours(amount),
                _ => TimeSpan.FromDays(amount),
            };
            return true;
        }

        return TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out duration) && duration > TimeSpan.Zero;
    }
}
