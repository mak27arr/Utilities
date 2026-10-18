using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Utilities.Infrastructure.Persistence.Migrations;

[DbContext(typeof(UtilitiesDbContext))]
[Migration("20261017140000_RefreshBuiltInServiceTypes")]
public sealed class RefreshBuiltInServiceTypes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
