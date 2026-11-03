using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Exceptions;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace APIHandler.Helpers;

internal static class OwnerPreOrderHttp
{
    public static bool TryGetUserId(
        APIGatewayProxyRequest request,
        ILambdaContext context,
        out string userId,
        [NotNullWhen(false)] out APIGatewayProxyResponse? errorResponse)
    {
        userId = string.Empty;
        errorResponse = null;
        if (request.RequestContext?.Authorizer?.Claims is not null &&
            request.RequestContext.Authorizer.Claims.TryGetValue("sub", out var subject) &&
            !string.IsNullOrWhiteSpace(subject))
        {
            userId = subject;
            return true;
        }

        errorResponse = ResponseBuilder.CreateErrorResponse(401, "Unauthorized.", context.Logger);
        return false;
    }

    public static bool TryGetPathValue(
        APIGatewayProxyRequest request,
        ILambdaContext context,
        string name,
        out string value,
        [NotNullWhen(false)] out APIGatewayProxyResponse? errorResponse)
    {
        value = string.Empty;
        errorResponse = null;
        if (request.PathParameters is not null &&
            request.PathParameters.TryGetValue(name, out var pathValue) &&
            !string.IsNullOrWhiteSpace(pathValue))
        {
            value = pathValue;
            return true;
        }

        errorResponse = ResponseBuilder.CreateErrorResponse(
            400, $"Missing '{name}' in path parameters.", context.Logger);
        return false;
    }

    public static async Task<APIGatewayProxyResponse> ExecuteAsync<T>(
        Func<Task<T>> operation,
        ILambdaContext context) where T : notnull
    {
        try
        {
            return ResponseBuilder.CreateJsonResponse(200, await operation());
        }
        catch (Exception exception) when (
            exception is GroupReservationNotFoundException or
            ReservationNotFoundException or
            GuestSessionNotFoundException)
        {
            return ResponseBuilder.CreateErrorResponse(404, exception.Message, context.Logger);
        }
        catch (ActionForbiddenException exception)
        {
            return ResponseBuilder.CreateErrorResponse(403, exception.Message, context.Logger);
        }
        catch (ValidationException exception)
        {
            return ResponseBuilder.CreateErrorCodeResponse(exception);
        }
    }
}
