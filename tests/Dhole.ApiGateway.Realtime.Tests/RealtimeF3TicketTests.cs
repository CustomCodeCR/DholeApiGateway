using Dhole.ApiGateway.Realtime;
using StackExchange.Redis;
using Xunit;

namespace Dhole.ApiGateway.Realtime.Tests;

public sealed class RealtimeF3TicketTests
{
    private static string Redis => Environment.GetEnvironmentVariable("F3_REDIS_TEST")
        ?? throw new InvalidOperationException("F3_REDIS_TEST required for real Redis tests.");

    [Fact]
    public async Task Valid_ticket_is_one_use_across_two_store_instances()
    {
        using var connection = await ConnectionMultiplexer.ConnectAsync(Redis);
        var serviceA = new RealtimeAccessTicketStore(connection);
        var serviceB = new RealtimeAccessTicketStore(connection);
        var token = "test-jwt-from-validated-post-do-not-use-in-production";
        var ticket = await serviceA.IssueAsync(token);
        Assert.Equal(43, ticket.Length);
        Assert.DoesNotContain(token, ticket, StringComparison.Ordinal);
        Assert.Equal(token, await serviceB.RedeemAsync(ticket));
        Assert.Null(await serviceA.RedeemAsync(ticket));
    }

    [Fact]
    public async Task Ticket_does_not_accept_old_access_token_or_arbitrary_strings()
    {
        using var connection = await ConnectionMultiplexer.ConnectAsync(Redis);
        var tickets = new RealtimeAccessTicketStore(connection);
        Assert.Null(await tickets.RedeemAsync("access_token=bad"));
        Assert.Null(await tickets.RedeemAsync("x"));
        Assert.Null(await tickets.RedeemAsync(new string('a', 43) + ".."));
        Assert.Null(await tickets.RedeemAsync(null));
    }

    [Fact]
    public async Task Ticket_has_bounded_60_second_redis_ttl()
    {
        using var connection = await ConnectionMultiplexer.ConnectAsync(Redis);
        var tickets = new RealtimeAccessTicketStore(connection);
        var ticket = await tickets.IssueAsync("ci-only-token");
        var key = "dhole:rt:v1:ticket:" + ticket;
        var ttl = await connection.GetDatabase().KeyTimeToLiveAsync(key);
        Assert.NotNull(ttl);
        Assert.True(ttl.Value.TotalSeconds > 0 && ttl.Value.TotalSeconds <= 60);
        Assert.Equal("ci-only-token", await tickets.RedeemAsync(ticket));
        Assert.False(await connection.GetDatabase().KeyExistsAsync(key));
    }

    [Fact]
    public async Task Null_or_oversized_token_cannot_be_issued()
    {
        using var connection = await ConnectionMultiplexer.ConnectAsync(Redis);
        var tickets = new RealtimeAccessTicketStore(connection);
        await Assert.ThrowsAsync<ArgumentException>(() => tickets.IssueAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(
            () => tickets.IssueAsync(new string('x', 8193)));
    }
}
