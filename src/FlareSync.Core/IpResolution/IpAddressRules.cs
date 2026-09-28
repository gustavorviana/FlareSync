using System.Net;
using System.Net.Sockets;
using FlareSync.Core.Models;

namespace FlareSync.Core.IpResolution;

/// <summary>Validation of addresses returned by IP sources.</summary>
public static class IpAddressRules
{
    public static AddressFamily ToAddressFamily(this IpFamily family)
        => family == IpFamily.IPv4 ? AddressFamily.InterNetwork : AddressFamily.InterNetworkV6;

    /// <summary>Returns an error message when <paramref name="address"/> is not a public address of <paramref name="family"/>.</summary>
    public static string? Validate(IPAddress address, IpFamily family)
    {
        if (address.AddressFamily != family.ToAddressFamily())
        {
            return $"expected an {family} address but got '{address}'";
        }

        return IsPublic(address) ? null : $"'{address}' is not a public address";
    }

    public static bool IsPublic(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return !(b[0] == 0
                || b[0] == 10
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127) // 100.64.0.0/10 (CGNAT)
                || (b[0] == 169 && b[1] == 254)
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 192 && b[1] == 0 && b[2] == 2)      // TEST-NET-1
                || (b[0] == 198 && (b[1] == 18 || b[1] == 19))  // benchmarking
                || (b[0] == 198 && b[1] == 51 && b[2] == 100)   // TEST-NET-2
                || (b[0] == 203 && b[1] == 0 && b[2] == 113)    // TEST-NET-3
                || b[0] >= 224);                                // multicast and reserved
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = address.GetAddressBytes();
            return !(address.Equals(IPAddress.IPv6Any)
                || address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || address.IsIPv6UniqueLocal
                || address.IsIPv6Multicast
                || address.IsIPv4MappedToIPv6
                || (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0d && b[3] == 0xb8)); // 2001:db8::/32 documentation
        }

        return false;
    }
}
