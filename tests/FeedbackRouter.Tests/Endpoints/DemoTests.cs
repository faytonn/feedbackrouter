using System.Net.Http.Json;
using FeedbackRouter.Api;
using FeedbackRouter.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FeedbackRouter.Tests.Endpoints;

public sealed class DemoTests : IAsyncLifetime
{
    private readonly ApiFactory _factory = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _factory.InitializeDatabaseAsync();

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Reset_replaces_everything_with_the_seed()
    {
        var id = await _factory.AddProposedItemAsync();
        var client = _factory.CreateClient();
        await client.PostAsJsonAsync($"/api/feedback/{id}/decision", new { action = "Dismiss" }, Ct);

        await using var scope = _factory.Services.CreateAsyncScope();
        var imported = await scope.ServiceProvider.GetRequiredService<SeedImporter>().ResetAsync(Ct);

        var (items, decisions, imports) = await _factory.WithDbAsync(async db => (
            await db.FeedbackItems.CountAsync(Ct),
            await db.TriageDecisions.CountAsync(Ct),
            await db.FeedbackItems.CountAsync(i => i.Source == FeedbackSource.Import, Ct)));
        Assert.Equal(40, imported);
        Assert.Equal(40, items);
        Assert.Equal(40, imports);
        Assert.Equal(0, decisions);
    }

    [Theory]
    [InlineData("2026-11-05T02:00:00", "01:00:00")]
    [InlineData("2026-11-05T03:00:00", "1.00:00:00")]
    [InlineData("2026-11-05T21:30:00", "05:30:00")]
    public void Next_reset_is_the_next_three_am_utc(string now, string expected)
    {
        var wait = DemoResetService.UntilNextReset(DateTime.Parse(now, null, System.Globalization.DateTimeStyles.AdjustToUniversal), new TimeOnly(3, 0));

        Assert.Equal(TimeSpan.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), wait);
    }

    [Fact]
    public async Task Me_reports_demo_mode_off_by_default()
    {
        var me = await _factory.CreateClient().GetFromJsonAsync<MeDto>("/api/me", Ct);

        Assert.False(me!.Demo);
    }
}
