using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.Model;
using APIHandler.Constants;
using APIHandler.Entities;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace APIHandler.Repositories.GuestSessions;

public class GuestSessionRepository(IDynamoDBContext context, IAmazonDynamoDB client) : IGuestSessionRepository
{
    public async Task<GuestSession?> GetByGuestSessionId(string id) =>
        await context.LoadAsync<GuestSession?>(id, DynamoConfig.LoadConfig(ResourceNames.GuestSessionTableName));

    public async Task<GuestSession?> GetByGroupReservationId(string groupReservationId)
    {
        List<GuestSession> result = await context.QueryAsync<GuestSession>(groupReservationId, new QueryConfig
        {
            OverrideTableName = ResourceNames.GuestSessionTableName,
            IndexName = GlobalSecondaryIndexes.GuestSession.GroupReservationId
        }).GetRemainingAsync();

        return result.FirstOrDefault();
    }

    public async Task<GuestSession?> GetByTokenHash(string tokenHash)
    {
        List<GuestSession> result = await context.QueryAsync<GuestSession>(tokenHash, new QueryConfig
        {
            OverrideTableName = ResourceNames.GuestSessionTableName,
            IndexName = GlobalSecondaryIndexes.GuestSession.SessionTokenHash
        }).GetRemainingAsync();

        return result.FirstOrDefault();
    }

    public async Task<List<GuestSession>> GetAllByGroupReservationIdAsync(string groupReservationId)
    {
        return await context.QueryAsync<GuestSession>(groupReservationId, new QueryConfig
        {
            OverrideTableName = ResourceNames.GuestSessionTableName,
            IndexName = GlobalSecondaryIndexes.GuestSession.GroupReservationId
        }).GetRemainingAsync();
    }

    public async Task SaveAsync(GuestSession guestSession) => await context.SaveAsync(guestSession, new SaveConfig
    {
        OverrideTableName = ResourceNames.GuestSessionTableName
    });

    public async Task CreateOwnerSessionIfMissingAsync(GuestSession guestSession)
    {
        try
        {
            await client.PutItemAsync(new PutItemRequest
            {
                TableName = ResourceNames.GuestSessionTableName,
                Item = context.ToDocument(guestSession).ToAttributeMap(),
                ConditionExpression = "attribute_not_exists(guestSessionId)"
            });
        }
        catch (ConditionalCheckFailedException)
        {
            // Another request created the same deterministic owner identity; preserve its cart.
        }
    }

    public async Task<int> CountByGroupReservationId(string groupReservationId)
    {
        List<GuestSession> result = await context.QueryAsync<GuestSession>(groupReservationId, new QueryConfig
        {
            OverrideTableName = ResourceNames.GuestSessionTableName,
            IndexName = GlobalSecondaryIndexes.GuestSession.GroupReservationId
        }).GetRemainingAsync();

        return result.Count(session => !session.IsOwnerSession);
    }

    public async Task DeleteAsync(GuestSession session)
    {
        await context.DeleteAsync(session, DynamoConfig.DeleteConfig(ResourceNames.GuestSessionTableName));
    }
}
