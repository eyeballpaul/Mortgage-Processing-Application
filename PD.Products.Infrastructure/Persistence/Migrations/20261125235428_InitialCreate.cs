using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace PD.Products.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MortgageProducts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    InterestRate = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    Fee = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Aprc = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    FixedTermYears = table.Column<int>(type: "int", nullable: false),
                    MaximumLoanToValue = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MortgageProducts", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "MortgageProducts",
                columns: new[] { "Id", "Aprc", "Fee", "FixedTermYears", "InterestRate", "MaximumLoanToValue", "Name" },
                values: new object[,]
                {
                    { "fixed-2", 5.9m, 999m, 2, 4.29m, 90, "2 year fixed" },
                    { "fixed-5", 5.5m, 999m, 5, 4.09m, 90, "5 year fixed" },
                    { "fixed-5-95", 6.4m, 999m, 5, 5.19m, 95, "5 year fixed · 95% LTV" },
                    { "tracker-2", 5.8m, 0m, 2, 4.64m, 85, "2 year tracker" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MortgageProducts");
        }
    }
}
