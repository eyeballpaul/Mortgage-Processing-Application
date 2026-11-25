using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PD.Mortgages.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveMortgageDocumentContents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [MortgageDocumentContents])
                    THROW 51000, 'Export existing document contents before applying this migration.', 1;
                """);

            migrationBuilder.DropTable(
                name: "MortgageDocumentContents");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MortgageDocumentContents",
                columns: table => new
                {
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Contents = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MortgageDocumentContents", x => new { x.ApplicationId, x.DocumentId });
                    table.ForeignKey(
                        name: "FK_MortgageDocumentContents_MortgageApplications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "MortgageApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }
    }
}
