using NhatVuong.Application.Abstractions;
using NhatVuong.Domain.Entities;

namespace NhatVuong.Application.Identity;

/// <summary>Sign-in with the university account (US-01). Passwords are BCrypt hashes only (NFR-03).</summary>
public sealed class AuthService(IDataStore store)
{
    public const int WorkFactor = 11;

    // Verified against when the email is unknown, so response time does not reveal which emails exist.
    private static readonly string DummyHash = BCrypt.Net.BCrypt.HashPassword("nvc-timing-equaliser", WorkFactor);

    public static string HashPassword(string password) => BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public async Task<User?> ValidateCredentialsAsync(string email, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
        {
            return null;
        }

        var normalized = NormalizeEmail(email);
        var user = await store.Query<User>().Where(u => u.Email == normalized).FirstOrDefaultAsync(ct);
        var valid = BCrypt.Net.BCrypt.Verify(password, user?.PasswordHash ?? DummyHash);
        return valid && user is { IsActive: true } ? user : null;
    }

    public async Task<User> GetActiveUserAsync(Guid userId, CancellationToken ct = default) =>
        await store.Query<User>().Where(u => u.Id == userId && u.IsActive).FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException("UserNotFound", "User not found or inactive.");
}
