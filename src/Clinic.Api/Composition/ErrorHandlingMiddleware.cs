using System.Text.Json;
using Clinic.SharedKernel.Errors;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Clinic.Api.Composition;

/// <summary>Maps every failure to problem+json. Unexpected errors return only the traceId (500).</summary>
internal sealed class ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (AppException e)
        {
            await Problems.WriteAsync(context, e.Status, e.Code, e.FieldErrors);
        }
        catch (BadHttpRequestException e)
        {
            var status = e.StatusCode == StatusCodes.Status413PayloadTooLarge ? e.StatusCode : StatusCodes.Status400BadRequest;
            await Problems.WriteAsync(context, status, ErrorCodes.MalformedRequest);
        }
        catch (JsonException)
        {
            await Problems.WriteAsync(context, StatusCodes.Status400BadRequest, ErrorCodes.MalformedRequest);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await Problems.WriteAsync(context, StatusCodes.Status409Conflict, ErrorCodes.Conflict);
        }
        catch (DbUpdateConcurrencyException)
        {
            await Problems.WriteAsync(context, StatusCodes.Status412PreconditionFailed, ErrorCodes.ConcurrencyStale);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client went away; nothing to write.
        }
        catch (Exception e)
        {
            // Exception type only: messages may contain values.
            logger.LogError("Unhandled {ExceptionType} on {Method} {Route}", e.GetType().FullName,
                context.Request.Method, context.GetEndpoint()?.DisplayName ?? "unmatched");
            await Problems.WriteAsync(context, StatusCodes.Status500InternalServerError, ErrorCodes.InternalError);
        }
    }
}
