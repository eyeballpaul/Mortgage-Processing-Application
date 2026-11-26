using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using PD.Mortgages.Contracts;
using PD.Mortgages.Application.Services;

namespace PD.Mortgages.Api.Middleware;

public sealed class MortgageApiExceptionHandler(ILogger<MortgageApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var status = exception switch
        {
            KeyNotFoundException => StatusCodes.Status404NotFound,
            UnauthorizedAccessException => StatusCodes.Status403Forbidden,
            ArgumentException => StatusCodes.Status400BadRequest,
            EligibilityUnavailableException => StatusCodes.Status503ServiceUnavailable,
            MortgageConcurrencyException => StatusCodes.Status409Conflict,
            InvalidOperationException => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };
        logger.LogError(exception, "Mortgage API request failed with status {Status}", status);
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Type = exception switch
            {
                UnauthorizedAccessException => MortgageApiProblems.ApplicationUnavailable,
                KeyNotFoundException => MortgageApiProblems.ApplicationNotFound,
                MortgageConcurrencyException => MortgageApiProblems.ConcurrencyConflict,
                _ => null
            },
            Title = status == StatusCodes.Status500InternalServerError ? "Internal server error" : "Mortgage operation failed",
            Detail = status == StatusCodes.Status500InternalServerError ? "The operation could not be completed." : exception.Message
        }, options: null, contentType: "application/problem+json", cancellationToken: cancellationToken);
        return true;
    }
}
