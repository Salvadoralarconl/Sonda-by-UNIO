using Sonda.Api.Contracts;

namespace Sonda.Server.Queries;

public static class HomeBadgeProjection
{
    // Availability qualifies business health; it never changes a run result or daily metric.
    public static CurrentSystemBadgeDto System(IReadOnlyList<ApplicationStateDto> monitored)
    {
        if (monitored.Count == 0) return new(null, true, "No monitored applications.", 0, []);
        var business = monitored.Any(x => x.BusinessHealth == "Error") ? "Error" :
            monitored.Any(x => x.BusinessHealth == "Warning") ? "Warning" :
            monitored.All(x => x.BusinessHealth == "Stable") ? "Stable" : null;
        var issues = monitored.Where(x => x.Availability != "Fresh").ToArray();
        return new(business, issues.Length > 0 || business is null,
            issues.Length > 0 ? "Monitoring availability is not confirmed for every application." :
            business is null ? "Business health is not yet known." : "", monitored.Count, issues);
    }
}
