using APIHandler.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace APIHandler.Repositories.GroupPreOrders;

public interface IGroupPreOrderRepository
{
    Task SubmitAsync(Order order, GroupReservation group, OrderStatus expectedOrderStatus,
        IReadOnlyList<OrderItem> expectedOrderDishes, IReadOnlyList<GuestSession> submittedSessions);
    Task DeleteSessionAsync(GuestSession session, string groupReservationId);
}
