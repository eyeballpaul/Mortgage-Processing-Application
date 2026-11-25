using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PD.Mortgages.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MortgageApplications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    IdSearchText = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByEmployeeId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    FirstName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LastName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MortgageApplications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MortgageApplicationAuditEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MortgageApplicationAuditEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MortgageApplicationAuditEntries_MortgageApplications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "MortgageApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MortgageApplicationAuditEntries_ApplicationId_RecordedAtUtc_Id",
                table: "MortgageApplicationAuditEntries",
                columns: new[] { "ApplicationId", "RecordedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_MortgageApplications_CreatedByEmployeeId",
                table: "MortgageApplications",
                column: "CreatedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_MortgageApplications_Status_UpdatedAtUtc",
                table: "MortgageApplications",
                columns: new[] { "Status", "UpdatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MortgageApplicationAuditEntries");

            migrationBuilder.DropTable(
                name: "MortgageApplications");
        }
    }
}
