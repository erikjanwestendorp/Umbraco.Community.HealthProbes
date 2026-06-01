using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Umbraco.Community.HealthProbes.Filters;

/// <summary>
/// An ASP.NET Core endpoint filter that restricts access to health probe endpoints based on IP allowlists.
/// </summary>
/// <remarks>
/// <para>
/// When no allowed networks are configured, all requests are passed through.
/// When one or more entries are configured, only requests whose remote IP address falls within one of
/// the specified CIDR ranges are allowed; all other requests receive a <c>403 Forbidden</c> response.
/// </para>
/// <para>
/// If the application runs behind a reverse proxy or Kubernetes ingress controller, the
/// <c>RemoteIpAddress</c> will reflect the proxy IP rather than the originating client.
/// Configure <c>ForwardedHeadersMiddleware</c> in your application and specify trusted proxies /
/// networks to ensure the real client IP is used for filtering.
/// </para>
/// <para>
/// When an allowlist is configured and <c>HttpContext.Connection.RemoteIpAddress</c> is
/// <see langword="null"/> (which can occur in some hosting environments), the request is blocked.
/// </para>
/// </remarks>
internal sealed class HealthProbeIpAllowlistFilter : IEndpointFilter
{
    private readonly ParsedNetwork[] _networks;
    private readonly ILogger<HealthProbeIpAllowlistFilter> _logger;

    private HealthProbeIpAllowlistFilter(
        ParsedNetwork[] networks,
        ILogger<HealthProbeIpAllowlistFilter> logger)
    {
        _networks = networks;
        _logger = logger;
    }

    /// <summary>
    /// Creates a <see cref="HealthProbeIpAllowlistFilter"/> instance from the raw <paramref name="allowedNetworks"/>
    /// configuration strings. The strings are parsed once here so that per-request handling only performs
    /// in-memory comparisons.
    /// </summary>
    internal static HealthProbeIpAllowlistFilter Create(
        string[] allowedNetworks,
        ILogger<HealthProbeIpAllowlistFilter> logger)
        => new(ParseNetworks(allowedNetworks), logger);

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        string endpointLabel = GetEndpointLabel(context.HttpContext);
        IPAddress? remoteIp = context.HttpContext.Connection.RemoteIpAddress;
        string remoteIpText = FormatRemoteIp(remoteIp);

        if (_networks.Length > 0 && (remoteIp is null || !IsAllowed(remoteIp, _networks)))
        {
            _logger.LogWarning(
                "Denied {HealthProbeEndpoint} request from {RemoteIpAddress}.",
                endpointLabel,
                remoteIpText);

            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        object? result = await next(context);

        if (result is IStatusCodeHttpResult { StatusCode: int value })
        {
            _logger.LogInformation(
                "Handled {HealthProbeEndpoint} request from {RemoteIpAddress} with status code {StatusCode}.",
                endpointLabel,
                remoteIpText,
                value);
        }
        else
        {
            _logger.LogInformation(
                "Handled {HealthProbeEndpoint} request from {RemoteIpAddress}.",
                endpointLabel,
                remoteIpText);
        }

        return result;
    }

    private static string GetEndpointLabel(HttpContext httpContext)
        => httpContext.GetEndpoint()?.DisplayName ?? "unknown health probe endpoint";

    private static string FormatRemoteIp(IPAddress? remoteIp)
    {
        if (remoteIp is null)
        {
            return "unknown";
        }

        return remoteIp.IsIPv4MappedToIPv6
            ? remoteIp.MapToIPv4().ToString()
            : remoteIp.ToString();
    }

    private static bool IsAllowed(IPAddress remoteIp, ParsedNetwork[] networks)
    {
        // Normalise IPv4-mapped IPv6 addresses (e.g. ::ffff:127.0.0.1) to plain IPv4.
        if (remoteIp.IsIPv4MappedToIPv6)
        {
            remoteIp = remoteIp.MapToIPv4();
        }

        foreach (ParsedNetwork network in networks)
        {
            if (network.Contains(remoteIp))
            {
                return true;
            }
        }

        return false;
    }

    private static ParsedNetwork[] ParseNetworks(string[] allowedNetworks)
    {
        var result = new List<ParsedNetwork>(allowedNetworks.Length);

        foreach (string entry in allowedNetworks)
        {
            if (ParsedNetwork.TryParse(entry, out ParsedNetwork network))
            {
                result.Add(network);
            }
        }

        return result.ToArray();
    }

    private readonly struct ParsedNetwork
    {
        private readonly IPAddress _networkAddress;
        private readonly int _prefixLength;

        private ParsedNetwork(IPAddress networkAddress, int prefixLength)
        {
            _networkAddress = networkAddress;
            _prefixLength = prefixLength;
        }

        internal static bool TryParse(string entry, out ParsedNetwork result)
        {
            int slashIndex = entry.IndexOf('/');

            if (slashIndex < 0)
            {
                // Plain IP address – treat as a host route (/32 for IPv4, /128 for IPv6).
                if (!IPAddress.TryParse(entry, out IPAddress? plain))
                {
                    result = default;
                    return false;
                }

                // Normalise IPv4-mapped IPv6 plain addresses.
                if (plain.IsIPv4MappedToIPv6)
                {
                    plain = plain.MapToIPv4();
                }

                int hostBits = plain.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? 128 : 32;
                result = new ParsedNetwork(plain, hostBits);
                return true;
            }

            string addressPart = entry[..slashIndex];
            string prefixLengthPart = entry[(slashIndex + 1)..];

            if (!IPAddress.TryParse(addressPart, out IPAddress? networkAddress) ||
                !int.TryParse(prefixLengthPart, out int prefixLength))
            {
                result = default;
                return false;
            }

            // Normalise IPv4-mapped IPv6 network addresses.
            if (networkAddress.IsIPv4MappedToIPv6)
            {
                networkAddress = networkAddress.MapToIPv4();
            }

            int totalBits = networkAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? 128 : 32;

            if (prefixLength < 0 || prefixLength > totalBits)
            {
                result = default;
                return false;
            }

            result = new ParsedNetwork(networkAddress, prefixLength);
            return true;
        }

        internal bool Contains(IPAddress remoteIp)
        {
            // Address families must match.
            if (remoteIp.AddressFamily != _networkAddress.AddressFamily)
            {
                return false;
            }

            byte[] remoteBytes = remoteIp.GetAddressBytes();
            byte[] networkBytes = _networkAddress.GetAddressBytes();

            int fullBytes = _prefixLength / 8;
            int remainingBits = _prefixLength % 8;

            // Compare full bytes.
            for (int i = 0; i < fullBytes; i++)
            {
                if (remoteBytes[i] != networkBytes[i])
                {
                    return false;
                }
            }

            // Compare the partial byte (if any).
            if (remainingBits > 0)
            {
                int mask = 0xFF << (8 - remainingBits);
                if ((remoteBytes[fullBytes] & mask) != (networkBytes[fullBytes] & mask))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
