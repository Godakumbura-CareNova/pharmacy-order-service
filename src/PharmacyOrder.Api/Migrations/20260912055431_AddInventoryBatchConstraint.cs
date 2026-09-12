using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmacyOrder.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryBatchConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Stocks_MedicineId",
                table: "Stocks");

            migrationBuilder.AlterColumn<string>(
                name: "BatchNumber",
                table: "Stocks",
                type: "varchar(255)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Stocks_MedicineId_BatchNumber",
                table: "Stocks",
                columns: new[] { "MedicineId", "BatchNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Stocks_MedicineId_BatchNumber",
                table: "Stocks");

            migrationBuilder.AlterColumn<string>(
                name: "BatchNumber",
                table: "Stocks",
                type: "longtext",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(255)")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Stocks_MedicineId",
                table: "Stocks",
                column: "MedicineId");
        }
    }
}
