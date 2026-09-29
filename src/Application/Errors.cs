namespace NhatVuong.Application;

/// <summary>
/// Application failures carry a stable machine code; clients localise the code (NFR-07),
/// the message is a neutral fallback for logs and API consumers.
/// </summary>
public abstract class AppException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class NotFoundException(string code, string message) : AppException(code, message);

public sealed class ConflictException(string code, string message) : AppException(code, message);

public sealed class ValidationException(string code, string message) : AppException(code, message);

public sealed class ForbiddenException(string code, string message) : AppException(code, message);
