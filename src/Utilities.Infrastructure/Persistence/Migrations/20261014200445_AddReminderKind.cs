using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Utilities.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReminderKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReminderSettings_OwnerType_OwnerId",
                table: "ReminderSettings");

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "ReminderSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("UPDATE ReminderSettings SET Kind = 1 WHERE OwnerType = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ReminderSettings_OwnerType_OwnerId_Kind",
                table: "ReminderSettings",
                columns: new[] { "OwnerType", "OwnerId", "Kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReminderSettings_OwnerType_OwnerId_Kind",
                table: "ReminderSettings");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "ReminderSettings");

            migrationBuilder.CreateIndex(
                name: "IX_ReminderSettings_OwnerType_OwnerId",
                table: "ReminderSettings",
                columns: new[] { "OwnerType", "OwnerId" });
        }
    }
}
