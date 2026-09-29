using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using NhatVuong.Application;
using NhatVuong.Domain;
using NhatVuong.Domain.Entities;

namespace NhatVuong.Adapters.Rest.Security;

public sealed class AuthOptions
{
    public const string Section = "Auth";

    public string Issuer { get; set; } = "nhatvuong-controller";

    public string Audience { get; set; } = "nhatvuong-clients";

    /// <summary>HMAC-SHA256 key, at least 32 bytes. From the environment in production; never committed.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public int TokenLifetimeHours { get; set; } = 8;

    /// <summary>Sign-in attempts allowed per client IP per minute (brute-force protection).</summary>
    public int LoginAttemptsPerMinute { get; set; } = 20;
}

public static class Policies
{
    public const string Administrator = "administrator";
    public const string Maintenance = "maintenance";
}

public static class NvcClaims
{
    public const string Subject = "sub";
    public const string Name = "name";
    public const string Email = "email";
    public const string Role = "role";
}

/// <summary>Issues bearer tokens after a successful sign-in (US-01). The subject is the internal user id (F-1).</summary>
public sealed class TokenService(AuthOptions options, TimeProvider time)
{
    public (string Token, DateTimeOffset ExpiresAt) Issue(User user)
    {
        var now = time.GetUtcNow();
        var expires = now.AddHours(options.TokenLifetimeHours);
        var credentials = new SigningCredentials(AuthSetup.SigningKey(options), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            options.Issuer,
            options.Audience,
            [
                new Claim(NvcClaims.Subject, user.Id.ToString()),
                new Claim(NvcClaims.Name, user.FullName),
                new Claim(NvcClaims.Email, user.Email),
                new Claim(NvcClaims.Role, user.Role.ToString()),
            ],
            now.UtcDateTime,
            expires.UtcDateTime,
            credentials);
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}

public static class AuthSetup
{
    public static SymmetricSecurityKey SigningKey(AuthOptions options)
    {
        var bytes = Encoding.UTF8.GetBytes(options.SigningKey);
        if (bytes.Length < 32)
        {
            throw new InvalidOperationException("Auth:SigningKey must be at least 32 bytes; supply it via configuration or environment.");
        }

        return new SymmetricSecurityKey(bytes);
    }

    public static IServiceCollection AddNvcAuthentication(this IServiceCollection services, AuthOptions options)
    {
        services.AddSingleton(options);
        services.AddSingleton<TokenService>();
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwt =>
            {
                jwt.MapInboundClaims = false;
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = options.Issuer,
                    ValidAudience = options.Audience,
                    IssuerSigningKey = SigningKey(options),
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                    NameClaimType = NvcClaims.Name,
                    RoleClaimType = NvcClaims.Role,
                };
            });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(Policies.Administrator, p => p.RequireRole(nameof(UserRole.Administrator)))
            .AddPolicy(Policies.Maintenance, p => p.RequireRole(nameof(UserRole.MaintenanceStaff), nameof(UserRole.Administrator)));
        return services;
    }

    /// <summary>The authenticated caller as an application actor. The role comes from the signed token, not the request body.</summary>
    public static Actor ToActor(this ClaimsPrincipal principal)
    {
        var id = Guid.Parse(principal.FindFirstValue(NvcClaims.Subject) ?? throw new InvalidOperationException("Missing subject claim."));
        var role = Enum.Parse<UserRole>(principal.FindFirstValue(NvcClaims.Role) ?? throw new InvalidOperationException("Missing role claim."));
        return new Actor(id, principal.FindFirstValue(NvcClaims.Name) ?? string.Empty, role);
    }
}
