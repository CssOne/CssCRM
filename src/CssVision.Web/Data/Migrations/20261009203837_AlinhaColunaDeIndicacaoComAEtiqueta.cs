using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AlinhaColunaDeIndicacaoComAEtiqueta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Lead de anúncio (ID de lead da Meta ou GCLID) vindo do Notion, de produto de Lead (AGV, APVS, Loovi...), que ficou com a etiqueta
            //    "Indicação" por engano e não tem indicação nenhuma registrada: volta a ser "Lead". Indicação de verdade (venda marcada como indicação,
            //    cliente indicado por outro lead) e as etiquetas escolhidas à mão (Pessoal, Parceria...) não são tocadas. Idempotente.
            migrationBuilder.Sql("""
                UPDATE "CrmLeads" l
                SET "TipoIndicacao" = 'Lead',
                    "CriadoManualmente" = FALSE,
                    "AtualizadoEm" = now()
                WHERE l."Arquivado" = FALSE
                  AND l."TipoIndicacao" = 'Indicação'
                  AND l."IndicadoPorLeadId" IS NULL
                  AND l."ConsentimentoOrigem" IN ('Sincronização automática (Notion)', 'Migração da base histórica (Notion)')
                  AND (l."MetaLeadId" IS NOT NULL OR l."Gclid" IS NOT NULL)
                  AND upper(btrim(COALESCE(l."ProdutoInteresse", ''))) IN ('AGV', 'AGV TRUCK', 'APVS', 'APVS TRUCK', 'LOOVI', 'AGV ELETRICO', 'AGV ELÉTRICO')
                  AND NOT EXISTS (SELECT 1 FROM "CrmOpportunities" o WHERE o."LeadId" = l."Id" AND o."Arquivado" = FALSE AND o."Indicacao" = TRUE);
                """);

            // 2) A coluna "(Leads)" / "(Indicação)" de "Em atendimento" e "Venda concluída" tem que bater com a etiqueta do lead (a mesma regra do quadro:
            //    Indicação = etiqueta diferente de "Lead" e (cadastro manual ou etiqueta preenchida)). Lead com a coluna trocada vai para a coluna irmã.
            migrationBuilder.Sql("""
                UPDATE "CrmLeads" l
                SET "EtapaId" = d."Id",
                    "AtualizadoEm" = now()
                FROM "CrmLeadStages" s, "CrmLeadStages" d
                WHERE l."EtapaId" = s."Id"
                  AND l."Arquivado" = FALSE
                  AND (s."Nome" LIKE 'Em atendimento (%' OR s."Nome" LIKE 'Venda conclu%da (%')
                  AND d."Ativa"
                  AND d."Nome" = regexp_replace(s."Nome", ' \(.*\)$', '')
                                 || CASE WHEN COALESCE(lower(btrim(l."TipoIndicacao")), '') <> 'lead'
                                          AND (l."CriadoManualmente" OR COALESCE(btrim(l."TipoIndicacao"), '') <> '')
                                         THEN ' (Indicação)' ELSE ' (Leads)' END
                  AND d."Id" <> s."Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
