using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using PharmacyOrder.Api.Data;

#nullable disable

namespace PharmacyOrder.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260914150000_AddPatientNotifications")]
public partial class AddPatientNotifications : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PatientNotifications",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false).Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                EventId = table.Column<Guid>(type: "char(36)", nullable: false),
                PatientId = table.Column<Guid>(type: "char(36)", nullable: false),
                Title = table.Column<string>(type: "longtext", nullable: false),
                Message = table.Column<string>(type: "longtext", nullable: false),
                RelatedEntityType = table.Column<string>(type: "longtext", nullable: false),
                RelatedEntityId = table.Column<int>(type: "int", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                IsRead = table.Column<bool>(type: "tinyint(1)", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_PatientNotifications", x => x.Id))
            .Annotation("MySql:CharSet", "utf8mb4");
        migrationBuilder.CreateIndex(name: "IX_PatientNotifications_PatientId_EventId", table: "PatientNotifications", columns: new[] { "PatientId", "EventId" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(name: "PatientNotifications");
}
