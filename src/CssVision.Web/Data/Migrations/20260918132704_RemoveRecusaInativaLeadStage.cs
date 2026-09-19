using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveRecusaInativaLeadStage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Move todo lead que estava em "Recusa/Inativa" para "Perdido" antes de desativar a
            // coluna — senão esses leads ficariam órfãos (etapa inativa não aparece em nenhuma coluna).
            migrationBuilder.Sql(
                """
                UPDATE "CrmLeads"
                SET "EtapaId" = (SELECT "Id" FROM "CrmLeadStages" WHERE "Nome" = 'Perdido')
                WHERE "EtapaId" = (SELECT "Id" FROM "CrmLeadStages" WHERE "Nome" = 'Recusa/Inativa');
                """);

            migrationBuilder.Sql(
                """
                UPDATE "CrmLeadStages" SET "Ativa" = false WHERE "Nome" = 'Recusa/Inativa';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reativa a coluna, mas não há como recuperar quais leads estavam originalmente nela —
            // eles permanecem em "Perdido".
            migrationBuilder.Sql(
                """
                UPDATE "CrmLeadStages" SET "Ativa" = true WHERE "Nome" = 'Recusa/Inativa';
                """);
        }
    }
}
