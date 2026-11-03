using APIHandler.Models.GroupReservation;
using APIHandler.Models.GuestSessions;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace APIHandler.Services.OwnerPreOrders;

public interface IOwnerPreOrderService
{
    Task<GetAllSessionsResponse> GetSessionsAsync(string userId, string groupReservationId);
    Task<GetOwnerSessionIdResponse> GetOwnerSessionIdAsync(string userId, string reservationId);
    Task<IReadOnlyList<ConsolidatedOrderItemResponse>> SubmitAsync(string userId, string groupReservationId, string reservationId);
    Task<GetAllSessionsResponse> DeleteSessionAsync(string userId, string groupReservationId, string guestSessionId);
}
