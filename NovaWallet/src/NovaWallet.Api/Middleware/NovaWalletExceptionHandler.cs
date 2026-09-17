using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using NovaWallet.Domain.Exceptions;

namespace NovaWallet.Api.Middleware;

/// <summary>
/// Translates domain exceptions into RFC 7807 Problem Details responses with
/// appropriate status codes, so callers get a consistent, structured error
/// shape instead of raw 500s and stack traces.
/// </summary>
public class NovaWalletExceptionHandler : IExceptionHandler
{
    private readonly ILogger<NovaWalletExceptionHandler> _logger;

    public NovaWalletExceptionHandler(ILogger<NovaWalletExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        var (statusCode, title) = exception switch
        {
            WalletNotFoundException => (StatusCodes.Status404NotFound, "Wallet not found"),
            CustomerAlreadyHasWalletException => (StatusCodes.Status409Conflict, "Customer already has a wallet"),
            InsufficientFundsException => (StatusCodes.Status422UnprocessableEntity, "Insufficient funds"),
            DailyLimitExceededException => (StatusCodes.Status422UnprocessableEntity, "Daily transfer limit exceeded"),
            InvalidTransferException => (StatusCodes.Status400BadRequest, "Invalid transfer"),
            InvalidAmountException => (StatusCodes.Status400BadRequest, "Invalid amount"),
            IdempotencyKeyConflictException => (StatusCodes.Status409Conflict, "Idempotency key conflict"),
            IdempotencyKeyMissingException => (StatusCodes.Status400BadRequest, "Idempotency-Key header required"),
            _ => (0, string.Empty)
        };

        if (statusCode == 0)
        {
            // Unrecognized exception - log with full detail, but never leak
            // internals to the caller.
            _logger.LogError(exception, "Unhandled exception processing {Path}", httpContext.Request.Path);
            statusCode = StatusCodes.Status500InternalServerError;
            title = "An unexpected error occurred";
        }
        else
        {
            _logger.LogWarning(exception, "{Title} on {Path}", title, httpContext.Request.Path);
        }

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = statusCode == StatusCodes.Status500InternalServerError ? null : exception.Message,
            Type = $"https://novawallet.firstbank.example/problems/{title.Replace(' ', '-').ToLowerInvariant()}",
            Instance = httpContext.Request.Path
        };
        problemDetails.Extensions["correlationId"] = httpContext.TraceIdentifier;

        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/problem+json";
        await httpContext.Response.WriteAsJsonAsync(problemDetails, ct);

        return true;
    }
}
