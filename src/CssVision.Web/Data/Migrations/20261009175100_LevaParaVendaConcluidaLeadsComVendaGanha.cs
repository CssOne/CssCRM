using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class LevaParaVendaConcluidaLeadsComVendaGanha : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Regra do sistema: lead com venda ganha (oportunidade em "Ganho" não excluída) fica em "Venda concluída (Leads|Indicação)".
            // A migration anterior só pegou as vendas a partir de 09/10/2026; esta pega as demais (cards vindos do Notion com o Status
            // atrasado, vendas sem data de fechamento...). Não mexe em "Perdido" nem "Não fazemos" (decisão explícita). Idempotente.
            migrationBuilder.Sql("""
                WITH alvo AS (
                    SELECT l."Id" AS lead_id,
                           (COALESCE(lower(btrim(l."TipoIndicacao")), '') <> 'lead'
                            AND (l."CriadoManualmente" OR COALESCE(btrim(l."TipoIndicacao"), '') <> '')) AS eh_indicacao,
                           (COALESCE(lower(btrim(l."TipoIndicacao")), '') = 'lead' AND bool_or(COALESCE(o."Indicacao", FALSE))) AS lead_que_fechou_como_indicacao
                    FROM "CrmLeads" l
                    JOIN "CrmOpportunities" o ON o."LeadId" = l."Id" AND o."Arquivado" = FALSE
                    JOIN "CrmPipelineStages" ps ON ps."Id" = o."EtapaId" AND ps."Tipo" = 2
                    LEFT JOIN "CrmLeadStages" le ON le."Id" = l."EtapaId"
                    WHERE l."Arquivado" = FALSE
                      AND (le."Id" IS NULL OR (le."Nome" NOT LIKE 'Venda conclu%' AND le."Nome" NOT IN ('Perdido', 'Não fazemos')))
                    GROUP BY l."Id", l."TipoIndicacao", l."CriadoManualmente"
                )
                UPDATE "CrmLeads" l
                SET "EtapaId" = (SELECT d."Id" FROM "CrmLeadStages" d
                                 WHERE d."Ativa" AND d."Nome" = CASE WHEN a.eh_indicacao OR a.lead_que_fechou_como_indicacao
                                                                     THEN 'Venda concluída (Indicação)' ELSE 'Venda concluída (Leads)' END
                                 LIMIT 1),
                    "TipoIndicacao" = CASE WHEN a.lead_que_fechou_como_indicacao THEN 'Indicação Lead' ELSE l."TipoIndicacao" END,
                    "MotivoPerdaId" = NULL,
                    "MotivoPerdaObservacao" = NULL,
                    "VeiculoNaoAtendido" = NULL,
                    "AtualizadoEm" = now()
                FROM alvo a
                WHERE l."Id" = a.lead_id
                  AND EXISTS (SELECT 1 FROM "CrmLeadStages" d
                              WHERE d."Ativa" AND d."Nome" = CASE WHEN a.eh_indicacao OR a.lead_que_fechou_como_indicacao
                                                                  THEN 'Venda concluída (Indicação)' ELSE 'Venda concluída (Leads)' END);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
