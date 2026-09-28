using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;

namespace FlareSync.Core.IpResolution;

/// <summary>Parses DNS server notations: <c>1.1.1.1</c>, <c>1.1.1.1:53</c>, <c>2606:4700::1111</c>, <c>[2606:4700::1111]:53</c>.</summary>
public static class DnsServerEndpoint
{
    public const int DefaultPort = 53;

    public static bool TryParse(string? value, [NotNullWhen(true)] out IPEndPoint? endpoint)
    {
        endpoint = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        string addressPart;
        string? portPart = null;

        if (text.StartsWith('['))
        {
            var close = text.IndexOf(']');
            if (close < 0)
            {
                return false;
            }

            addressPart = text[1..close];
            var rest = text[(close + 1)..];
            if (rest.Length > 0)
            {
                if (!rest.StartsWith(':'))
                {
                    return false;
                }

                portPart = rest[1..];
            }
        }
        else if (text.Count(c => c == ':') == 1)
        {
            var colon = text.IndexOf(':');
            addressPart = text[..colon];
            portPart = text[(colon + 1)..];
        }
        else
        {
            addressPart = text;
        }

        if (!IPAddress.TryParse(addressPart, out var address))
        {
            return false;
        }

        var port = DefaultPort;
        if (portPart is not null
            && (!int.TryParse(portPart, NumberStyles.None, CultureInfo.InvariantCulture, out port) || port is < 1 or > 65535))
        {
            return false;
        }

        endpoint = new IPEndPoint(address, port);
        return true;
    }

    /// <summary>Canonical text form used when storing servers (<c>1.1.1.1:53</c>, <c>[::1]:53</c>).</summary>
    public static string Format(IPEndPoint endpoint) => endpoint.ToString();
}
