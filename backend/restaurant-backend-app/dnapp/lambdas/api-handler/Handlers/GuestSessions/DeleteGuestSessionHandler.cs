using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Helpers;
using APIHandler.Services.OwnerPreOrders;
using System.Threading.Tasks;

namespace APIHandler.Handlers.GuestSessions;

public class DeleteGuestSessionHandler(IOwnerPreOrderService ownerPreOrderService) : IApiRequestHandler
{
    public async Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
    {
        if (!OwnerPreOrderHttp.TryGetUserId(request, context, out var userId, out var errorResponse))
            return errorResponse;

        if (!OwnerPreOrderHttp.TryGetPathValue(request, context, "groupReservationId", out var groupReservationId, out errorResponse))
            return errorResponse;

        if (!OwnerPreOrderHttp.TryGetPathValue(request, context, "guestSessionId", out var guestSessionId, out errorResponse))
            return errorResponse;

        return await OwnerPreOrderHttp.ExecuteAsync(
            () => ownerPreOrderService.DeleteSessionAsync(userId, groupReservationId, guestSessionId), context);
    }
}
