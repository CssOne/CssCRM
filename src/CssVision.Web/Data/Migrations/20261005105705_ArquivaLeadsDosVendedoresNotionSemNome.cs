using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class ArquivaLeadsDosVendedoresNotionSemNome : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Vendedores que o Notion não identifica ("Vendedor Notion xxxxxxxx-xxxx", criados pela sincronização) ficam
            // fora do sistema por enquanto (pedido de 05/10/2026): os leads deles e as vendas (oportunidades) são
            // arquivados — como na exclusão pelo CRM, sem apagar nada — e os usuários continuam, inativos e escondidos
            // das telas, para a revisão futura. Quem já foi renomeado em Usuários não entra.
            migrationBuilder.Sql("""
                UPDATE "CrmOpportunities" o SET "Arquivado" = TRUE, "ArquivadoEm" = now()
                WHERE NOT o."Arquivado" AND (
                    o."ResponsavelId" IN (SELECT "Id" FROM "AspNetUsers" WHERE "NotionUserId" IS NOT NULL AND "NomeCompleto" LIKE 'Vendedor Notion %')
                    OR o."LeadId" IN (SELECT l."Id" FROM "CrmLeads" l JOIN "AspNetUsers" u ON u."Id" = l."ResponsavelId"
                                      WHERE u."NotionUserId" IS NOT NULL AND u."NomeCompleto" LIKE 'Vendedor Notion %'));

                UPDATE "CrmLeads" l SET "Arquivado" = TRUE, "ArquivadoEm" = now()
                FROM "AspNetUsers" u
                WHERE u."Id" = l."ResponsavelId" AND NOT l."Arquivado"
                  AND u."NotionUserId" IS NOT NULL AND u."NomeCompleto" LIKE 'Vendedor Notion %';

                UPDATE "AspNetUsers" SET "Ativo" = FALSE, "RecebeLeads" = FALSE
                WHERE "NotionUserId" IS NOT NULL AND "NomeCompleto" LIKE 'Vendedor Notion %';
                """);


        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
