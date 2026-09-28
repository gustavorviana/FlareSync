using System.Text.Json.Serialization;

namespace FlareSync.Providers.Cloudflare;

internal sealed class CloudflareEnvelope<T>
{
    public bool Success { get; set; }

    public List<CloudflareMessage> Errors { get; set; } = [];

    public T? Result { get; set; }

    public CloudflareResultInfo? ResultInfo { get; set; }
}

internal sealed class CloudflareMessage
{
    public int Code { get; set; }

    public string Message { get; set; } = "";

    public override string ToString() => $"{Message} (code {Code})";
}

internal sealed class CloudflareResultInfo
{
    public int Page { get; set; }

    public int TotalPages { get; set; }
}

public sealed class CloudflareZone
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public string Status { get; set; } = "";
}

public sealed class CloudflareDnsRecord
{
    public string Id { get; set; } = "";

    public string Type { get; set; } = "";

    public string Name { get; set; } = "";

    public string Content { get; set; } = "";

    public int Ttl { get; set; }

    public bool Proxied { get; set; }
}

internal sealed class CloudflareTokenStatus
{
    public string Id { get; set; } = "";

    public string Status { get; set; } = "";
}

internal sealed class CloudflareRecordRequest
{
    public string Type { get; set; } = "";

    public string Name { get; set; } = "";

    public string Content { get; set; } = "";

    public int Ttl { get; set; }

    public bool Proxied { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Comment { get; set; }
}
