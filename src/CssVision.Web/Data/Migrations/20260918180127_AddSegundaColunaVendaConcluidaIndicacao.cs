using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSegundaColunaVendaConcluidaIndicacao : Migration
    {
        private static readonly Guid NovaColunaId = Guid.Parse("8f4b1a3d-6c7e-4a2b-9f10-1d5e8a2c4b90");

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Abre espaço na ordem (Venda concluída=5) pra inserir a segunda coluna logo depois.
            migrationBuilder.Sql(
                """
                UPDATE "CrmLeadStages" SET "Ordem" = "Ordem" + 1 WHERE "Ordem" >= 6;
                """);

            migrationBuilder.Sql(
                $"""
                INSERT INTO "CrmLeadStages" ("Id", "Nome", "Ordem", "Ativa", "Fechada", "Cor", "CriadoEm")
                SELECT '{NovaColunaId}', "Nome", 6, "Ativa", "Fechada", "Cor", now()
                FROM "CrmLeadStages" WHERE "Nome" = 'Venda concluída' AND "Ordem" = 5;
                """);

            // Migra pra nova coluna só os leads que hoje estão na primeira "Venda concluída" E têm a
            // etiqueta "Indicação" (mesma regra usada no cartão do quadro de leads: cadastro manual
            // ou classificação Notion "Indicação").
            migrationBuilder.Sql(
                $"""
                UPDATE "CrmLeads"
                SET "EtapaId" = '{NovaColunaId}'
                WHERE "EtapaId" = (SELECT "Id" FROM "CrmLeadStages" WHERE "Nome" = 'Venda concluída' AND "Ordem" = 5)
                  AND ("CriadoManualmente" = true OR "TipoIndicacao" ILIKE 'Indicação');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                $"""
                UPDATE "CrmLeads"
                SET "EtapaId" = (SELECT "Id" FROM "CrmLeadStages" WHERE "Nome" = 'Venda concluída' AND "Ordem" = 5)
                WHERE "EtapaId" = '{NovaColunaId}';
                """);

            migrationBuilder.Sql(
                $"""
                DELETE FROM "CrmLeadStages" WHERE "Id" = '{NovaColunaId}';
                """);

            migrationBuilder.Sql(
                """
                UPDATE "CrmLeadStages" SET "Ordem" = "Ordem" - 1 WHERE "Ordem" >= 6;
                """);
        }
    }
}
