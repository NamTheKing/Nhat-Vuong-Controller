using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NhatVuong.Adapters.Rest.Security;
using NhatVuong.Application.Administration;
using NhatVuong.Application.Identity;
using NhatVuong.Contracts.Api;
using Dom = NhatVuong.Domain;

namespace NhatVuong.Adapters.Rest.Endpoints;

internal static class IdentityEndpoints
{
    public const string LoginRateLimit = "login";

    public static void Map(RouteGroupBuilder api)
    {
        // US-01
        api.MapPost("/auth/login", async (LoginRequest request, AuthService auth, TokenService tokens, CancellationToken ct) =>
            {
                var user = await auth.ValidateCredentialsAsync(request.Email, request.Password, ct);
                if (user is null)
                {
                    return ProblemMapping.Problem(StatusCodes.Status401Unauthorized, "InvalidCredentials", "Email or password is incorrect.");
                }

                var (token, expiresAt) = tokens.Issue(user);
                return Results.Ok(new LoginResponse(token, expiresAt, user.ToDto()));
            })
            .AllowAnonymous()
            .RequireRateLimiting(LoginRateLimit);

        api.MapGet("/me", async (HttpContext http, AuthService auth, CancellationToken ct) =>
            (await auth.GetActiveUserAsync(http.User.ToActor().RequireUserId(), ct)).ToDto());

        // US-21: users
        var users = api.MapGroup("/users").RequireAuthorization(Policies.Administrator);
        users.MapGet("", async (ReferenceDataService data, CancellationToken ct) =>
            (await data.ListUsersAsync(ct)).Select(u => u.ToDto()));
        users.MapPost("", async (SaveUserRequest r, ReferenceDataService data, CancellationToken ct) =>
        {
            var user = await data.CreateUserAsync(r.Email, r.FullName, (Dom.UserRole)(int)r.Role, r.Password ?? string.Empty, ct);
            return Results.Created($"{ApiRoutes.Prefix}/users/{user.Id}", user.ToDto());
        });
        users.MapPut("/{id:guid}", async (Guid id, SaveUserRequest r, ReferenceDataService data, CancellationToken ct) =>
            (await data.UpdateUserAsync(id, r.Email, r.FullName, (Dom.UserRole)(int)r.Role, r.IsActive, r.Password, ct)).ToDto());
        users.MapDelete("/{id:guid}", async (Guid id, HttpContext http, ReferenceDataService data, CancellationToken ct) =>
        {
            var result = await data.DeleteUserAsync(id, http.User.ToActor(), ct);
            return new DeleteUserResponse(result.Deleted, result.Deactivated);
        });
    }
}
