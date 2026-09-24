using Sonda.Access;
using Sonda.Application.Acquisition;
using Xunit;

namespace Sonda.Access.Tests;

public sealed class MonitoringPathTests
{
    private readonly MonitoringPathPolicy policy = new([@"C:\synthetic-logs", @"\\fixture-server\logs\approved"]);

    [Theory]
    [InlineData(@"C:\synthetic-logs")]
    [InlineData(@"c:\SYNTHETIC-LOGS\CAM")]
    [InlineData(@"\\fixture-server\logs\approved\CAM")]
    public void Allowed_configuration_does_not_require_filesystem_access(string path)
    {
        var source = policy.Validate(new SourceConfiguration { ProfileId = "p", SourceKey = "s", Root = path });
        Assert.Equal(path, source.Root);
    }

    [Theory]
    [InlineData(@"C:\synthetic-logs-other")]
    [InlineData(@"C:\synthetic-logs\..\private")]
    [InlineData(@"C:\synthetic-logs\test.log:secret")]
    [InlineData(@"C:\synthetic-logs\folder.")]
    [InlineData(@"C:\synthetic-logs\folder ")]
    [InlineData(@"C:\synthetic-logs\NUL.log")]
    [InlineData(@"C:synthetic-logs")]
    [InlineData(@"\\?\C:\synthetic-logs")]
    [InlineData(@"\\.\C:\synthetic-logs")]
    [InlineData(@"\\fixture-server\other\approved")]
    [InlineData(@"\\other-server\logs\approved")]
    [InlineData(@"\\fixture-server\logs\approved-extra")]
    [InlineData(@"C:/synthetic-logs")]
    public void Rejects_ambiguous_or_outside_paths(string path) => Assert.Throws<AccessFault>(() => policy.ValidatePath(path));

    [Fact]
    public void Auxiliary_manifest_cannot_escape_allowlist() => Assert.Throws<AccessFault>(() => policy.Validate(
        new SourceConfiguration { ProfileId = "p", SourceKey = "s", Root = @"C:\synthetic-logs", RotationManifestPath = @"C:\private\manifest.json" }));

    [Fact]
    public void Empty_allowlist_fails_closed() => Assert.Throws<AccessFault>(() => new MonitoringPathPolicy([]).ValidatePath(@"C:\synthetic-logs"));
}
