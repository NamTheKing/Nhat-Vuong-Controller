using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NhatVuong.Adapters.Rest.Endpoints;
using NhatVuong.Adapters.Rest.Security;
using NhatVuong.Contracts;
using NhatVuong.Contracts.Api;

namespace NhatVuong.Adapters.Rest;

public static class RestExtensions
{
    public static IServiceCollection AddNvcRest(this IServiceCollection services, AuthOptions auth)
    {
        services.AddNvcAuthentication(auth);
        services.ConfigureHttpJsonOptions(o => NvcJson.Configure(o.SerializerOptions));
        services.AddProblemDetails();
        services.AddExceptionHandler<ProblemMapping>();
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(IdentityEndpoints.LoginRateLimit, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = Math.Max(1, auth.LoginAttemptsPerMinute), Window = TimeSpan.FromMinutes(1) }));
        });
        return services;
    }

    /// <summary>Maps the v1 REST API. Every endpoint requires an authenticated caller unless marked otherwise.</summary>
    public static IEndpointRouteBuilder MapNvcApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup(ApiRoutes.Prefix);
        IdentityEndpoints.Map(api);
        CampusEndpoints.Map(api);
        DeviceEndpoints.Map(api);
        ScheduleEndpoints.Map(api);
        OperationsEndpoints.Map(api);
        return app;
    }
}
