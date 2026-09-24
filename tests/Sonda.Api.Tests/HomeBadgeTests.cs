using Sonda.Api.Contracts;
using Sonda.Server.Queries;
using Xunit;

namespace Sonda.Api.Tests;

public sealed class HomeBadgeTests
{
    private static ApplicationStateDto App(string health, string availability = "Fresh") =>
        new(Guid.NewGuid().ToString(), health, availability, health == "Stable" && availability == "Fresh", null, null, "test");

    [Theory]
    [InlineData("Stable", "Stable", "Stable")]
    [InlineData("Stable", "Warning", "Warning")]
    [InlineData("Warning", "Error", "Error")]
    public void Aggregate_is_current_business_state(string a, string b, string expected)
    {
        var result = HomeBadgeProjection.System([App(a), App(b)]);
        Assert.Equal(expected, result.BusinessHealth); Assert.False(result.IsQualified);
    }

    [Theory]
    [InlineData("Stale")]
    [InlineData("Disconnected")]
    [InlineData("Unknown")]
    [InlineData("NotObserved")]
    public void Unconfirmed_availability_prevents_unqualified_stable(string availability)
    {
        var result = HomeBadgeProjection.System([App("Stable"), App("Stable", availability)]);
        Assert.Equal("Stable", result.BusinessHealth); Assert.True(result.IsQualified);
        Assert.Equal(availability, Assert.Single(result.AvailabilityIssues).Availability);
    }

    [Fact]
    public void Empty_monitored_set_does_not_claim_stable()
    {
        var result = HomeBadgeProjection.System([]);
        Assert.Null(result.BusinessHealth); Assert.True(result.IsQualified);
    }
}
