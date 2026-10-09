using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class ArquivaVendasConcluidasDuplicadas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Vendas concluídas repetidas do mesmo cliente: mesmo lead, mesma placa, mesma adesão e mesma mensalidade (86 em produção, na
            // conferência de 09/10/2026). Fica a mais completa (termo, comprovante e "Ativo em" preenchidos; no empate, a
            // mais antiga) e as outras são arquivadas — a exclusão normal do CRM, que o administrador desfaz pela lixeira. Não mexe em vendas
            // com placa, adesão ou mensalidade diferentes (outro veículo ou reativação): essas não dá para afirmar que são repetidas.
            // Idempotente: depois de rodar, não sobra grupo com mais de uma venda ativa.
            migrationBuilder.Sql("""
                WITH vendas AS (
                    SELECT o."Id" AS id,
                           ROW_NUMBER() OVER (
                               PARTITION BY o."LeadId", upper(regexp_replace(v."Placa", '[^A-Za-z0-9]', '', 'g')), o."PagamentoAdesao", o."Mensalidade"
                               ORDER BY (CASE WHEN o."TermoAdesaoArquivoUrl" IS NOT NULL THEN 1 ELSE 0 END
                                       + CASE WHEN o."PagamentoAdesaoArquivoUrl" IS NOT NULL THEN 1 ELSE 0 END
                                       + CASE WHEN o."AtivoEm" IS NOT NULL THEN 1 ELSE 0 END) DESC,
                                        o."CriadoEm", o."Id") AS pos
                    FROM "CrmOpportunities" o
                    JOIN "CrmPipelineStages" s ON s."Id" = o."EtapaId"
                    JOIN "CrmVeiculos" v ON v."OpportunityId" = o."Id"
                    WHERE s."Tipo" = 2
                      AND o."Arquivado" = FALSE
                      AND v."Placa" IS NOT NULL
                      AND regexp_replace(v."Placa", '[^A-Za-z0-9]', '', 'g') <> ''
                )
                UPDATE "CrmOpportunities" o
                SET "Arquivado" = TRUE, "ArquivadoEm" = now()
                FROM vendas x
                WHERE o."Id" = x.id AND x.pos > 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
