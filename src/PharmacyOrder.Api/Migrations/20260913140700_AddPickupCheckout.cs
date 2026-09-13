using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmacyOrder.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPickupCheckout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The scaffold used integer owners. Never invent GUID ownership for legacy orders.
            // Stop before schema changes if rows need a separately reviewed ownership migration.
            RequireEmptyOrders(migrationBuilder);
            migrationBuilder.AlterColumn<Guid>(
                name: "CustomerId",
                table: "Orders",
                type: "char(36)",
                nullable: false,
                collation: "ascii_general_ci",
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "Orders",
                type: "varchar(1000)",
                maxLength: 1000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "RequestFingerprint",
                table: "Orders",
                type: "varchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<Guid>(
                name: "RequestId",
                table: "Orders",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<string>(
                name: "MedicineName",
                table: "OrderItems",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "PrescriptionId",
                table: "OrderItems",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OrderStockAllocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    OrderItemId = table.Column<int>(type: "int", nullable: false),
                    StockId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderStockAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderStockAllocations_OrderItems_OrderItemId",
                        column: x => x.OrderItemId,
                        principalTable: "OrderItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrderStockAllocations_Stocks_StockId",
                        column: x => x.StockId,
                        principalTable: "Stocks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CustomerId_RequestId",
                table: "Orders",
                columns: new[] { "CustomerId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_PrescriptionId",
                table: "OrderItems",
                column: "PrescriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderStockAllocations_OrderItemId",
                table: "OrderStockAllocations",
                column: "OrderItemId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderStockAllocations_StockId",
                table: "OrderStockAllocations",
                column: "StockId");

            migrationBuilder.AddForeignKey(
                name: "FK_OrderItems_Prescriptions_PrescriptionId",
                table: "OrderItems",
                column: "PrescriptionId",
                principalTable: "Prescriptions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RequireEmptyOrders(migrationBuilder);
            migrationBuilder.DropForeignKey(
                name: "FK_OrderItems_Prescriptions_PrescriptionId",
                table: "OrderItems");

            migrationBuilder.DropTable(
                name: "OrderStockAllocations");

            migrationBuilder.DropIndex(
                name: "IX_Orders_CustomerId_RequestId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_OrderItems_PrescriptionId",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "RequestFingerprint",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "RequestId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "MedicineName",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "PrescriptionId",
                table: "OrderItems");

            migrationBuilder.AlterColumn<int>(
                name: "CustomerId",
                table: "Orders",
                type: "int",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "char(36)")
                .OldAnnotation("Relational:Collation", "ascii_general_ci");
        }

        private static void RequireEmptyOrders(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TEMPORARY TABLE IF EXISTS GCN31_existing_orders_need_owner_mapping;
                CREATE TEMPORARY TABLE GCN31_existing_orders_need_owner_mapping (
                    GuardId INT NOT NULL PRIMARY KEY
                );
                INSERT INTO GCN31_existing_orders_need_owner_mapping VALUES (1);
                INSERT INTO GCN31_existing_orders_need_owner_mapping SELECT 1 FROM Orders LIMIT 1;
                DROP TEMPORARY TABLE GCN31_existing_orders_need_owner_mapping;
                """);
        }
    }
}
