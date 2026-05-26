namespace Umbraco.Community.HealthProbes;

/// <summary>
/// Configuration options for Umbraco health probe endpoints.
/// </summary>
public sealed class UmbracoHealthProbeOptions
{
    /// <summary>
    /// Gets or sets the list of allowed networks in CIDR notation (e.g. <c>10.0.0.0/8</c>, <c>127.0.0.1/32</c>, <c>::1/128</c>).
    /// </summary>
    /// <remarks>
    /// When empty (the default), all requests are allowed. When one or more entries are specified,
    /// only requests originating from the configured IP addresses or CIDR ranges are allowed;
    /// all other requests receive a <c>403 Forbidden</c> response.
    /// </remarks>
    public string[] AllowedNetworks { get; set; } = [];
}
