using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Utilities.Infrastructure.Persistence;

#nullable disable

namespace Utilities.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(UtilitiesDbContext))]
    [Migration("20261027120000_AddReadingReminderFollowUp")]
    public partial class AddReadingReminderFollowUp : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>("FollowUpCycleAt", "ReminderSettings", type: "INTEGER", nullable: true);
            migrationBuilder.AddColumn<long>("FollowUpNextAt", "ReminderSettings", type: "INTEGER", nullable: true);
            migrationBuilder.AddColumn<int>("FollowUpStatus", "ReminderSettings", type: "INTEGER", nullable: false, defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn("FollowUpCycleAt", "ReminderSettings");
            migrationBuilder.DropColumn("FollowUpNextAt", "ReminderSettings");
            migrationBuilder.DropColumn("FollowUpStatus", "ReminderSettings");
        }
    }
}
