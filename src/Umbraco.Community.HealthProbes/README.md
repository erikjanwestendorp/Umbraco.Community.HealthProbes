# Umbraco.Community.HealthProbes

A community package project for building reusable health probes for Umbraco CMS.

## Usage

Register the health probe endpoints in `Program.cs`:

```csharp
app.UseUmbracoHealthProbes();
```

This maps three anonymous endpoints:

| Endpoint | Purpose |
|---|---|
| `GET /health/live` | Liveness probe – always returns `200 OK` |
| `GET /health/ready` | Readiness probe – returns `200 OK` when Umbraco runtime is at `Run` level, otherwise `503` |
| `GET /health/startup` | Startup probe – same semantics as readiness |

## IP Allowlist

To restrict access to the health endpoints, add a `HealthProbes` section under `Umbraco` in `appsettings.json`:

```json
{
  "Umbraco": {
    "HealthProbes": {
      "AllowedNetworks": [
        "127.0.0.1/32",
        "::1/128",
        "10.0.0.0/8"
      ]
    }
  }
}
```

When `AllowedNetworks` is empty (the default) all requests are allowed – preserving backward compatibility.
When one or more entries are present, only requests originating from those IP addresses or CIDR ranges are allowed; all other requests receive `403 Forbidden`.

Supported formats:

- IPv4 plain address: `192.168.1.1`
- IPv4 CIDR: `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`
- IPv6 plain address: `::1`
- IPv6 CIDR: `::1/128`, `fd00::/8`

### Environment variable configuration

The allowlist can also be configured via environment variables, which is convenient for Kubernetes ConfigMaps and Helm values:

```bash
Umbraco__HealthProbes__AllowedNetworks__0=127.0.0.1/32
Umbraco__HealthProbes__AllowedNetworks__1=10.0.0.0/8
```

### Reverse proxy / Kubernetes ingress

When the application runs behind a reverse proxy or a Kubernetes ingress controller, `HttpContext.Connection.RemoteIpAddress` reflects the proxy IP rather than the originating client IP. To ensure IP filtering works correctly:

1. Enable `ForwardedHeadersMiddleware` in your application.
2. Configure trusted proxies/networks in `ForwardedHeadersOptions.KnownProxies` or `KnownNetworks`.

Example:

```csharp
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
    // Add your ingress / load-balancer network here
    options.KnownNetworks.Add(new IPNetwork(IPAddress.Parse("10.0.0.0"), 8));
});

app.UseForwardedHeaders();
```

> **Note:** The package does **not** enable forwarded-headers middleware automatically. Always restrict `KnownProxies`/`KnownNetworks` to your actual infrastructure IP ranges. A misconfigured or unrestricted forwarded-headers setup allows clients to spoof their IP address by setting `X-Forwarded-For` headers directly, bypassing the allowlist.
