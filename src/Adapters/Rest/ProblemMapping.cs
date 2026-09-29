using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NhatVuong.Application;

namespace NhatVuong.Adapters.Rest;

/// <summary>Maps application errors to RFC 9457 problem details with a stable <c>code</c> extension the client localises.</summary>
public sealed class ProblemMapping(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not AppException app)
        {
            return false;
        }

        var status = app switch
        {
            NotFoundException => StatusCodes.Status404NotFound,
            ConflictException => StatusCodes.Status409Conflict,
            ForbiddenException => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest,
        };

        httpContext.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = app.Code,
                Detail = app.Message,
                Extensions = { ["code"] = app.Code },
            },
        });
    }

    public static IResult Problem(int status, string code, string detail) =>
        Results.Problem(statusCode: status, title: code, detail: detail, extensions: new Dictionary<string, object?> { ["code"] = code });
}
