using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using VehicleRental.Application.Exceptions;

namespace VehicleRental.Api.Http;

/// <summary>
/// The single place that turns exceptions into HTTP responses (RFC 9457 Problem Details).
/// Application errors become 400, 404 or 409; anything else is a 500 with a generic message, so no
/// stack trace, SQL, connection detail or file path ever reaches a client.
/// </summary>
internal sealed partial class ApiExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetails;
    private readonly ILogger<ApiExceptionHandler> _logger;
    private readonly IHostEnvironment _environment;

    public ApiExceptionHandler(
        IProblemDetailsService problemDetails,
        ILogger<ApiExceptionHandler> logger,
        IHostEnvironment environment)
    {
        _problemDetails = problemDetails;
        _logger = logger;
        _environment = environment;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // The client went away; there is nobody to answer and nothing went wrong.
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            httpContext.Response.StatusCode = 499;
            return true;
        }

        (int status, string title, string? detail) = Classify(exception);

        if (status >= 500)
        {
            _logger.LogError(exception, "Unhandled exception while processing {Method} {Path}",
                httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            _logger.LogInformation("Request failed with {Status}: {Title}", status, title);
        }

        httpContext.Response.StatusCode = status;

        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = detail },
            Exception = null // never let the framework add exception details to the response
        });
    }

    /// <summary>
    /// Argument exceptions append framework text such as "(Parameter 'x')" and "Actual value was ...";
    /// clients only need the first sentence.
    /// </summary>
    private static string ClientMessage(string message)
    {
        string firstLine = message.Split('\n')[0].Trim();

        return ParameterSuffix().Replace(firstLine, "").Trim();
    }

    [GeneratedRegex(@"\s*\(Parameter '[^']*'\)")]
    private static partial Regex ParameterSuffix();

    private (int Status, string Title, string? Detail) Classify(Exception exception) => exception switch
    {
        NotFoundException => (StatusCodes.Status404NotFound, "The resource was not found.", exception.Message),
        ConflictException => (StatusCodes.Status409Conflict, "The request conflicts with the current state.", exception.Message),
        BadHttpRequestException bad => (bad.StatusCode, "The request could not be read.", "The request body or parameters are malformed."),
        // Argument errors come from domain validation; their messages are written for clients.
        ArgumentException => (StatusCodes.Status400BadRequest, "The request is invalid.", ClientMessage(exception.Message)),
        _ => (
            StatusCodes.Status500InternalServerError,
            "An unexpected error occurred.",
            // Details only while developing; production responses stay generic.
            _environment.IsDevelopment() ? exception.Message : null)
    };
}
