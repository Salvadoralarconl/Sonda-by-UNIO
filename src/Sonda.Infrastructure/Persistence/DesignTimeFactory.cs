using Microsoft.EntityFrameworkCore.Design;

namespace Sonda.Infrastructure.Persistence;

public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<SondaDbContext>
{
    public SondaDbContext CreateDbContext(string[] args) => SondaDbContext.Open(
        Environment.GetEnvironmentVariable("SONDA_DATABASE") ?? "Host=127.0.0.1;Database=sonda_design_only;Username=design_only");
}
