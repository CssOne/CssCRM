using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class ArquivaLeadsMg134ExternosERenomeiaProvisorios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Leads da regional "MG134 Consultores Externos" saem do quadro (pedido de 05/10/2026), como os da MG134:
            //    arquivados (não apagados); usuários e oportunidades ficam.
            migrationBuilder.Sql("""
                UPDATE "CrmLeads" SET "Arquivado" = TRUE, "ArquivadoEm" = now()
                WHERE "Regional" = 'MG134 Consultores Externos' AND "Arquivado" = FALSE;
                """);

            // 2) Nome único para quem o Notion não identifica: muitos ids dividem os 8 primeiros caracteres, então o
            //    nome "Vendedor Notion 391d872b" aparecia repetido. Passa a "Vendedor Notion 391d872b-8693" (início-fim).
            migrationBuilder.Sql("""
                UPDATE "AspNetUsers"
                SET "NomeCompleto" = 'Vendedor Notion ' || LEFT("NotionUserId", 8) || '-' || RIGHT("NotionUserId", 4)
                WHERE "NotionUserId" IS NOT NULL AND LENGTH("NotionUserId") > 12
                  AND "NomeCompleto" LIKE 'Vendedor Notion %';
                """);


        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
