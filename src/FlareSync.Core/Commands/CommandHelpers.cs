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
