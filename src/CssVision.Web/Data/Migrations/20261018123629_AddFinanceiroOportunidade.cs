using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFinanceiroOportunidade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OportunidadeId",
                table: "CrmFinanceiroVendas",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CrmFinanceiroVendas_UsuarioId_OportunidadeId",
                table: "CrmFinanceiroVendas",
                columns: new[] { "UsuarioId", "OportunidadeId" },
                unique: true,
                filter: "\"OportunidadeId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CrmFinanceiroVendas_UsuarioId_OportunidadeId",
                table: "CrmFinanceiroVendas");

            migrationBuilder.DropColumn(
                name: "OportunidadeId",
                table: "CrmFinanceiroVendas");
        }
    }
}
