using System.Net.Sockets;
using FlareSync.Core.Abstractions;
using FlareSync.Core.Models;

namespace FlareSync.Core.IpResolution;

/// <summary>
/// Creates HTTP handlers that resolve host names through <see cref="IHostResolver"/> and connect only over one
/// address family, so dual-stack IP sources answer with the address of the requested family.
/// </summary>
public static class FamilyBoundHandler
{
    public static SocketsHttpHandler Create(IHostResolver resolver, IpFamily family) => new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(1),
        ConnectCallback = async (context, cancellationToken) =>
        {
            var host = context.DnsEndPoint.Host;
            var addresses = await resolver.ResolveAsync(host, family, cancellationToken);
            if (addresses.Length == 0)
            {
                throw new HttpRequestException($"no {family} address found for '{host}'");
            }

            var socket = new Socket(family.ToAddressFamily(), SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };
}
