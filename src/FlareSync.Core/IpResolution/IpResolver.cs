using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using FlareSync.Core.Abstractions;
using FlareSync.Core.Config;
using FlareSync.Core.Models;
using Microsoft.Extensions.Logging;

namespace FlareSync.Core.IpResolution;

/// <summary>
/// Detects the public address by querying the configured IP sources in order until one returns a valid answer.
/// Requests go through per-family HTTP clients that only connect over that family.
/// </summary>
public sealed class IpResolver(
    IHttpClientFactory httpClientFactory,
    GlobalConfigStore configStore,
    ILogger<IpResolver> logger) : IIpResolver
{
    public const string IPv4ClientName = "flaresync-ip-ipv4";
    public const string IPv6ClientName = "flaresync-ip-ipv6";

    public static string ClientName(IpFamily family) => family == IpFamily.IPv4 ? IPv4ClientName : IPv6ClientName;

    public async Task<IpDetection> DetectAsync(IpFamily family, CancellationToken cancellationToken)
    {
        var config = await configStore.LoadAsync(cancellationToken);
        var sources = config.IpSources.For(family);
        var errors = new List<string>();

        if (sources.Count == 0)
        {
            errors.Add($"no {family} sources configured");
            return new IpDetection(family, null, null, errors);
        }

        var client = httpClientFactory.CreateClient(ClientName(family));
        foreach (var source in sources)
        {
            try
            {
                var address = await FetchAsync(client, source, family, cancellationToken);
                logger.LogDebug("{Family} address {Address} obtained from {Source}", family, address, source.Url);
                return new IpDetection(family, address, source.Url, errors);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                var message = $"{source.Url}: {Describe(ex)}";
                errors.Add(message);
                logger.LogDebug("{Family} source failed: {Error}", family, message);
            }
        }

        return new IpDetection(family, null, null, errors);
    }

    private static async Task<IPAddress> FetchAsync(HttpClient client, IpSourceSettings source, IpFamily family, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, source.Url);
        request.Headers.UserAgent.ParseAdd("FlareSync");
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        var text = ParseBody(body, source.JsonField);
        if (!IPAddress.TryParse(text, out var address))
        {
            throw new FormatException($"response is not an IP address: '{Truncate(text)}'");
        }

        if (IpAddressRules.Validate(address, family) is { } error)
        {
            throw new FormatException(error);
        }

        return address;
    }

    internal static string ParseBody(string body, string? jsonField)
    {
        if (string.IsNullOrWhiteSpace(jsonField))
        {
            return body.Trim();
        }

        using var document = JsonDocument.Parse(body);
        var element = document.RootElement;
        foreach (var segment in jsonField.Split('.'))
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(segment, out element))
            {
                throw new FormatException($"JSON field '{jsonField}' not found in response");
            }
        }

        return element.ValueKind == JsonValueKind.String ? element.GetString()!.Trim() : element.ToString();
    }

    private static string Describe(Exception ex) => ex switch
    {
        OperationCanceledException => "timed out",
        HttpRequestException { StatusCode: { } status } => $"HTTP {(int)status}",
        // Socket messages come localized from the OS; the error code is stable and in English.
        HttpRequestException { InnerException: SocketException socket } => $"network error ({socket.SocketErrorCode})",
        HttpRequestException http => http.InnerException?.Message ?? http.Message,
        _ => ex.Message,
    };

    private static string Truncate(string text) => text.Length <= 60 ? text : text[..60] + "...";
}
