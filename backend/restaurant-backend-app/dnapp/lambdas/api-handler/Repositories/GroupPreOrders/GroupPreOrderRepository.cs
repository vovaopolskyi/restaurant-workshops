using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.Model;
using APIHandler.Constants;
using APIHandler.Entities;
using APIHandler.Exceptions;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace APIHandler.Repositories.GroupPreOrders;

public class GroupPreOrderRepository(IAmazonDynamoDB client, IDynamoDBContext context) : IGroupPreOrderRepository
{
    public async Task SubmitAsync(Order order, GroupReservation group, OrderStatus expectedOrderStatus,
        IReadOnlyList<OrderItem> expectedOrderDishes, IReadOnlyList<GuestSession> submittedSessions)
    {
        if (submittedSessions.Count > 98)
            throw new ValidationException("A group submission supports at most 98 submitted sessions.");
        var originalOrder = new Order
        {
            OrderId = order.OrderId, CustomerId = order.CustomerId, ReservationId = order.ReservationId,
            ReservationAddress = order.ReservationAddress, ReservationTime = order.ReservationTime,
            Dishes = expectedOrderDishes.ToList(), Status = expectedOrderStatus
        };
        var originalAttributes = context.ToDocument(originalOrder).ToAttributeMap();
        var orderAttributes = context.ToDocument(order).ToAttributeMap();
        var groupAttributes = context.ToDocument(group).ToAttributeMap();
        var request = new TransactWriteItemsRequest
        {
            TransactItems =
            [
                new TransactWriteItem
                {
                    Update = new Update
                    {
                        TableName = ResourceNames.OrdersTableName,
                        Key = new Dictionary<string, AttributeValue>
                        {
                            ["orderId"] = new() { S = order.OrderId },
                            ["customerId"] = new() { S = order.CustomerId }
                        },
                        UpdateExpression = "SET #dishes = :dishes, #status = :submitted",
                        ConditionExpression = "#status = :expected AND reservationId = :reservationId AND " +
                            (expectedOrderDishes.Count == 0
                                ? "(attribute_not_exists(#dishes) OR #dishes = :previousDishes)"
                                : "#dishes = :previousDishes"),
                        ExpressionAttributeNames = new() { ["#dishes"] = "dishes", ["#status"] = "status" },
                        ExpressionAttributeValues = new()
                        {
                            [":dishes"] = orderAttributes["dishes"],
                            [":previousDishes"] = originalAttributes["dishes"],
                            [":submitted"] = orderAttributes["status"],
                            [":expected"] = new() { N = ((int)expectedOrderStatus).ToString(CultureInfo.InvariantCulture) },
                            [":reservationId"] = new() { S = group.ReservationId }
                        }
                    }
                },
                new TransactWriteItem
                {
                    Update = new Update
                    {
                        TableName = ResourceNames.GroupReservationsTableName,
                        Key = new() { ["groupReservationId"] = new() { S = group.GroupReservationId } },
                        UpdateExpression = "SET #status = :submitted",
                        ConditionExpression = "#status = :open AND reservationId = :reservationId",
                        ExpressionAttributeNames = new() { ["#status"] = "status" },
                        ExpressionAttributeValues = new()
                        {
                            [":submitted"] = groupAttributes["status"],
                            [":open"] = new() { N = ((int)GroupReservationStatus.Open).ToString(CultureInfo.InvariantCulture) },
                            [":reservationId"] = new() { S = group.ReservationId }
                        }
                    }
                }
            ]
        };

        foreach (var session in submittedSessions)
        {
            var attributes = context.ToDocument(session).ToAttributeMap();
            request.TransactItems.Add(new TransactWriteItem
            {
                ConditionCheck = new ConditionCheck
                {
                    TableName = ResourceNames.GuestSessionTableName,
                    Key = new() { ["guestSessionId"] = new() { S = session.Id } },
                    ConditionExpression = "groupReservationId = :group AND isSubmitted = :submitted AND #dishes = :dishes",
                    ExpressionAttributeNames = new() { ["#dishes"] = "dishes" },
                    ExpressionAttributeValues = new()
                    {
                        [":group"] = new() { S = group.GroupReservationId },
                        [":submitted"] = attributes["isSubmitted"],
                        [":dishes"] = attributes["dishes"]
                    }
                }
            });
        }
        await CommitAsync(request);
    }

    public async Task DeleteSessionAsync(GuestSession session, string groupReservationId)
    {
        var request = new TransactWriteItemsRequest
        {
            TransactItems =
            [
                new TransactWriteItem
                {
                    ConditionCheck = new ConditionCheck
                    {
                        TableName = ResourceNames.GroupReservationsTableName,
                        Key = new() { ["groupReservationId"] = new() { S = groupReservationId } },
                        ConditionExpression = "#status = :open",
                        ExpressionAttributeNames = new() { ["#status"] = "status" },
                        ExpressionAttributeValues = new()
                        {
                            [":open"] = new() { N = ((int)GroupReservationStatus.Open).ToString(CultureInfo.InvariantCulture) }
                        }
                    }
                },
                new TransactWriteItem
                {
                    Delete = new Delete
                    {
                        TableName = ResourceNames.GuestSessionTableName,
                        Key = new() { ["guestSessionId"] = new() { S = session.Id } },
                        ConditionExpression = "groupReservationId = :group",
                        ExpressionAttributeValues = new() { [":group"] = new() { S = groupReservationId } }
                    }
                }
            ]
        };
        await CommitAsync(request);
    }

    private async Task CommitAsync(TransactWriteItemsRequest request)
    {
        try
        {
            await client.TransactWriteItemsAsync(request);
        }
        catch (TransactionCanceledException exception) when (
            exception.CancellationReasons?.Any(reason => reason.Code == "ConditionalCheckFailed") == true)
        {
            throw new ValidationException("The order, group or session changed. Reload the group before retrying.");
        }
    }
}
