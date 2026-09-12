using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmacyOrder.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPatientCartManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CartItems_Medicines_MedicineId",
                table: "CartItems");

            migrationBuilder.AlterColumn<Guid>(
                name: "CustomerId",
                table: "Carts",
                type: "char(36)",
                nullable: false,
                collation: "ascii_general_ci",
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "PrescriptionId",
                table: "CartItems",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Carts_CustomerId",
                table: "Carts",
                column: "CustomerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CartItems_PrescriptionId",
                table: "CartItems",
                column: "PrescriptionId");

            migrationBuilder.AddForeignKey(
                name: "FK_CartItems_Medicines_MedicineId",
                table: "CartItems",
                column: "MedicineId",
                principalTable: "Medicines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CartItems_Prescriptions_PrescriptionId",
                table: "CartItems",
                column: "PrescriptionId",
                principalTable: "Prescriptions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CartItems_Medicines_MedicineId",
                table: "CartItems");

            migrationBuilder.DropForeignKey(
                name: "FK_CartItems_Prescriptions_PrescriptionId",
                table: "CartItems");

            migrationBuilder.DropIndex(
                name: "IX_Carts_CustomerId",
                table: "Carts");

            migrationBuilder.DropIndex(
                name: "IX_CartItems_PrescriptionId",
                table: "CartItems");

            migrationBuilder.DropColumn(
                name: "PrescriptionId",
                table: "CartItems");

            migrationBuilder.AlterColumn<int>(
                name: "CustomerId",
                table: "Carts",
                type: "int",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "char(36)")
                .OldAnnotation("Relational:Collation", "ascii_general_ci");

            migrationBuilder.AddForeignKey(
                name: "FK_CartItems_Medicines_MedicineId",
                table: "CartItems",
                column: "MedicineId",
                principalTable: "Medicines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
