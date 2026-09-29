using NhatVuong.Domain;

namespace NhatVuong.Application;

/// <summary>Who raised an action. A system actor (scheduler) has no user id but is still authorised and audited (AD-1, AD-7).</summary>
public sealed record Actor(Guid? UserId, string Name, UserRole? Role)
{
    public bool IsSystem => UserId is null;

    public bool IsAdministrator => Role == UserRole.Administrator;

    public static Actor System(string name) => new(null, name, null);

    public Guid RequireUserId() =>
        UserId ?? throw new ForbiddenException("UserRequired", "This action requires a signed-in user.");
}
