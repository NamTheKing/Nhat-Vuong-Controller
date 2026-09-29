using System.Text.Json;
using NhatVuong.Contracts;
using NhatVuong.Contracts.Api;

namespace NhatVuong.Client.Services;

/// <summary>The signed-in user. The bearer token lives in platform secure storage, never in preferences or files.</summary>
public sealed class SessionService
{
    private const string TokenKey = "access_token";
    private const string UserKey = "current_user";
    private const string ExpiryKey = "token_expiry";

    public string? AccessToken { get; private set; }

    public UserDto? User { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public bool IsSignedIn => AccessToken is not null && User is not null && ExpiresAt > DateTimeOffset.UtcNow;

    public event EventHandler? Expired;

    public async Task StartAsync(LoginResponse login)
    {
        AccessToken = login.AccessToken;
        User = login.User;
        ExpiresAt = login.ExpiresAt;
        await SecureStorage.Default.SetAsync(TokenKey, login.AccessToken);
        await SecureStorage.Default.SetAsync(UserKey, JsonSerializer.Serialize(login.User, NvcJson.Options));
        await SecureStorage.Default.SetAsync(ExpiryKey, login.ExpiresAt.ToString("O"));
    }

    public async Task<bool> TryResumeAsync()
    {
        try
        {
            var token = await SecureStorage.Default.GetAsync(TokenKey);
            var user = await SecureStorage.Default.GetAsync(UserKey);
            var expiry = await SecureStorage.Default.GetAsync(ExpiryKey);
            if (token is null || user is null || !DateTimeOffset.TryParse(expiry, out var expiresAt) || expiresAt <= DateTimeOffset.UtcNow.AddMinutes(5))
            {
                return false;
            }

            AccessToken = token;
            User = JsonSerializer.Deserialize<UserDto>(user, NvcJson.Options);
            ExpiresAt = expiresAt;
            return User is not null;
        }
        catch (Exception)
        {
            // Secure storage can be unavailable (e.g. keystore reset); simply sign in again.
            return false;
        }
    }

    public void SignOut()
    {
        AccessToken = null;
        User = null;
        SecureStorage.Default.Remove(TokenKey);
        SecureStorage.Default.Remove(UserKey);
        SecureStorage.Default.Remove(ExpiryKey);
    }

    public void NotifyExpired()
    {
        if (AccessToken is null)
        {
            return;
        }

        SignOut();
        Expired?.Invoke(this, EventArgs.Empty);
    }

    public bool IsIn(params UserRole[] roles) => User is not null && roles.Contains(User.Role);
}
