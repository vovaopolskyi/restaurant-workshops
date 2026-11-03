using APIHandler.Entities;
using APIHandler.Exceptions;
using APIHandler.Repositories.GroupReservations;
using APIHandler.Repositories.GuestSessions;
using APIHandler.Repositories.Reservations;
using APIHandler.Repositories.Tables;
using APIHandler.Services.Auth;
using APIHandler.Services.GroupReservations;
using APIHandler.Services.Locations;
using Moq;

namespace APIHandler.Tests.Services;

public class GroupOwnerSessionTests
{
    private readonly Mock<IGroupReservationRepository> _groups = new();
    private readonly Mock<IGuestSessionRepository> _sessions = new();
    private readonly Mock<IReservationRepository> _reservations = new();
    private readonly GroupReservationService _service;

    public GroupOwnerSessionTests()
    {
        _service = new GroupReservationService(_groups.Object, Mock.Of<ITableRepository>(),
            _reservations.Object, _sessions.Object, Mock.Of<IUserService>(), Mock.Of<ILocationService>());
    }

    [Fact]
    public async Task CreateGroupReservation_CreatesEmptyOwnerSessionForReservationCustomer()
    {
        var reservation = Reservation("owner-1");
        _reservations.Setup(repository => repository.GetReservationByIdAsync(reservation.Id)).ReturnsAsync(reservation);
        GuestSession? savedSession = null;
        _sessions.Setup(repository => repository.CreateOwnerSessionIfMissingAsync(It.IsAny<GuestSession>()))
            .Callback<GuestSession>(session => savedSession = session)
            .Returns(Task.CompletedTask);

        var group = await _service.CreateGroupReservationAsync(reservation.Id);

        Assert.Equal(reservation.Id, group.ReservationId);
        Assert.Equal(GroupReservationStatus.Open, group.Status);
        Assert.NotNull(savedSession);
        Assert.Equal("owner-" + group.GroupReservationId, savedSession.Id);
        Assert.Equal(group.GroupReservationId, savedSession.GroupReservationId);
        Assert.True(savedSession.IsOwnerSession);
        Assert.Equal(reservation.CustomerId, savedSession.OwnerUserId);
        Assert.Equal("Owner", savedSession.GuestName);
        Assert.False(savedSession.IsSubmitted);
        Assert.Empty(savedSession.Dishes);
        _groups.Verify(repository => repository.SaveGroupReservationAsync(group), Times.Once);
        _sessions.Verify(repository => repository.CreateOwnerSessionIfMissingAsync(It.IsAny<GuestSession>()), Times.Once);
        _sessions.Verify(repository => repository.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task CreateGroupReservation_WithoutCustomerDoesNotCreateOwnerSession(string? customerId)
    {
        var reservation = Reservation(customerId);
        _reservations.Setup(repository => repository.GetReservationByIdAsync(reservation.Id)).ReturnsAsync(reservation);

        var group = await _service.CreateGroupReservationAsync(reservation.Id);

        _groups.Verify(repository => repository.SaveGroupReservationAsync(group), Times.Once);
        _sessions.Verify(repository => repository.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
        _sessions.Verify(repository => repository.CreateOwnerSessionIfMissingAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Fact]
    public async Task CreateGroupReservation_MissingReservationDoesNotPersistOrphanGroupOrSession()
    {
        _reservations.Setup(repository => repository.GetReservationByIdAsync("missing-reservation"))
            .ReturnsAsync((Reservation?)null);

        await Assert.ThrowsAsync<ReservationNotFoundException>(() => _service.CreateGroupReservationAsync("missing-reservation"));

        _groups.Verify(repository => repository.SaveGroupReservationAsync(It.IsAny<GroupReservation>()), Times.Never);
        _sessions.Verify(repository => repository.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
        _sessions.Verify(repository => repository.CreateOwnerSessionIfMissingAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Fact]
    public async Task CreateGroupReservationIfMissing_PreservesExistingOwnerCart()
    {
        var reservation = Reservation("owner-1");
        var group = new GroupReservation
        {
            GroupReservationId = "existing-group",
            ReservationId = reservation.Id,
            ReservationToken = "existing-token",
            Status = GroupReservationStatus.Open
        };
        var existingSession = new GuestSession
        {
            Id = "owner-existing-group",
            GroupReservationId = group.GroupReservationId,
            GuestName = "Owner",
            IsOwnerSession = true,
            OwnerUserId = reservation.CustomerId,
            IsSubmitted = true,
            Dishes = [new OrderItem { DishId = "dish-1", Quantity = 3 }]
        };
        _groups.Setup(repository => repository.GetByReservationIdAsync(reservation.Id)).ReturnsAsync(group);
        _reservations.Setup(repository => repository.GetReservationByIdAsync(reservation.Id)).ReturnsAsync(reservation);
        _sessions.Setup(repository => repository.GetByGuestSessionId(existingSession.Id)).ReturnsAsync(existingSession);

        await _service.CreateGroupReservationIfDoesNotExistAsync(reservation.CustomerId!, reservation.Id);

        Assert.True(existingSession.IsSubmitted);
        Assert.Equal(3, Assert.Single(existingSession.Dishes).Quantity);
        _groups.Verify(repository => repository.SaveGroupReservationAsync(It.IsAny<GroupReservation>()), Times.Never);
        _sessions.Verify(repository => repository.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
        _sessions.Verify(repository => repository.CreateOwnerSessionIfMissingAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Fact]
    public async Task CreateGroupReservationIfMissing_RepairsMissingOwnerUsingConditionalCreation()
    {
        var reservation = Reservation("owner-1");
        var group = new GroupReservation
        {
            GroupReservationId = "existing-group",
            ReservationId = reservation.Id,
            ReservationToken = "existing-token",
            Status = GroupReservationStatus.Open
        };
        _groups.Setup(repository => repository.GetByReservationIdAsync(reservation.Id)).ReturnsAsync(group);
        _reservations.Setup(repository => repository.GetReservationByIdAsync(reservation.Id)).ReturnsAsync(reservation);

        await _service.CreateGroupReservationIfDoesNotExistAsync(reservation.CustomerId!, reservation.Id);

        _sessions.Verify(repository => repository.CreateOwnerSessionIfMissingAsync(It.Is<GuestSession>(session =>
            session.Id == "owner-existing-group" && session.GroupReservationId == group.GroupReservationId &&
            session.OwnerUserId == reservation.CustomerId && session.IsOwnerSession && session.Dishes.Count == 0)), Times.Once);
        _sessions.Verify(repository => repository.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
        _groups.Verify(repository => repository.SaveGroupReservationAsync(It.IsAny<GroupReservation>()), Times.Never);
    }

    private static Reservation Reservation(string? customerId) => new()
    {
        Id = "reservation-1",
        TableId = "table-1",
        WaiterId = "waiter-1",
        CustomerId = customerId,
        GuestCount = 3
    };
}
