using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Helpers;
using APIHandler.Services.OwnerPreOrders;
using System.Threading.Tasks;

namespace APIHandler.Handlers.GroupReservations;

public class GetOwnerSessionIdHandler(IOwnerPreOrderService ownerPreOrderService) : IApiRequestHandler
{
    public async Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
    {
        if (!OwnerPreOrderHttp.TryGetUserId(request, context, out var userId, out var errorResponse))
            return errorResponse;

        if (!OwnerPreOrderHttp.TryGetPathValue(request, context, "reservationId", out var reservationId, out errorResponse))
            return errorResponse;

        return await OwnerPreOrderHttp.ExecuteAsync(
            () => ownerPreOrderService.GetOwnerSessionIdAsync(userId, reservationId), context);
    }
}
