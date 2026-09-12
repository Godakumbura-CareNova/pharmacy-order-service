using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmacyOrder.Api.Migrations
{
    public partial class ChangePrescriptionOwnerToGuid : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "UploadedBy",
                table: "Prescriptions",
                type: "char(36)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "UploadedBy",
                table: "Prescriptions",
                type: "int",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "char(36)");
        }
    }
}
