using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class LevaParaVendaConcluidaOsCardsDeVendasRecentes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Vendas concluídas desde a tarde de 09/10/2026 cujo card do lead ficou parado em "Em atendimento"/"Cotação": o quadro movia o card num
            // segundo passo, que falhava por conflito de versão depois de a venda ser ganha. Leva o card para "Venda concluída (Leads)" ou "(Indicação)"
            // (pela etiqueta do cliente; lead que fechou como indicação vira "Indicação Lead"), como o servidor passa a fazer ao concluir a venda.
            // Só mexe em lead com venda ganha e ativa lançada a partir de então e que não esteja em "Venda concluída", "Perdido" ou "Não fazemos" —
            // o que é anterior (muito vindo do Notion, que decide a coluna pelo Status) fica como está. Idempotente.
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
                      AND o."EtapaDesde" >= TIMESTAMPTZ '2026-10-09 14:30:00+00'
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
