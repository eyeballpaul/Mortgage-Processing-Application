using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PD.Mortgages.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SubmissionNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SimulatedNotifications",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SimulatedNotifications", x => x.EventId);
                });

            migrationBuilder.CreateTable(
                name: "SubmissionOutbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LeaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LeaseUntilUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubmissionOutbox", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionOutbox_ApplicationId",
                table: "SubmissionOutbox",
                column: "ApplicationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionOutbox_PublishedAtUtc_LeaseUntilUtc_OccurredAtUtc",
                table: "SubmissionOutbox",
                columns: new[] { "PublishedAtUtc", "LeaseUntilUtc", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SimulatedNotifications");

            migrationBuilder.DropTable(
                name: "SubmissionOutbox");
        }
    }
}
