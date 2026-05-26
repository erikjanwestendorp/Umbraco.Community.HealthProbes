using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Umbraco.Community.HealthProbes.Filters;

/// <summary>
/// An ASP.NET Core endpoint filter that restricts access to health probe endpoints based on IP allowlists.
/// </summary>
/// <remarks>
/// <para>
/// When <c>UmbracoHealthProbes:AllowedNetworks</c> is empty (the default), all requests are passed through.
/// When one or more entries are configured, only requests whose remote IP address falls within one of
/// the specified CIDR ranges are allowed; all other requests receive a <c>403 Forbidden</c> response.
/// </para>
/// <para>
/// If the application runs behind a reverse proxy or Kubernetes ingress controller, the
/// <c>RemoteIpAddress</c> will reflect the proxy IP rather than the originating client.
/// Configure <c>ForwardedHeadersMiddleware</c> in your application and specify trusted proxies /
/// networks to ensure the real client IP is used for filtering.
/// </para>
/// </remarks>
internal sealed class HealthProbeIpAllowlistFilter : IEndpointFilter
{
    private readonly IConfiguration _configuration;

    public HealthProbeIpAllowlistFilter(IConfiguration configuration)
        => _configuration = configuration;

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        string[] allowedNetworks = _configuration
            .GetSection($"{Constants.PackageName}:{nameof(UmbracoHealthProbeOptions.AllowedNetworks)}")
            .Get<string[]>() ?? [];

        if (allowedNetworks.Length == 0)
        {
            return await next(context);
        }

        IPAddress? remoteIp = context.HttpContext.Connection.RemoteIpAddress;

        if (remoteIp is null || !IsAllowed(remoteIp, allowedNetworks))
        {
            return Results.Forbid();
        }

        return await next(context);
    }

    private static bool IsAllowed(IPAddress remoteIp, string[] allowedNetworks)
    {
        // Normalise IPv4-mapped IPv6 addresses (e.g. ::ffff:127.0.0.1) to plain IPv4.
        if (remoteIp.IsIPv4MappedToIPv6)
        {
            remoteIp = remoteIp.MapToIPv4();
        }

        foreach (string network in allowedNetworks)
        {
            if (IsInNetwork(remoteIp, network))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInNetwork(IPAddress remoteIp, string network)
    {
        int slashIndex = network.IndexOf('/');

        if (slashIndex < 0)
        {
            // Plain IP address without CIDR prefix – exact match.
            return IPAddress.TryParse(network, out IPAddress? parsed) && remoteIp.Equals(parsed);
        }

        string addressPart = network[..slashIndex];
        string prefixLengthPart = network[(slashIndex + 1)..];

        if (!IPAddress.TryParse(addressPart, out IPAddress? networkAddress))
        {
            return false;
        }

        if (!int.TryParse(prefixLengthPart, out int prefixLength))
        {
            return false;
        }

        // Address families must match.
        if (remoteIp.AddressFamily != networkAddress.AddressFamily)
        {
            return false;
        }

        byte[] remoteBytes = remoteIp.GetAddressBytes();
        byte[] networkBytes = networkAddress.GetAddressBytes();

        int totalBits = remoteBytes.Length * 8;

        if (prefixLength < 0 || prefixLength > totalBits)
        {
            return false;
        }

        int fullBytes = prefixLength / 8;
        int remainingBits = prefixLength % 8;

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
