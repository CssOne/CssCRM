using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSegundaColunaEmAtendimentoIndicacao : Migration
    {
        private static readonly Guid NovaColunaId = Guid.Parse("6a2e1c2c-2f3a-4f0a-9b1e-3e0f7c2a9d10");

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Abre espaço na ordem (Em atendimento=1) pra inserir a segunda coluna logo depois.
            migrationBuilder.Sql(
                """
                UPDATE "CrmLeadStages" SET "Ordem" = "Ordem" + 1 WHERE "Ordem" >= 2;
                """);

            migrationBuilder.Sql(
                $"""
                INSERT INTO "CrmLeadStages" ("Id", "Nome", "Ordem", "Ativa", "Fechada", "Cor", "CriadoEm")
                SELECT '{NovaColunaId}', 'Em atendimento', 2, true, false, "Cor", now()
                FROM "CrmLeadStages" WHERE "Nome" = 'Em atendimento' AND "Ordem" = 1;
                """);

            // Migra pra nova coluna só os leads que hoje estão na primeira "Em atendimento" E têm a
            // etiqueta "Indicação" (mesma regra usada no cartão do quadro de leads: cadastro manual
            // ou classificação Notion "Indicação").
            migrationBuilder.Sql(
                $"""
                UPDATE "CrmLeads"
                SET "EtapaId" = '{NovaColunaId}'
                WHERE "EtapaId" = (SELECT "Id" FROM "CrmLeadStages" WHERE "Nome" = 'Em atendimento' AND "Ordem" = 1)
                  AND ("CriadoManualmente" = true OR "TipoIndicacao" ILIKE 'Indicação');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                $"""
                UPDATE "CrmLeads"
                SET "EtapaId" = (SELECT "Id" FROM "CrmLeadStages" WHERE "Nome" = 'Em atendimento' AND "Ordem" = 1)
                WHERE "EtapaId" = '{NovaColunaId}';
                """);

            migrationBuilder.Sql(
                $"""
                DELETE FROM "CrmLeadStages" WHERE "Id" = '{NovaColunaId}';
                """);

            migrationBuilder.Sql(
                """
                UPDATE "CrmLeadStages" SET "Ordem" = "Ordem" - 1 WHERE "Ordem" >= 2;
                """);
        }
    }
}
