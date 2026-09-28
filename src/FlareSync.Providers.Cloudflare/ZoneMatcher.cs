namespace FlareSync.Providers.Cloudflare;

public static class ZoneMatcher
{
    /// <summary>Returns the zone whose name is the longest suffix of <paramref name="hostname"/>.</summary>
    public static CloudflareZone? FindZone(string hostname, IEnumerable<CloudflareZone> zones)
    {
        var host = Normalize(hostname);
        return zones
            .Where(z =>
            {
                var zone = Normalize(z.Name);
                return host == zone || host.EndsWith("." + zone, StringComparison.Ordinal);
            })
            .MaxBy(z => z.Name.Length);
    }

    public static string Normalize(string hostname) => hostname.Trim().TrimEnd('.').ToLowerInvariant();
}
