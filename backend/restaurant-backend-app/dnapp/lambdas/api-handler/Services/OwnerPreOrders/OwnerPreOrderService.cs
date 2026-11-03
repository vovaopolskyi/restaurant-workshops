using APIHandler.Constants.Orders;
using APIHandler.Entities;
using APIHandler.Exceptions;
using APIHandler.Mappers;
using APIHandler.Models.GroupReservation;
using APIHandler.Models.GuestSessions;
using APIHandler.Models.Orders;
using APIHandler.Repositories.Dishes;
using APIHandler.Repositories.GroupPreOrders;
using APIHandler.Repositories.GroupReservations;
using APIHandler.Repositories.GuestSessions;
using APIHandler.Repositories.Orders;
using APIHandler.Repositories.Reservations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace APIHandler.Services.OwnerPreOrders;

public class OwnerPreOrderService(
    IGroupReservationRepository groupReservationRepository,
    IReservationRepository reservationRepository,
    IGuestSessionRepository guestSessionRepository,
    IDishRepository dishRepository,
    IOrderRepository orderRepository,
    IGroupPreOrderRepository groupPreOrderRepository) : IOwnerPreOrderService
{
    public async Task<GetAllSessionsResponse> GetSessionsAsync(string userId, string groupReservationId)
    {
        await GetOwnedGroupAsync(userId, groupReservationId);
        return await BuildSnapshotAsync(await LoadSessionsAsync(groupReservationId));
    }

    public async Task<GetOwnerSessionIdResponse> GetOwnerSessionIdAsync(string userId, string reservationId)
    {
        await GetOwnedReservationAsync(userId, reservationId);
        var group = await groupReservationRepository.GetByReservationIdAsync(reservationId)
            ?? throw new GroupReservationNotFoundException();
        if (group.ReservationId != reservationId)
            throw new ValidationException("Group reservation does not belong to this reservation.");

        var sessions = await LoadSessionsAsync(group.GroupReservationId);
        var ownerSession = sessions.FirstOrDefault(session => session.IsOwnerSession && session.OwnerUserId == userId)
            ?? throw new GuestSessionNotFoundException("Owner session was not found for this reservation.");
        return new GetOwnerSessionIdResponse(ownerSession.Id);
    }

    public async Task<IReadOnlyList<ConsolidatedOrderItemResponse>> SubmitAsync(
        string userId, string groupReservationId, string reservationId)
    {
        await GetOwnedReservationAsync(userId, reservationId);
        var group = await groupReservationRepository.GetGroupReservationByIdAsync(groupReservationId)
            ?? throw new GroupReservationNotFoundException();
        if (group.ReservationId != reservationId)
            throw new ValidationException("Group reservation does not belong to this reservation.");
        EnsureOpen(group);

        var submittedSessions = (await LoadSessionsAsync(groupReservationId))
            .Where(session => session.IsSubmitted).ToArray();
        if (submittedSessions.Length == 0)
            throw new ValidationException("There are no submitted guest sessions.");

        var items = submittedSessions.SelectMany(session => session.Dishes ?? []).ToArray();
        if (items.Length == 0)
            throw new ValidationException("Submitted guest sessions contain no dishes.");
        ValidateItems(items);

        var order = await orderRepository.GetOrderByReservationIdAsync(reservationId)
            ?? throw new ValidationException("Order was not found for this reservation.");
        if (order.CustomerId != userId)
            throw new ActionForbiddenException("Only the order owner can submit this pre-order.");
        if (order.ReservationId != reservationId)
            throw new ValidationException("Order does not belong to this reservation.");
        if (order.Status != OrderStatus.Created)
            throw new ValidationException("Only a created order can be submitted.");

        var consolidated = Consolidate(items);
        var existing = order.Dishes ?? [];
        ValidateItems(existing);
        var merged = Consolidate(existing.Concat(items));
        var existingStates = existing.GroupBy(item => item.DishId)
            .ToDictionary(grouping => grouping.Key, grouping => grouping.First().State);

        // Keep the loaded entities unchanged if the transaction fails.
        var submittedOrder = new Order
        {
            OrderId = order.OrderId,
            CustomerId = order.CustomerId,
            ReservationId = order.ReservationId,
            ReservationAddress = order.ReservationAddress,
            ReservationTime = order.ReservationTime,
            Status = OrderStatus.SubmittedByCustomer,
            Dishes = merged.Select(item => new OrderItem
            {
                DishId = item.DishId,
                Quantity = item.Quantity,
                State = existingStates.GetValueOrDefault(item.DishId, OrderItemState.Pending)
            }).ToList()
        };
        var submittedGroup = new GroupReservation
        {
            GroupReservationId = group.GroupReservationId,
            ReservationId = group.ReservationId,
            ReservationToken = group.ReservationToken,
            Status = GroupReservationStatus.Submitted
        };

        await groupPreOrderRepository.SubmitAsync(submittedOrder, submittedGroup, order.Status, existing, submittedSessions);
        return consolidated;
    }

    public async Task<GetAllSessionsResponse> DeleteSessionAsync(string userId, string groupReservationId, string guestSessionId)
    {
        var group = await GetOwnedGroupAsync(userId, groupReservationId);
        EnsureOpen(group);
        var session = await guestSessionRepository.GetByGuestSessionId(guestSessionId)
            ?? throw new GuestSessionNotFoundException($"Guest session '{guestSessionId}' was not found.");
        if (session.GroupReservationId != groupReservationId)
            throw new ValidationException("Guest session does not belong to this group reservation.");
        if (session.IsOwnerSession)
            throw new ValidationException("The owner session cannot be removed from the group.");

        // Exclude the deleted identity explicitly: the group index is eventually consistent.
        var remainingSessions = (await LoadSessionsAsync(groupReservationId))
            .Where(candidate => candidate.Id != guestSessionId).ToArray();
        var snapshot = await BuildSnapshotAsync(remainingSessions);
        await groupPreOrderRepository.DeleteSessionAsync(session, groupReservationId);
        return snapshot;
    }

    private async Task<Reservation> GetOwnedReservationAsync(string userId, string reservationId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ActionForbiddenException("An authenticated owner is required.");
        var reservation = await reservationRepository.GetReservationByIdAsync(reservationId)
            ?? throw new ReservationNotFoundException($"Reservation '{reservationId}' was not found.");
        if (reservation.CustomerId != userId)
            throw new ActionForbiddenException("Only the reservation owner can perform this action.");
        return reservation;
    }

    private async Task<GroupReservation> GetOwnedGroupAsync(string userId, string groupReservationId)
    {
        var group = await groupReservationRepository.GetGroupReservationByIdAsync(groupReservationId)
            ?? throw new GroupReservationNotFoundException();
        await GetOwnedReservationAsync(userId, group.ReservationId);
        return group;
    }

    private static void EnsureOpen(GroupReservation group)
    {
        if (group.Status != GroupReservationStatus.Open)
            throw new ValidationException("Only an open group reservation can be modified.");
    }

    private async Task<IReadOnlyList<GuestSession>> LoadSessionsAsync(string groupReservationId)
    {
        var candidates = await guestSessionRepository.GetAllByGroupReservationIdAsync(groupReservationId);
        // Hydrate from the base table so a stale index cannot submit an unsubmitted cart.
        var sessions = await Task.WhenAll(candidates.DistinctBy(session => session.Id)
            .Select(session => guestSessionRepository.GetByGuestSessionId(session.Id)));
        return sessions.Where(session => session != null && session.GroupReservationId == groupReservationId)
            .Select(session => session!).ToArray();
    }

    private async Task<GetAllSessionsResponse> BuildSnapshotAsync(IEnumerable<GuestSession> sessions)
    {
        var members = sessions.ToArray();
        var dishIds = members.SelectMany(session => session.Dishes ?? [])
            .Select(item => item.DishId).Distinct().ToArray();
        var dishes = dishIds.Length == 0
            ? new Dictionary<string, Dish>()
            : (await dishRepository.GetDishesByIdsAsync(dishIds)).ToDictionary(dish => dish.Id);

        var participants = members.Select(session =>
        {
            var cart = session.Dishes ?? [];
            ValidateItems(cart);
            var items = cart.Select(item =>
            {
                if (!dishes.TryGetValue(item.DishId, out var dish))
                    throw new ValidationException($"Dish '{item.DishId}' referenced by a session was not found.");
                return new OrderItemResponse { Dish = DishMapper.ToDishResponse(dish), Quantity = item.Quantity };
            }).ToArray();
            return new Participant(session.Id, session.IsOwnerSession ? ParticipantType.Owner : ParticipantType.Guest,
                session.GuestName, items, items.Sum(item => item.Quantity * item.Dish.Price), session.IsSubmitted);
        }).ToArray();
        return new GetAllSessionsResponse(participants, participants.Sum(participant => participant.Total));
    }

    private static void ValidateItems(IEnumerable<OrderItem> items)
    {
        if (items.Any(item => string.IsNullOrWhiteSpace(item.DishId) || item.Quantity <= 0))
            throw new ValidationException("Each order item requires a dish ID and a positive quantity.");
    }

    private static IReadOnlyList<ConsolidatedOrderItemResponse> Consolidate(IEnumerable<OrderItem> items)
    {
        try
        {
            return items.GroupBy(item => item.DishId)
                .Select(grouping => new ConsolidatedOrderItemResponse(grouping.Key, grouping.Sum(item => item.Quantity)))
                .ToArray();
        }
        catch (OverflowException)
        {
            throw new ValidationException("Consolidated dish quantity is too large.");
        }
    }
}
