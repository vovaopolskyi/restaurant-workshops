using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Exceptions;
using APIHandler.Handlers;
using APIHandler.Handlers.GroupReservations;
using APIHandler.Handlers.GuestSessions;
using APIHandler.Models.Dishes;
using APIHandler.Models.GroupReservation;
using APIHandler.Models.GuestSessions;
using APIHandler.Models.Orders;
using APIHandler.Services.OwnerPreOrders;
using Moq;
using System.Text.Json;

namespace APIHandler.Tests.Handlers;

public class OwnerPreOrderHandlersTests
{
    private const string OwnerId = "owner-1";
    private const string GroupId = "group-1";
    private const string ReservationId = "reservation-1";
    private const string SessionId = "guest-session-1";
    private readonly Mock<IOwnerPreOrderService> _service = new(MockBehavior.Strict);
    private readonly Mock<ILambdaContext> _context = new();

    public OwnerPreOrderHandlersTests()
    {
        _context.SetupGet(context => context.Logger).Returns(Mock.Of<ILambdaLogger>());
    }

    public enum Operation { Snapshot, OwnerSession, Submit, Delete }

    public static IEnumerable<object[]> UnauthorizedRequests =>
        from operation in Enum.GetValues<Operation>()
        from authentication in Enumerable.Range(0, 6)
        select new object[] { operation, authentication };

    public static IEnumerable<object[]> InvalidPaths
    {
        get
        {
            var paths = new (Operation Operation, string Name)[]
            {
                (Operation.Snapshot, "groupReservationId"),
                (Operation.OwnerSession, "reservationId"),
                (Operation.Submit, "groupReservationId"),
                (Operation.Delete, "groupReservationId"),
                (Operation.Delete, "guestSessionId")
            };
            return from path in paths
                   from invalidValue in Enumerable.Range(0, 4)
                   select new object[] { path.Operation, path.Name, invalidValue };
        }
    }

    public static IEnumerable<object[]> DomainErrors =>
        from operation in Enum.GetValues<Operation>()
        from failure in new[] { "group", "reservation", "session", "forbidden", "validation" }
        select new object[] { operation, failure };

    [Theory]
    [MemberData(nameof(UnauthorizedRequests))]
    public async Task MissingOrBlankSubject_Returns401WithoutCallingService(Operation operation, int authentication)
    {
        var request = CreateRequest();
        switch (authentication)
        {
            case 0: request.RequestContext = null!; break;
            case 1: request.RequestContext.Authorizer = null; break;
            case 2: request.RequestContext.Authorizer.Claims = null; break;
            case 3: request.RequestContext.Authorizer.Claims.Clear(); break;
            case 4: request.RequestContext.Authorizer.Claims["sub"] = string.Empty; break;
            case 5: request.RequestContext.Authorizer.Claims["sub"] = "   "; break;
        }

        var response = await CreateHandler(operation).HandleRequest(request, _context.Object);

        Assert.Equal(401, response.StatusCode);
        Assert.Empty(_service.Invocations);
    }

    [Theory]
    [MemberData(nameof(InvalidPaths))]
    public async Task MissingOrBlankPath_Returns400WithoutCallingService(Operation operation, string name, int invalidValue)
    {
        var request = CreateRequest();
        switch (invalidValue)
        {
            case 0: request.PathParameters = null; break;
            case 1: request.PathParameters.Remove(name); break;
            case 2: request.PathParameters[name] = string.Empty; break;
            case 3: request.PathParameters[name] = "   "; break;
        }

        var response = await CreateHandler(operation).HandleRequest(request, _context.Object);

        Assert.Equal(400, response.StatusCode);
        Assert.Contains(invalidValue == 0 && operation == Operation.Delete ? "groupReservationId" : name, response.Body);
        Assert.Empty(_service.Invocations);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"reservationId\":null}")]
    [InlineData("{\"reservationId\":\"\"}")]
    [InlineData("{\"reservationId\":\"   \"}")]
    [InlineData("{\"reservationId\":123}")]
    public async Task Submit_InvalidBody_Returns400WithoutCallingService(string? body)
    {
        var request = CreateRequest();
        request.Body = body;

        var response = await CreateHandler(Operation.Submit).HandleRequest(request, _context.Object);

        Assert.Equal(400, response.StatusCode);
        Assert.Empty(_service.Invocations);
    }

    [Theory]
    [MemberData(nameof(DomainErrors))]
    public async Task DomainFailure_MapsToRequiredHttpStatus(Operation operation, string failure)
    {
        var (exception, expectedStatus) = failure switch
        {
            "group" => ((Exception)new GroupReservationNotFoundException(), 404),
            "reservation" => (new ReservationNotFoundException("Missing reservation."), 404),
            "session" => (new GuestSessionNotFoundException("Missing session."), 404),
            "forbidden" => (new ActionForbiddenException("Only the owner may do this."), 403),
            _ => (new ValidationException("Invalid group reservation state."), 400)
        };
        ConfigureFailure(operation, exception);

        var response = await CreateHandler(operation).HandleRequest(CreateRequest(), _context.Object);

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Contains(exception.Message, response.Body);
        Assert.Equal("application/json", response.Headers["Content-Type"]);
        Assert.Single(_service.Invocations);
    }

    [Theory]
    [InlineData(Operation.Snapshot)]
    [InlineData(Operation.Delete)]
    public async Task SnapshotSuccess_SerializesCamelCaseAndStringParticipantTypes(Operation operation)
    {
        var snapshot = new GetAllSessionsResponse(
        [
            new Participant("owner-session", ParticipantType.Owner, "Owner",
                [new OrderItemResponse { Dish = CreateDish("dish1", "Caesar Salad", 180m), Quantity = 2 }],
                360m, true),
            new Participant("session2", ParticipantType.Guest, "Olena",
                [new OrderItemResponse { Dish = CreateDish("dish2", "Tom Yum", 220m), Quantity = 1 }],
                220m, false)
        ], 580m);
        if (operation == Operation.Snapshot)
            _service.Setup(service => service.GetSessionsAsync(OwnerId, GroupId)).ReturnsAsync(snapshot);
        else
            _service.Setup(service => service.DeleteSessionAsync(OwnerId, GroupId, SessionId)).ReturnsAsync(snapshot);

        var response = await CreateHandler(operation).HandleRequest(CreateRequest(), _context.Object);

        Assert.Equal(200, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal(580m, json.RootElement.GetProperty("total").GetDecimal());
        Assert.False(json.RootElement.TryGetProperty("Total", out _));
        Assert.False(json.RootElement.TryGetProperty("Participants", out _));
        var participants = json.RootElement.GetProperty("participants");
        Assert.Equal(2, participants.GetArrayLength());
        Assert.Equal("Owner", participants[0].GetProperty("type").GetString());
        Assert.Equal("Guest", participants[1].GetProperty("type").GetString());
        Assert.True(participants[0].GetProperty("isSubmitted").GetBoolean());
        Assert.False(participants[1].GetProperty("isSubmitted").GetBoolean());
        Assert.Equal("Olena", participants[1].GetProperty("name").GetString());
        var item = participants[0].GetProperty("items")[0];
        Assert.Equal(2, item.GetProperty("quantity").GetInt32());
        Assert.Equal("dish1", item.GetProperty("dish").GetProperty("id").GetString());
        Assert.Equal(180m, item.GetProperty("dish").GetProperty("price").GetDecimal());
        Assert.Single(_service.Invocations);
    }

    [Fact]
    public async Task OwnerSessionSuccess_ReturnsGuestSessionIdInCamelCase()
    {
        _service.Setup(service => service.GetOwnerSessionIdAsync(OwnerId, ReservationId))
            .ReturnsAsync(new GetOwnerSessionIdResponse("owner-session-123"));

        var response = await CreateHandler(Operation.OwnerSession).HandleRequest(CreateRequest(), _context.Object);

        Assert.Equal(200, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal("owner-session-123", json.RootElement.GetProperty("guestSessionId").GetString());
        Assert.Single(json.RootElement.EnumerateObject());
        _service.Verify(service => service.GetOwnerSessionIdAsync(OwnerId, ReservationId), Times.Once);
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SubmitSuccess_ReturnsConsolidatedArrayAndPassesReservationIdFromBody()
    {
        IReadOnlyList<ConsolidatedOrderItemResponse> items =
        [new("dish1", 3), new("dish2", 2)];
        _service.Setup(service => service.SubmitAsync(OwnerId, GroupId, ReservationId)).ReturnsAsync(items);

        var response = await CreateHandler(Operation.Submit).HandleRequest(CreateRequest(), _context.Object);

        Assert.Equal(200, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
        Assert.Equal(2, json.RootElement.GetArrayLength());
        Assert.Equal("dish1", json.RootElement[0].GetProperty("dishId").GetString());
        Assert.Equal(3, json.RootElement[0].GetProperty("quantity").GetInt32());
        Assert.Equal("dish2", json.RootElement[1].GetProperty("dishId").GetString());
        Assert.Equal(2, json.RootElement[1].GetProperty("quantity").GetInt32());
        Assert.Equal(new[] { "dishId", "quantity" }, json.RootElement[0].EnumerateObject().Select(property => property.Name));
        _service.Verify(service => service.SubmitAsync(OwnerId, GroupId, ReservationId), Times.Once);
        _service.VerifyNoOtherCalls();
    }

    private IApiRequestHandler CreateHandler(Operation operation) => operation switch
    {
        Operation.Snapshot => new GetGroupSessionsHandler(_service.Object),
        Operation.OwnerSession => new GetOwnerSessionIdHandler(_service.Object),
        Operation.Submit => new SubmitGroupPreOrderHandler(_service.Object),
        Operation.Delete => new DeleteGuestSessionHandler(_service.Object),
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    private static APIGatewayProxyRequest CreateRequest() => new()
    {
        Body = "{\"reservationId\":\"reservation-1\"}",
        PathParameters = new Dictionary<string, string>
        {
            ["groupReservationId"] = GroupId,
            ["reservationId"] = ReservationId,
            ["guestSessionId"] = SessionId
        },
        RequestContext = new APIGatewayProxyRequest.ProxyRequestContext
        {
            Authorizer = new APIGatewayCustomAuthorizerContext
            {
                Claims = new Dictionary<string, string> { ["sub"] = OwnerId }
            }
        }
    };

    private void ConfigureFailure(Operation operation, Exception exception)
    {
        switch (operation)
        {
            case Operation.Snapshot:
                _service.Setup(service => service.GetSessionsAsync(OwnerId, GroupId)).ThrowsAsync(exception);
                break;
            case Operation.OwnerSession:
                _service.Setup(service => service.GetOwnerSessionIdAsync(OwnerId, ReservationId)).ThrowsAsync(exception);
                break;
            case Operation.Submit:
                _service.Setup(service => service.SubmitAsync(OwnerId, GroupId, ReservationId)).ThrowsAsync(exception);
                break;
            case Operation.Delete:
                _service.Setup(service => service.DeleteSessionAsync(OwnerId, GroupId, SessionId)).ThrowsAsync(exception);
                break;
        }
    }

    private static DishResponse CreateDish(string id, string name, decimal price) => new()
    {
        Id = id,
        Name = name,
        Description = "Test dish",
        DishType = "Main Course",
        Price = price,
        Weight = "250 g",
        Calories = "200",
        Proteins = "10",
        Fats = "5",
        Carbohydrates = "25",
        Vitamins = string.Empty,
        ImageUrl = null,
        State = "Available"
    };
}
