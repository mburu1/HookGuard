using System.Globalization;
using System.Net.Sockets;
using HookGuard.Application.Abstractions;

namespace HookGuard.Infrastructure.Health;

/// <summary>
/// Protocol-agnostic reachability probe for a <c>host:port</c> endpoint.
/// </summary>
/// <remarks>
/// A TCP connect proves the dependency is listening and accepting connections, which is all the
/// readiness endpoint needs. As the real clients arrive (Npgsql, StackExchange.Redis, the RabbitMQ
/// client in phases 1 to 3) they bring their own protocol-aware health checks and this probe is
/// retired for those dependencies.
/// </remarks>
public sealed class TcpReadinessProbe : IReadinessProbe
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);

    private readonly string _host;
    private readonly int _port;
    private readonly TimeSpan _timeout;

    public TcpReadinessProbe(string name, string endpoint, TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name;
        (_host, _port) = ParseEndpoint(endpoint);
        _timeout = timeout ?? DefaultTimeout;
    }

    public string Name { get; }

    public async Task<ReadinessResult> CheckAsync(CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);

        try
        {
            await client.ConnectAsync(_host, _port, timeout.Token);
            return ReadinessResult.Healthy();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ReadinessResult.Unhealthy($"timed out after {_timeout.TotalMilliseconds:0} ms");
        }
        catch (SocketException ex)
        {
            return ReadinessResult.Unhealthy(ex.Message);
        }
    }

    private static (string Host, int Port) ParseEndpoint(string endpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);

        var separator = endpoint.LastIndexOf(':');
        var host = separator > 0 ? endpoint[..separator] : null;
        var portText = separator > 0 ? endpoint[(separator + 1)..] : null;

        if (host is null
            || !int.TryParse(portText, CultureInfo.InvariantCulture, out var port)
            || port is <= 0 or > 65535)
        {
            throw new FormatException($"Endpoint '{endpoint}' must be in host:port form.");
        }

        return (host, port);
    }
}
