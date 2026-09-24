using Microsoft.EntityFrameworkCore.Migrations;

namespace Sonda.Infrastructure.Persistence.Migrations;

public partial class DurablePersistence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        using var stream = typeof(DurablePersistence).Assembly.GetManifestResourceStream("Sonda.Infrastructure.Persistence.Migrations.001_initial.sql")!;
        using var reader = new StreamReader(stream);
        migrationBuilder.Sql(reader.ReadToEnd());
    }
    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Restore a backup; destructive downgrade is not supported.");
}
