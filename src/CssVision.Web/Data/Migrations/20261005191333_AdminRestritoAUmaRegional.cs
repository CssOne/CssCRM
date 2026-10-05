using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AdminRestritoAUmaRegional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RegionalRestritaId",
                table: "AspNetUsers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_RegionalRestritaId",
                table: "AspNetUsers",
                column: "RegionalRestritaId");

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_CrmRegionais_RegionalRestritaId",
                table: "AspNetUsers",
                column: "RegionalRestritaId",
                principalTable: "CrmRegionais",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_CrmRegionais_RegionalRestritaId",
                table: "AspNetUsers");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_RegionalRestritaId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "RegionalRestritaId",
                table: "AspNetUsers");
        }
    }
}
