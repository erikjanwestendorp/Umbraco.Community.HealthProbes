using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Services;

namespace Umbraco.Community.HealthProbes.Extensions;

public static class UmbracoHealthProbeExtensions
{
    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>
        /// Maps Kubernetes liveness, readiness, and startup probe endpoints for an Umbraco application.
        /// </summary>
        /// <param name="livePath">The liveness endpoint path.</param>
        /// <param name="readyPath">The readiness endpoint path.</param>
        /// <param name="startupPath">The startup endpoint path.</param>
        /// <returns>The endpoint route builder.</returns>
        public IEndpointRouteBuilder UseUmbracoHealthProbes(
            string livePath = "/health/live",
            string readyPath = "/health/ready",
            string startupPath = "/health/startup")
        {
            endpoints.MapGet(livePath, static () => Results.Ok("OK"))
                .AllowAnonymous();

            endpoints.MapGet(readyPath, ReadyCheck)
                .AllowAnonymous();

            endpoints.MapGet(startupPath, StartupCheck)
                .AllowAnonymous();

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
