using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace Tezgah.Api;

/// <summary>
/// A row-version check failed: somebody changed the same record first. That is the client's cue
/// to reload, not a server fault.
/// </summary>
internal sealed class ConcurrencyConflictHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is not DbUpdateConcurrencyException)
        {
            return false;
        }

        context.Response.StatusCode = StatusCodes.Status409Conflict;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails =
            {
                Status = StatusCodes.Status409Conflict,
                Detail = "The record changed while the request was being processed. Reload and try again.",
                Extensions = { ["code"] = "concurrent_update" },
            },
        });
    }
}
