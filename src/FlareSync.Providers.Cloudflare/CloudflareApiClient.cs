using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FlareSync.Core;

namespace FlareSync.Providers.Cloudflare;

public sealed class CloudflareApiException(string message) : FlareSyncException(message);

/// <summary>Minimal Cloudflare API v4 client. The token is passed per call so it can be verified before being saved.</summary>
public sealed class CloudflareApiClient(IHttpClientFactory httpClientFactory)
{
    public const string HttpClientName = "cloudflare";
    public static readonly Uri BaseAddress = new("https://api.cloudflare.com/client/v4/");

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>Returns the token status (e.g. <c>active</c>).</summary>
    public async Task<string> VerifyTokenAsync(string token, CancellationToken cancellationToken)
    {
        var status = await SendAsync<CloudflareTokenStatus>(HttpMethod.Get, "user/tokens/verify", token, null, cancellationToken);
        return status.Status;
    }

    public async Task<IReadOnlyList<CloudflareZone>> ListZonesAsync(string token, CancellationToken cancellationToken)
    {
        var zones = new List<CloudflareZone>();
        for (var page = 1; ; page++)
        {
            var (result, info) = await SendPagedAsync<List<CloudflareZone>>($"zones?per_page=50&page={page}", token, cancellationToken);
            zones.AddRange(result);
            if (info is null || page >= info.TotalPages)
            {
                return zones;
            }
        }
    }

    public async Task<IReadOnlyList<CloudflareDnsRecord>> FindRecordsAsync(
        string token, string zoneId, string type, string name, CancellationToken cancellationToken)
    {
        var path = $"zones/{Uri.EscapeDataString(zoneId)}/dns_records?type={type}&name={Uri.EscapeDataString(name)}";
        return await SendAsync<List<CloudflareDnsRecord>>(HttpMethod.Get, path, token, null, cancellationToken);
    }

    public Task<CloudflareDnsRecord> CreateRecordAsync(
        string token, string zoneId, CloudflareDnsRecord record, CancellationToken cancellationToken)
        => SendAsync<CloudflareDnsRecord>(HttpMethod.Post, $"zones/{Uri.EscapeDataString(zoneId)}/dns_records", token, ToRequest(record, "Managed by FlareSync"), cancellationToken);

    public Task<CloudflareDnsRecord> UpdateRecordAsync(
        string token, string zoneId, string recordId, CloudflareDnsRecord record, CancellationToken cancellationToken)
        => SendAsync<CloudflareDnsRecord>(HttpMethod.Patch, $"zones/{Uri.EscapeDataString(zoneId)}/dns_records/{Uri.EscapeDataString(recordId)}", token, ToRequest(record, null), cancellationToken);

    private static CloudflareRecordRequest ToRequest(CloudflareDnsRecord record, string? comment) => new()
    {
        Type = record.Type,
        Name = record.Name,
        Content = record.Content,
        Ttl = record.Ttl,
        Proxied = record.Proxied,
        Comment = comment,
    };

    private async Task<T> SendAsync<T>(HttpMethod method, string path, string token, object? body, CancellationToken cancellationToken)
        => (await SendCoreAsync<T>(method, path, token, body, cancellationToken)).Result!;

    private async Task<(T Result, CloudflareResultInfo? Info)> SendPagedAsync<T>(string path, string token, CancellationToken cancellationToken)
    {
        var envelope = await SendCoreAsync<T>(HttpMethod.Get, path, token, null, cancellationToken);
        return (envelope.Result!, envelope.ResultInfo);
    }

    private async Task<CloudflareEnvelope<T>> SendCoreAsync<T>(
        HttpMethod method, string path, string token, object? body, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(method, new Uri(BaseAddress, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: SerializerOptions);
        }

        using var response = await client.SendAsync(request, cancellationToken);

        CloudflareEnvelope<T>? envelope;
        try
        {
            envelope = await response.Content.ReadFromJsonAsync<CloudflareEnvelope<T>>(SerializerOptions, cancellationToken);
        }
        catch (JsonException)
        {
            envelope = null;
        }

        if (envelope is null)
        {
            throw new CloudflareApiException($"Cloudflare API returned HTTP {(int)response.StatusCode} with an unexpected body.");
        }

        if (!envelope.Success || !response.IsSuccessStatusCode || envelope.Result is null)
        {
            var errors = envelope.Errors.Count > 0 ? string.Join("; ", envelope.Errors) : $"HTTP {(int)response.StatusCode}";
            throw new CloudflareApiException($"Cloudflare API error: {errors}");
        }

        return envelope;
    }
}
