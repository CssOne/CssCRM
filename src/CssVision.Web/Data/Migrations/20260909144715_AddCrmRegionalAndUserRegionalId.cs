using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCrmRegionalAndUserRegionalId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RegionalId",
                table: "AspNetUsers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CrmRegionais",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Ativa = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AtualizadoPorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmRegionais", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_RegionalId",
                table: "AspNetUsers",
                column: "RegionalId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmRegionais_Ativa",
                table: "CrmRegionais",
                column: "Ativa");

            migrationBuilder.CreateIndex(
                name: "IX_CrmRegionais_Nome",
                table: "CrmRegionais",
                column: "Nome",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_CrmRegionais_RegionalId",
                table: "AspNetUsers",
                column: "RegionalId",
                principalTable: "CrmRegionais",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_CrmRegionais_RegionalId",
                table: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "CrmRegionais");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_RegionalId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "RegionalId",
                table: "AspNetUsers");
        }
    }
}
