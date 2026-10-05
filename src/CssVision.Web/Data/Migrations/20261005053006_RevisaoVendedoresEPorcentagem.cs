using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class RevisaoVendedoresEPorcentagem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Porcentagem das vendas vindas do Notion estava guardada como fração (0,23 = 23%); o CRM usa pontos (23).
            // Só vendas ligadas a card do Notion e com valor entre 0 e 1 — as criadas no CRM já estão em pontos.
            migrationBuilder.Sql("""
                UPDATE "CrmOpportunities" SET "Porcentagem" = "Porcentagem" * 100
                WHERE "NotionPageId" IS NOT NULL AND "Porcentagem" > 0 AND "Porcentagem" <= 1;
                """);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RevisaoVendedoresConcluidaEm",
                table: "CrmNotionSyncCheckpoints",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RevisaoVendedoresConcluidaEm",
                table: "CrmNotionSyncCheckpoints");
        }
    }
}
