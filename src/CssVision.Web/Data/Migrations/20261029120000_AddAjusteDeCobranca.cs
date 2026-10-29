using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAjusteDeCobranca : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Dispensada",
                table: "CrmCobrancasPagamento",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "ValorAjustado",
                table: "CrmCobrancasPagamento",
                type: "numeric(14,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Dispensada",
                table: "CrmCobrancasPagamento");

            migrationBuilder.DropColumn(
                name: "ValorAjustado",
                table: "CrmCobrancasPagamento");
        }
    }
}
