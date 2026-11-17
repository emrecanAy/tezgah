using Tezgah.Core.Results;
using Microsoft.AspNetCore.Http;

namespace Tezgah.Core.Web;

public static class ProblemResults
{
    /// <summary>
    /// Turns a business error into an RFC 9457 problem response. Clients show their own text for
    /// the stable <c>code</c>; <c>detail</c> is for developers reading the response.
    /// </summary>
    public static IResult ToProblem(this Error error)
    {
        var status = error.Kind switch
        {
            ErrorKind.Validation => StatusCodes.Status400BadRequest,
            ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError,
        };

        return Microsoft.AspNetCore.Http.Results.Problem(
            detail: error.Message,
            statusCode: status,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }

    /// <summary>200 with the value, or the problem response for the error.</summary>
    public static IResult ToHttp<T>(this Result<T> result) =>
        result.Match(value => Microsoft.AspNetCore.Http.Results.Ok(value), error => error.ToProblem());

    /// <summary>204, or the problem response for the error.</summary>
    public static IResult ToHttp(this Result result) =>
        result.IsSuccess ? Microsoft.AspNetCore.Http.Results.NoContent() : result.Error.ToProblem();
}
