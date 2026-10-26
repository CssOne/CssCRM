using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class LevaCotacoesComAdesaoParaOPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Lead que já estava em "Cotação" com a adesão preenchida antes de o servidor passar a levá-lo sozinho para "Proposta enviada" no
            // pipeline (LeadService.GarantirPropostaEnviadaNoPipelineAsync). Mesma regra: sem oportunidade aberta, cria uma com a adesão como valor;
            // com oportunidade aberta numa etapa anterior, avança para "Proposta enviada" (etapas adiante ficam como estão). Lead arquivado, sem
            // responsável ou com venda ganha não entra. Idempotente: depois de rodar, o lead já tem oportunidade em "Proposta enviada" ou adiante.
            migrationBuilder.Sql("""
                CREATE TEMP TABLE _cotacao_alvo ON COMMIT DROP AS
                SELECT l."Id" AS lead_id, l."ResponsavelId" AS responsavel_id, l."NomeOuRazaoSocial" AS nome, l."ValorAdesao" AS adesao
                FROM "CrmLeads" l
                JOIN "CrmLeadStages" le ON le."Id" = l."EtapaId" AND le."Nome" = 'Cotação'
                WHERE l."Arquivado" = FALSE
                  AND l."ResponsavelId" IS NOT NULL
                  AND COALESCE(l."ValorAdesao", 0) > 0
                  AND NOT EXISTS (SELECT 1 FROM "CrmOpportunities" o
                                  JOIN "CrmPipelineStages" ps ON ps."Id" = o."EtapaId"
                                  WHERE o."LeadId" = l."Id" AND o."Arquivado" = FALSE AND ps."Tipo" = 2);

                CREATE TEMP TABLE _proposta ON COMMIT DROP AS
                SELECT "Id" AS id, "Ordem" AS ordem FROM "CrmPipelineStages"
                WHERE "Ativa" AND "Tipo" = 1 AND "Nome" = 'Proposta enviada' LIMIT 1;

                CREATE TEMP TABLE _novas ON COMMIT DROP AS
                SELECT gen_random_uuid() AS opp_id, a.*
                FROM _cotacao_alvo a
                WHERE EXISTS (SELECT 1 FROM _proposta)
                  AND NOT EXISTS (SELECT 1 FROM "CrmOpportunities" o
                                  JOIN "CrmPipelineStages" ps ON ps."Id" = o."EtapaId"
                                  WHERE o."LeadId" = a.lead_id AND o."Arquivado" = FALSE AND ps."Tipo" = 1);

                INSERT INTO "CrmOpportunities" ("Id", "LeadId", "Titulo", "ResponsavelId", "EtapaId", "EtapaDesde", "ValorEstimado",
                                                "Arquivado", "Migracao", "TermoAdesaoAceito", "CriadoEm")
                SELECT n.opp_id, n.lead_id, left('Cotação — ' || n.nome, 200), n.responsavel_id, p.id, now(), n.adesao,
                       FALSE, FALSE, FALSE, now()
                FROM _novas n CROSS JOIN _proposta p;

                INSERT INTO "CrmStageHistories" ("Id", "OpportunityId", "EtapaAnteriorId", "EtapaNovaId", "UsuarioId", "AlteradoEm")
                SELECT gen_random_uuid(), n.opp_id, NULL, p.id, n.responsavel_id, now()
                FROM _novas n CROSS JOIN _proposta p;

                CREATE TEMP TABLE _avancar ON COMMIT DROP AS
                SELECT DISTINCT ON (a.lead_id) o."Id" AS opp_id, o."EtapaId" AS etapa_anterior, a.responsavel_id
                FROM _cotacao_alvo a
                JOIN "CrmOpportunities" o ON o."LeadId" = a.lead_id AND o."Arquivado" = FALSE
                JOIN "CrmPipelineStages" ps ON ps."Id" = o."EtapaId" AND ps."Tipo" = 1
                CROSS JOIN _proposta p
                WHERE ps."Ordem" < p.ordem
                ORDER BY a.lead_id, o."CriadoEm" DESC;

                INSERT INTO "CrmStageHistories" ("Id", "OpportunityId", "EtapaAnteriorId", "EtapaNovaId", "UsuarioId", "AlteradoEm")
                SELECT gen_random_uuid(), v.opp_id, v.etapa_anterior, p.id, v.responsavel_id, now()
                FROM _avancar v CROSS JOIN _proposta p;

                UPDATE "CrmOpportunities" o
                SET "EtapaId" = p.id, "EtapaDesde" = now(), "AtualizadoEm" = now()
                FROM _avancar v CROSS JOIN _proposta p
                WHERE o."Id" = v.opp_id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
