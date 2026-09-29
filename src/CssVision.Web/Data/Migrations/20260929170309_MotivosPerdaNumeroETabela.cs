using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <summary>
    /// Motivos de perda novos ("Número não existe", "Fora da tabela de aceitação") e fim do motivo
    /// "Não informado no Notion": ele sai das opções e os leads/vendas que o tinham ficam "sem motivo
    /// informado" (o que ele de fato significava).
    /// </summary>
    public partial class MotivosPerdaNumeroETabela : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO "CrmLossReasons" ("Id", "Descricao", "Ativo", "CriadoEm")
                SELECT gen_random_uuid(), novo.descricao, true, now()
                FROM (VALUES ('Número não existe'), ('Fora da tabela de aceitação')) AS novo(descricao)
                WHERE NOT EXISTS (SELECT 1 FROM "CrmLossReasons" m WHERE lower(m."Descricao") = lower(novo.descricao));

                UPDATE "CrmLeads" SET "MotivoPerdaId" = NULL
                WHERE "MotivoPerdaId" IN (SELECT "Id" FROM "CrmLossReasons" WHERE "Descricao" = 'Não informado no Notion');

                UPDATE "CrmOpportunities" SET "MotivoPerdaId" = NULL
                WHERE "MotivoPerdaId" IN (SELECT "Id" FROM "CrmLossReasons" WHERE "Descricao" = 'Não informado no Notion');

                UPDATE "CrmLossReasons" SET "Ativo" = false WHERE "Descricao" = 'Não informado no Notion';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Sem volta: não dá para saber quais leads tinham "Não informado no Notion".
            migrationBuilder.Sql("""
                UPDATE "CrmLossReasons" SET "Ativo" = true WHERE "Descricao" = 'Não informado no Notion';
                """);
        }
    }
}
