using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Utilities.Infrastructure.Persistence.Migrations;

[DbContext(typeof(UtilitiesDbContext))]
[Migration("20261017150000_AddServiceTypeIconKey")]
public sealed class AddServiceTypeIconKey : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "IconKey",
            table: "ServiceTypes",
            type: "TEXT",
            maxLength: 64,
            nullable: false,
            defaultValue: "drop");

        migrationBuilder.Sql("UPDATE ServiceTypes SET IconKey = CASE Name " +
            "WHEN 'Cold water' THEN 'service-cold-water' " +
            "WHEN 'Hot water' THEN 'service-hot-water' " +
            "WHEN 'Water' THEN 'service-water' " +
            "WHEN 'Gas' THEN 'service-gas' " +
            "WHEN 'Electricity' THEN 'service-electricity' " +
            "WHEN 'Heating' THEN 'service-heating' " +
            "WHEN 'Housing and utilities' THEN 'service-housing' " +
            "WHEN 'Internet' THEN 'service-internet' " +
            "WHEN 'Waste collection' THEN 'service-waste' " +
            "ELSE CASE WHEN Category = 1 THEN 'building' ELSE 'drop' END END");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "IconKey", table: "ServiceTypes");
}
