using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReativaLeadsMg134 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A MG134 volta ao CRM (pedido de 07/10/2026): desfaz o arquivamento feito pelas migrations ArquivaLeadsMg134 (04/10)
            // e ArquivaLeadsMg134Externos (05/10, que depois moveu os leads para a regional "MG134"). O sincronizador ignora cards
            // ligados a leads arquivados, então este desarquivamento precisa vir antes de a sincronização voltar a rodar.
            // Só volta o que o sistema arquivou: exclusão feita por uma pessoa grava ArquivadoPorId (LeadService.ExcluirAsync) e
            // fica como está. Os leads não foram apagados, e usuários e oportunidades nunca saíram.
            migrationBuilder.Sql("""
                UPDATE "CrmLeads" SET "Arquivado" = FALSE, "ArquivadoEm" = NULL
                WHERE "Regional" = 'MG134' AND "Arquivado" = TRUE AND "ArquivadoPorId" IS NULL
                  AND "ArquivadoEm" >= TIMESTAMPTZ '2026-10-04 00:00:00+00';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
