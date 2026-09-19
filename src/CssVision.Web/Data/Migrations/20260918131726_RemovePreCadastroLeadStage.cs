using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemovePreCadastroLeadStage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Move todo lead que estava em "Pré-cadastro" para "Cotação" antes de desativar a coluna
            // — senão esses leads ficariam órfãos (etapa inativa não aparece mais em nenhuma coluna).
            migrationBuilder.Sql(
                """
                UPDATE "CrmLeads"
                SET "EtapaId" = (SELECT "Id" FROM "CrmLeadStages" WHERE "Nome" = 'Cotação')
                WHERE "EtapaId" = (SELECT "Id" FROM "CrmLeadStages" WHERE "Nome" = 'Pré-cadastro');
                """);

            migrationBuilder.Sql(
                """
                UPDATE "CrmLeadStages" SET "Ativa" = false WHERE "Nome" = 'Pré-cadastro';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reativa a coluna, mas não há como recuperar quais leads estavam originalmente nela —
            // eles permanecem em "Cotação".
            migrationBuilder.Sql(
                """
                UPDATE "CrmLeadStages" SET "Ativa" = true WHERE "Nome" = 'Pré-cadastro';
                """);
        }
    }
}
