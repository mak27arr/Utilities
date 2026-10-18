using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Utilities.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMeterIdentifiersAndReplacement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccountNumber",
                table: "Meters",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MeterNumber",
                table: "Meters",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ReplacedAt",
                table: "Meters",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReplacedByMeterId",
                table: "Meters",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReplacesMeterId",
                table: "Meters",
                type: "TEXT",
                nullable: true);

            migrationBuilder.Sql("UPDATE Meters SET MeterNumber = Name WHERE MeterNumber IS NULL AND NOT EXISTS (SELECT 1 FROM ServiceTypes WHERE ServiceTypes.Id = Meters.ServiceTypeId AND ServiceTypes.Name = Meters.Name);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccountNumber",
                table: "Meters");

            migrationBuilder.DropColumn(
                name: "MeterNumber",
                table: "Meters");

            migrationBuilder.DropColumn(
                name: "ReplacedAt",
                table: "Meters");

            migrationBuilder.DropColumn(
                name: "ReplacedByMeterId",
                table: "Meters");

            migrationBuilder.DropColumn(
                name: "ReplacesMeterId",
                table: "Meters");
        }
    }
}
