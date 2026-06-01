using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.HealthProbes.Filters;

namespace Umbraco.Community.HealthProbes.Extensions;

public static class UmbracoHealthProbeExtensions
{
    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>
        /// Maps Kubernetes liveness, readiness, and startup probe endpoints for an Umbraco application.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Options are read from the <c>Umbraco:HealthProbes</c> configuration section. When
        /// <c>AllowedNetworks</c> is empty (the default) all requests are allowed. When one or more
        /// entries are configured only requests from those IP addresses or CIDR ranges are allowed;
        /// all others receive <c>403 Forbidden</c>.
        /// </para>
        /// <para>
        /// If the application runs behind a reverse proxy or Kubernetes ingress controller, configure
        /// <c>ForwardedHeadersMiddleware</c> and trusted proxies so that the real client IP is
        /// available on <c>HttpContext.Connection.RemoteIpAddress</c> before the filter runs.
        /// The package does not enable forwarded-headers middleware automatically.
        /// </para>
        /// </remarks>
        /// <param name="livePath">The liveness endpoint path.</param>
        /// <param name="readyPath">The readiness endpoint path.</param>
        /// <param name="startupPath">The startup endpoint path.</param>
        /// <returns>The endpoint route builder.</returns>
        public IEndpointRouteBuilder UseUmbracoHealthProbes(
            string livePath = "/health/live",
            string readyPath = "/health/ready",
            string startupPath = "/health/startup")
        {
            // Parse the allowlist once at startup so individual requests perform only in-memory checks.
            IConfiguration configuration = endpoints.ServiceProvider.GetRequiredService<IConfiguration>();
            string[] allowedNetworks = configuration
                .GetSection(Constants.ConfigurationSection)
                .Get<UmbracoHealthProbeOptions>()
                ?.AllowedNetworks ?? [];

            ILogger<HealthProbeIpAllowlistFilter> logger = endpoints.ServiceProvider.GetRequiredService<ILogger<HealthProbeIpAllowlistFilter>>();
            HealthProbeIpAllowlistFilter filter = HealthProbeIpAllowlistFilter.Create(allowedNetworks, logger);

            endpoints.MapGet(livePath, static () => Results.Ok("OK"))
                .AllowAnonymous()
                .AddEndpointFilter(filter);

            endpoints.MapGet(readyPath, ReadyCheck)
                .AllowAnonymous()
                .AddEndpointFilter(filter);

            endpoints.MapGet(startupPath, StartupCheck)
                .AllowAnonymous()
                .AddEndpointFilter(filter);

            return endpoints;
        }
    }

    private static IResult ReadyCheck(IRuntimeState runtimeState) =>
        runtimeState.Level == RuntimeLevel.Run
            ? Results.Ok("READY")
            : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

    // Umbraco is considered started only when the runtime reaches Run, matching readiness semantics.
    private static IResult StartupCheck(IRuntimeState runtimeState) =>
        runtimeState.Level == RuntimeLevel.Run
            ? Results.Ok("STARTED")
            : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
}
