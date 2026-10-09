using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class TiraTrafegoPagoDaColunaDeIndicacao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Leads de anúncio (ID de lead da Meta ou GCLID) que a sincronização do Notion mandou para "Em atendimento (Indicação)" só porque o
            // "O que" do card veio sem acento ("AGV ELETRICO") ou vazio. Voltam para "Em atendimento (Leads)" como "Lead". Fora daqui ficam os
            // cards cujo "O que" já é Lead conhecido (AGV, AGV TRUCK...) e ainda assim estão como Indicação — não dá para saber, só pelo banco,
            // se foi escolha no Notion ou no CRM. Idempotente: só age enquanto o lead estiver na coluna de Indicação com a etiqueta "Indicação".
            migrationBuilder.Sql("""
                UPDATE "CrmLeads" l
                SET "TipoIndicacao" = 'Lead',
                    "CriadoManualmente" = FALSE,
                    "EtapaId" = (SELECT "Id" FROM "CrmLeadStages" WHERE "Nome" = 'Em atendimento (Leads)' LIMIT 1)
                WHERE l."EtapaId" = (SELECT "Id" FROM "CrmLeadStages" WHERE "Nome" = 'Em atendimento (Indicação)' LIMIT 1)
                  AND EXISTS (SELECT 1 FROM "CrmLeadStages" WHERE "Nome" = 'Em atendimento (Leads)')
                  AND l."Arquivado" = FALSE
                  AND l."TipoIndicacao" = 'Indicação'
                  AND l."IndicadoPorLeadId" IS NULL
                  AND l."ConsentimentoOrigem" IN ('Sincronização automática (Notion)', 'Migração da base histórica (Notion)')
                  AND (l."MetaLeadId" IS NOT NULL OR l."Gclid" IS NOT NULL)
                  AND (l."ProdutoInteresse" IS NULL
                       OR btrim(l."ProdutoInteresse") = ''
                       OR upper(btrim(l."ProdutoInteresse")) IN ('AGV ELETRICO', 'AGV ELÉTRICO'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
