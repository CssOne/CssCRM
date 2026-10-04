using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class ArquivaLeadsMg134 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Importacao2025ConcluidaEm",
                table: "CrmNotionSyncCheckpoints",
                type: "timestamp with time zone",
                nullable: true);

            // Leads da regional MG134 saem do quadro (pedido de 04/10/2026). Arquivar (e não apagar) é o
            // mesmo que a exclusão pelo CRM; os usuários da MG134 e as oportunidades ficam como estão.
            // "MG134 Consultores Externos" é outra base e não é afetada (match exato).
            migrationBuilder.Sql("""
                UPDATE "CrmLeads" SET "Arquivado" = TRUE, "ArquivadoEm" = now()
                WHERE "Regional" = 'MG134' AND "Arquivado" = FALSE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Importacao2025ConcluidaEm",
                table: "CrmNotionSyncCheckpoints");
        }
    }
}
