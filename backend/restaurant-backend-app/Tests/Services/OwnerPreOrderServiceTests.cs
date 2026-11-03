using APIHandler.Constants;
using APIHandler.Constants.Dishes;
using APIHandler.Constants.Orders;
using APIHandler.Entities;
using APIHandler.Exceptions;
using APIHandler.Models.GuestSessions;
using APIHandler.Repositories.Dishes;
using APIHandler.Repositories.GroupPreOrders;
using APIHandler.Repositories.GroupReservations;
using APIHandler.Repositories.GuestSessions;
using APIHandler.Repositories.Orders;
using APIHandler.Repositories.Reservations;
using APIHandler.Services.OwnerPreOrders;
using Moq;

namespace APIHandler.Tests.Services;

public class OwnerPreOrderServiceTests
{
    private const string OwnerId = "owner-1";
    private const string GroupId = "group-1";
    private const string ReservationId = "reservation-1";
    private readonly Mock<IGroupReservationRepository> _groups = new();
    private readonly Mock<IReservationRepository> _reservations = new();
    private readonly Mock<IGuestSessionRepository> _sessions = new();
    private readonly Mock<IDishRepository> _dishes = new();
    private readonly Mock<IOrderRepository> _orders = new();
    private readonly Mock<IGroupPreOrderRepository> _submissions = new();
    private readonly GroupReservation _group = new()
    {
        GroupReservationId = GroupId,
        ReservationId = ReservationId,
        ReservationToken = "invite-token",
        Status = GroupReservationStatus.Open
    };
    private readonly Reservation _reservation = new()
    {
        Id = ReservationId,
        TableId = "table-1",
        WaiterId = "waiter-1",
        CustomerId = OwnerId,
        GuestCount = 5,
        Status = ReservationStatus.Reserved
    };
    private readonly Order _order = new()
    {
        OrderId = "order-1",
        CustomerId = OwnerId,
        ReservationId = ReservationId,
        ReservationAddress = "Demo restaurant, Table 1",
        ReservationTime = "12:00 - 13:00",
        Dishes = [],
        Status = OrderStatus.Created
    };
    private readonly OwnerPreOrderService _service;

    public OwnerPreOrderServiceTests()
    {
        _groups.Setup(repository => repository.GetGroupReservationByIdAsync(GroupId)).ReturnsAsync(_group);
        _groups.Setup(repository => repository.GetByReservationIdAsync(ReservationId)).ReturnsAsync(_group);
        _reservations.Setup(repository => repository.GetReservationByIdAsync(ReservationId)).ReturnsAsync(_reservation);
        _orders.Setup(repository => repository.GetOrderByReservationIdAsync(ReservationId)).ReturnsAsync(_order);
        _sessions.Setup(repository => repository.GetAllByGroupReservationIdAsync(GroupId)).ReturnsAsync([]);
        SetDishes(Dish("dish-1", 180m), Dish("dish-2", 220m));
        _service = new OwnerPreOrderService(_groups.Object, _reservations.Object, _sessions.Object,
            _dishes.Object, _orders.Object, _submissions.Object);
    }

    [Fact]
    public async Task GetSessions_IncludesDraftAndSubmittedParticipantsAndAssignmentTotals()
    {
        SetSessions(Session("session-1", true, Item("dish-1", 2)), Session("session-2", false, Item("dish-2", 1)));

        var snapshot = await _service.GetSessionsAsync(OwnerId, GroupId);

        var participants = snapshot.Participants.ToArray();
        Assert.Equal(2, participants.Length);
        Assert.Equal(360m, participants.Single(participant => participant.Id == "session-1").Total);
        Assert.True(participants.Single(participant => participant.Id == "session-1").IsSubmitted);
        Assert.Equal(220m, participants.Single(participant => participant.Id == "session-2").Total);
        Assert.False(participants.Single(participant => participant.Id == "session-2").IsSubmitted);
        Assert.Equal(580m, snapshot.Total);
        Assert.All(participants, participant => Assert.Equal(ParticipantType.Guest, participant.Type));
        _sessions.Verify(repository => repository.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Fact]
    public async Task GetSessions_UsesDecimalArithmeticAndIdentifiesOwnerParticipant()
    {
        var owner = Session("owner-session", true, Item("dish-1", 3));
        owner.IsOwnerSession = true;
        owner.OwnerUserId = OwnerId;
        SetSessions(owner, Session("guest-session", false, Item("dish-2", 1)));
        SetDishes(Dish("dish-1", 0.1m), Dish("dish-2", 0.2m));

        var snapshot = await _service.GetSessionsAsync(OwnerId, GroupId);

        Assert.Equal(0.5m, snapshot.Total);
        Assert.Equal(ParticipantType.Owner, snapshot.Participants.Single(participant => participant.Id == owner.Id).Type);
    }

    [Fact]
    public async Task GetSessions_EmptyGroupHasZeroTotal()
    {
        var snapshot = await _service.GetSessionsAsync(OwnerId, GroupId);

        Assert.Empty(snapshot.Participants);
        Assert.Equal(0m, snapshot.Total);
    }

    [Fact]
    public async Task GetSessions_RefreshesStaleIndexItemsFromBaseTable()
    {
        var stale = Session("session-1", false, Item("dish-1", 99));
        SetSessions(stale);
        var current = Session(stale.Id, true, Item("dish-1", 2));
        _sessions.Setup(repository => repository.GetByGuestSessionId(stale.Id)).ReturnsAsync(current);

        var snapshot = await _service.GetSessionsAsync(OwnerId, GroupId);

        Assert.Equal(360m, snapshot.Total);
        Assert.True(Assert.Single(snapshot.Participants).IsSubmitted);
    }

    [Fact]
    public async Task GetSessions_ExcludesIndexRowsAlreadyDeletedFromBaseTable()
    {
        var deleted = Session("deleted-session", true, Item("dish-1", 1));
        SetSessions(deleted);
        _sessions.Setup(repository => repository.GetByGuestSessionId(deleted.Id)).ReturnsAsync((GuestSession?)null);

        var snapshot = await _service.GetSessionsAsync(OwnerId, GroupId);

        Assert.Empty(snapshot.Participants);
        Assert.Equal(0m, snapshot.Total);
    }

    [Fact]
    public async Task GetSessions_MissingDishReturnsValidationError()
    {
        SetSessions(Session("session-1", false, Item("deleted-dish", 1)));

        await Assert.ThrowsAsync<ValidationException>(() => _service.GetSessionsAsync(OwnerId, GroupId));
    }

    [Theory]
    [InlineData("snapshot")]
    [InlineData("owner-session")]
    [InlineData("submit")]
    [InlineData("delete")]
    public async Task OwnerOperations_RejectAnotherUserWithoutWriting(string operation)
    {
        await Assert.ThrowsAsync<ActionForbiddenException>(() => InvokeOperation(operation, "another-user"));

        AssertNoWrites();
    }

    [Fact]
    public async Task GetSessions_MissingGroupReturnsNotFound()
    {
        _groups.Setup(repository => repository.GetGroupReservationByIdAsync(GroupId)).ReturnsAsync((GroupReservation?)null);

        await Assert.ThrowsAsync<GroupReservationNotFoundException>(() => _service.GetSessionsAsync(OwnerId, GroupId));
    }

    [Fact]
    public async Task GetSessions_MissingReservationReturnsNotFound()
    {
        _reservations.Setup(repository => repository.GetReservationByIdAsync(ReservationId)).ReturnsAsync((Reservation?)null);

        await Assert.ThrowsAsync<ReservationNotFoundException>(() => _service.GetSessionsAsync(OwnerId, GroupId));
    }

    [Fact]
    public async Task GetOwnerSessionId_SelectsMatchingOwnerInsteadOfFirstGuest()
    {
        var guest = Session("first-guest", false);
        var owner = Session("owner-session", false);
        owner.IsOwnerSession = true;
        owner.OwnerUserId = OwnerId;
        SetSessions(guest, owner);

        var response = await _service.GetOwnerSessionIdAsync(OwnerId, ReservationId);

        Assert.Equal(owner.Id, response.GuestSessionId);
        _sessions.Verify(repository => repository.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Fact]
    public async Task GetOwnerSessionId_MissingReservationReturnsNotFound()
    {
        _reservations.Setup(repository => repository.GetReservationByIdAsync(ReservationId)).ReturnsAsync((Reservation?)null);

        await Assert.ThrowsAsync<ReservationNotFoundException>(() => _service.GetOwnerSessionIdAsync(OwnerId, ReservationId));
    }

    [Fact]
    public async Task GetOwnerSessionId_MissingGroupReturnsNotFound()
    {
        _groups.Setup(repository => repository.GetByReservationIdAsync(ReservationId)).ReturnsAsync((GroupReservation?)null);

        await Assert.ThrowsAsync<GroupReservationNotFoundException>(() => _service.GetOwnerSessionIdAsync(OwnerId, ReservationId));
    }

    [Fact]
    public async Task GetOwnerSessionId_RejectsOwnerSessionBelongingToAnotherUser()
    {
        var owner = Session("wrong-owner-session", false);
        owner.IsOwnerSession = true;
        owner.OwnerUserId = "another-user";
        SetSessions(owner);

        await Assert.ThrowsAsync<GuestSessionNotFoundException>(() => _service.GetOwnerSessionIdAsync(OwnerId, ReservationId));
        _sessions.Verify(repository => repository.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Fact]
    public async Task GetOwnerSessionId_DoesNotCreateSessionWhenNoneExists()
    {
        await Assert.ThrowsAsync<GuestSessionNotFoundException>(() => _service.GetOwnerSessionIdAsync(OwnerId, ReservationId));

        AssertNoWrites();
    }

    [Fact]
    public async Task Submit_MergesSubmittedSessionsAndPreservesExistingOrderItemsInOneCommit()
    {
        SetSessions(
            Session("session-1", true, Item("dish-1", 2), Item("dish-2", 1)),
            Session("session-2", true, Item("dish-1", 1), Item("dish-2", 1)),
            Session("draft", false, Item("dish-1", 99)));
        _order.Dishes = [Item("dish-1", 4), Item("existing-dish", 1)];
        Order? savedOrder = null;
        GroupReservation? savedGroup = null;
        IReadOnlyList<OrderItem>? expectedOrderItems = null;
        IReadOnlyList<GuestSession>? submittedSessions = null;
        _submissions.Setup(repository => repository.SubmitAsync(It.IsAny<Order>(), It.IsAny<GroupReservation>(),
                OrderStatus.Created, It.IsAny<IReadOnlyList<OrderItem>>(), It.IsAny<IReadOnlyList<GuestSession>>()))
            .Callback<Order, GroupReservation, OrderStatus, IReadOnlyList<OrderItem>, IReadOnlyList<GuestSession>>(
                (order, group, _, expectedItems, submitted) =>
                {
                    savedOrder = order;
                    savedGroup = group;
                    expectedOrderItems = expectedItems;
                    submittedSessions = submitted;
                })
            .Returns(Task.CompletedTask);

        var consolidated = await _service.SubmitAsync(OwnerId, GroupId, ReservationId);

        Assert.Equal(2, consolidated.Count);
        Assert.Equal(3, consolidated.Single(item => item.DishId == "dish-1").Quantity);
        Assert.Equal(2, consolidated.Single(item => item.DishId == "dish-2").Quantity);
        Assert.NotNull(savedOrder);
        Assert.Equal(OrderStatus.SubmittedByCustomer, savedOrder.Status);
        Assert.Equal(7, savedOrder.Dishes.Single(item => item.DishId == "dish-1").Quantity);
        Assert.Equal(2, savedOrder.Dishes.Single(item => item.DishId == "dish-2").Quantity);
        Assert.Equal(1, savedOrder.Dishes.Single(item => item.DishId == "existing-dish").Quantity);
        Assert.NotNull(savedGroup);
        Assert.Equal(GroupReservationStatus.Submitted, savedGroup.Status);
        Assert.NotNull(expectedOrderItems);
        Assert.Equal(2, expectedOrderItems.Count);
        Assert.Equal(4, expectedOrderItems.Single(item => item.DishId == "dish-1").Quantity);
        Assert.Equal(1, expectedOrderItems.Single(item => item.DishId == "existing-dish").Quantity);
        Assert.NotNull(submittedSessions);
        Assert.Equal(new[] { "session-1", "session-2" }, submittedSessions.Select(session => session.Id).OrderBy(id => id));
        _submissions.Verify(repository => repository.SubmitAsync(It.IsAny<Order>(), It.IsAny<GroupReservation>(),
            OrderStatus.Created, It.IsAny<IReadOnlyList<OrderItem>>(), It.IsAny<IReadOnlyList<GuestSession>>()), Times.Once);
        _orders.Verify(repository => repository.SaveOrderAsync(It.IsAny<Order>()), Times.Never);
        _groups.Verify(repository => repository.SaveGroupReservationAsync(It.IsAny<GroupReservation>()), Times.Never);
    }

    [Fact]
    public async Task Submit_UsesCurrentSubmittedFlagInsteadOfStaleIndexFlag()
    {
        var stale = Session("session-1", false, Item("dish-1", 1));
        SetSessions(stale);
        _sessions.Setup(repository => repository.GetByGuestSessionId(stale.Id))
            .ReturnsAsync(Session(stale.Id, true, Item("dish-1", 2)));

        var consolidated = await _service.SubmitAsync(OwnerId, GroupId, ReservationId);

        Assert.Equal(2, Assert.Single(consolidated).Quantity);
    }

    [Fact]
    public async Task Submit_NoSubmittedSessionsReturnsValidationErrorWithoutWriting()
    {
        SetSessions(Session("draft", false, Item("dish-1", 2)));

        await Assert.ThrowsAsync<ValidationException>(() => _service.SubmitAsync(OwnerId, GroupId, ReservationId));

        AssertNoWrites();
    }

    [Fact]
    public async Task Submit_SubmittedSessionsWithoutDishesReturnValidationError()
    {
        SetSessions(Session("empty-submitted", true), Session("draft-with-items", false, Item("dish-1", 2)));

        await Assert.ThrowsAsync<ValidationException>(() => _service.SubmitAsync(OwnerId, GroupId, ReservationId));

        AssertNoWrites();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Submit_NonPositiveQuantityReturnsValidationError(int quantity)
    {
        SetSessions(Session("session-1", true, Item("dish-1", quantity)));

        await Assert.ThrowsAsync<ValidationException>(() => _service.SubmitAsync(OwnerId, GroupId, ReservationId));

        AssertNoWrites();
    }

    [Fact]
    public async Task Submit_MissingGroupReturnsNotFound()
    {
        _groups.Setup(repository => repository.GetGroupReservationByIdAsync(GroupId)).ReturnsAsync((GroupReservation?)null);

        await Assert.ThrowsAsync<GroupReservationNotFoundException>(() => _service.SubmitAsync(OwnerId, GroupId, ReservationId));

        AssertNoWrites();
    }

    [Fact]
    public async Task Submit_MissingReservationReturnsNotFound()
    {
        _reservations.Setup(repository => repository.GetReservationByIdAsync(ReservationId)).ReturnsAsync((Reservation?)null);

        await Assert.ThrowsAsync<ReservationNotFoundException>(() => _service.SubmitAsync(OwnerId, GroupId, ReservationId));

        AssertNoWrites();
    }

    [Fact]
    public async Task Submit_ReservationBelongingToDifferentGroupReturnsValidationError()
    {
        _group.ReservationId = "another-reservation";

        await Assert.ThrowsAsync<ValidationException>(() => _service.SubmitAsync(OwnerId, GroupId, ReservationId));

        AssertNoWrites();
    }

    [Fact]
    public async Task Submit_MissingOrderReturnsValidationError()
    {
        SetSessions(Session("session-1", true, Item("dish-1", 2)));
        _orders.Setup(repository => repository.GetOrderByReservationIdAsync(ReservationId)).ReturnsAsync((Order?)null);

        await Assert.ThrowsAsync<ValidationException>(() => _service.SubmitAsync(OwnerId, GroupId, ReservationId));

        AssertNoWrites();
    }

    [Fact]
    public async Task Submit_OrderBelongingToAnotherUserIsForbidden()
    {
        SetSessions(Session("session-1", true, Item("dish-1", 2)));
        _order.CustomerId = "another-user";

        await Assert.ThrowsAsync<ActionForbiddenException>(() => _service.SubmitAsync(OwnerId, GroupId, ReservationId));

        AssertNoWrites();
    }

    [Fact]
    public async Task Submit_OrderBelongingToAnotherReservationReturnsValidationError()
    {
        SetSessions(Session("session-1", true, Item("dish-1", 2)));
        _order.ReservationId = "another-reservation";

        await Assert.ThrowsAsync<ValidationException>(() => _service.SubmitAsync(OwnerId, GroupId, ReservationId));

        AssertNoWrites();
    }

    [Theory]
    [InlineData(GroupReservationStatus.Submitted)]
    [InlineData(GroupReservationStatus.Closed)]
    public async Task Submit_GroupThatIsNotOpenReturnsValidationError(GroupReservationStatus status)
    {
        _group.Status = status;
        SetSessions(Session("session-1", true, Item("dish-1", 2)));

        await Assert.ThrowsAsync<ValidationException>(() => _service.SubmitAsync(OwnerId, GroupId, ReservationId));

        AssertNoWrites();
    }

    [Theory]
    [InlineData(OrderStatus.Submitted)]
    [InlineData(OrderStatus.SubmittedByCustomer)]
    [InlineData(OrderStatus.Cancelled)]
    public async Task Submit_OrderThatIsNotCreatedReturnsValidationError(OrderStatus status)
    {
        _order.Status = status;
        SetSessions(Session("session-1", true, Item("dish-1", 2)));

        await Assert.ThrowsAsync<ValidationException>(() => _service.SubmitAsync(OwnerId, GroupId, ReservationId));

        AssertNoWrites();
    }

    [Fact]
    public async Task Submit_FailedAtomicCommitDoesNotMutateLoadedEntitiesOrSaveSeparately()
    {
        SetSessions(Session("session-1", true, Item("dish-1", 2)));
        var existingItem = Item("dish-1", 1);
        _order.Dishes = [existingItem];
        _submissions.Setup(repository => repository.SubmitAsync(It.IsAny<Order>(), It.IsAny<GroupReservation>(),
                OrderStatus.Created, It.IsAny<IReadOnlyList<OrderItem>>(), It.IsAny<IReadOnlyList<GuestSession>>()))
            .ThrowsAsync(new InvalidOperationException("Simulated persistence failure"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SubmitAsync(OwnerId, GroupId, ReservationId));

        Assert.Equal(OrderStatus.Created, _order.Status);
        Assert.Equal(GroupReservationStatus.Open, _group.Status);
        Assert.Equal(1, existingItem.Quantity);
        Assert.Single(_order.Dishes);
        _orders.Verify(repository => repository.SaveOrderAsync(It.IsAny<Order>()), Times.Never);
        _groups.Verify(repository => repository.SaveGroupReservationAsync(It.IsAny<GroupReservation>()), Times.Never);
    }

    [Fact]
    public async Task DeleteSession_ReturnsUpdatedSnapshotEvenWhenIndexStillContainsDeletedSession()
    {
        var removed = Session("session-1", true, Item("dish-1", 2));
        var remaining = Session("session-2", false, Item("dish-2", 1));
        SetSessions(removed, remaining);

        var snapshot = await _service.DeleteSessionAsync(OwnerId, GroupId, removed.Id);

        var participant = Assert.Single(snapshot.Participants);
        Assert.Equal(remaining.Id, participant.Id);
        Assert.Equal(220m, participant.Total);
        Assert.Equal(220m, snapshot.Total);
        _submissions.Verify(repository => repository.DeleteSessionAsync(removed, GroupId), Times.Once);
        _sessions.Verify(repository => repository.DeleteAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Fact]
    public async Task DeleteSession_LastGuestReturnsEmptySnapshot()
    {
        var removed = Session("session-1", true, Item("dish-1", 2));
        SetSessions(removed);

        var snapshot = await _service.DeleteSessionAsync(OwnerId, GroupId, removed.Id);

        Assert.Empty(snapshot.Participants);
        Assert.Equal(0m, snapshot.Total);
    }

    [Fact]
    public async Task DeleteSession_MissingSessionReturnsNotFoundWithoutDeleting()
    {
        await Assert.ThrowsAsync<GuestSessionNotFoundException>(() => _service.DeleteSessionAsync(OwnerId, GroupId, "missing-session"));

        AssertNoWrites();
    }

    [Fact]
    public async Task DeleteSession_WrongGroupReturnsValidationErrorWithoutDeleting()
    {
        var wrongGroupSession = Session("wrong-group-session", false);
        wrongGroupSession.GroupReservationId = "another-group";
        _sessions.Setup(repository => repository.GetByGuestSessionId(wrongGroupSession.Id)).ReturnsAsync(wrongGroupSession);

        await Assert.ThrowsAsync<ValidationException>(() => _service.DeleteSessionAsync(OwnerId, GroupId, wrongGroupSession.Id));

        AssertNoWrites();
    }

    [Fact]
    public async Task DeleteSession_OwnerSessionCannotBeDeleted()
    {
        var owner = Session("owner-session", false);
        owner.IsOwnerSession = true;
        owner.OwnerUserId = OwnerId;
        SetSessions(owner);

        await Assert.ThrowsAsync<ValidationException>(() => _service.DeleteSessionAsync(OwnerId, GroupId, owner.Id));

        AssertNoWrites();
    }

    [Theory]
    [InlineData(GroupReservationStatus.Submitted)]
    [InlineData(GroupReservationStatus.Closed)]
    public async Task DeleteSession_GroupThatIsNotOpenReturnsValidationError(GroupReservationStatus status)
    {
        _group.Status = status;
        var guest = Session("session-1", false);
        SetSessions(guest);

        await Assert.ThrowsAsync<ValidationException>(() => _service.DeleteSessionAsync(OwnerId, GroupId, guest.Id));

        AssertNoWrites();
    }

    [Fact]
    public async Task DeleteSession_InvalidRemainingDishDoesNotDeleteFirst()
    {
        var removed = Session("session-1", false, Item("dish-1", 1));
        var invalidRemaining = Session("session-2", false, Item("deleted-dish", 1));
        SetSessions(removed, invalidRemaining);

        await Assert.ThrowsAsync<ValidationException>(() => _service.DeleteSessionAsync(OwnerId, GroupId, removed.Id));

        _sessions.Verify(repository => repository.DeleteAsync(It.IsAny<GuestSession>()), Times.Never);
        _submissions.Verify(repository => repository.DeleteSessionAsync(It.IsAny<GuestSession>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task DeleteSession_FailedAtomicDeleteDoesNotFallBackToUnconditionalDelete()
    {
        var guest = Session("session-1", true, Item("dish-1", 1));
        SetSessions(guest);
        _submissions.Setup(repository => repository.DeleteSessionAsync(guest, GroupId))
            .ThrowsAsync(new InvalidOperationException("Simulated delete transaction failure"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.DeleteSessionAsync(OwnerId, GroupId, guest.Id));

        _sessions.Verify(repository => repository.DeleteAsync(It.IsAny<GuestSession>()), Times.Never);
        _sessions.Verify(repository => repository.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    private async Task InvokeOperation(string operation, string userId)
    {
        switch (operation)
        {
            case "snapshot": await _service.GetSessionsAsync(userId, GroupId); break;
            case "owner-session": await _service.GetOwnerSessionIdAsync(userId, ReservationId); break;
            case "submit": await _service.SubmitAsync(userId, GroupId, ReservationId); break;
            case "delete": await _service.DeleteSessionAsync(userId, GroupId, "guest-session"); break;
            default: throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    private void SetSessions(params GuestSession[] sessions)
    {
        _sessions.Setup(repository => repository.GetAllByGroupReservationIdAsync(GroupId)).ReturnsAsync(sessions.ToList());
        foreach (var session in sessions)
        {
            _sessions.Setup(repository => repository.GetByGuestSessionId(session.Id)).ReturnsAsync(session);
        }
    }

    private void SetDishes(params Dish[] dishes)
    {
        _dishes.Setup(repository => repository.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync((IEnumerable<string> ids) => dishes.Where(dish => ids.Contains(dish.Id)).ToArray());
    }

    private void AssertNoWrites()
    {
        _sessions.Verify(repository => repository.DeleteAsync(It.IsAny<GuestSession>()), Times.Never);
        _sessions.Verify(repository => repository.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
        _sessions.Verify(repository => repository.CreateOwnerSessionIfMissingAsync(It.IsAny<GuestSession>()), Times.Never);
        _orders.Verify(repository => repository.SaveOrderAsync(It.IsAny<Order>()), Times.Never);
        _groups.Verify(repository => repository.SaveGroupReservationAsync(It.IsAny<GroupReservation>()), Times.Never);
        _submissions.Verify(repository => repository.SubmitAsync(It.IsAny<Order>(), It.IsAny<GroupReservation>(),
            It.IsAny<OrderStatus>(), It.IsAny<IReadOnlyList<OrderItem>>(), It.IsAny<IReadOnlyList<GuestSession>>()), Times.Never);
        _submissions.Verify(repository => repository.DeleteSessionAsync(It.IsAny<GuestSession>(), It.IsAny<string>()), Times.Never);
    }

    private static GuestSession Session(string id, bool submitted, params OrderItem[] dishes) => new()
    {
        Id = id,
        GroupReservationId = GroupId,
        GuestName = id,
        IsSubmitted = submitted,
        Dishes = dishes.ToList()
    };

    private static OrderItem Item(string dishId, int quantity) => new()
    {
        DishId = dishId,
        Quantity = quantity,
        State = OrderItemState.Pending
    };

    private static Dish Dish(string id, decimal price) => new()
    {
        Id = id,
        Name = id,
        Description = "Test dish",
        Price = price,
        State = DishState.Available,
        DishType = DishType.MainCourse,
        LocationId = "location-1"
    };
}
