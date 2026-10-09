using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class CorrigeVendaIndicacaoNsu1b13 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Venda de indicação (NSU1B13, João Henrique) concluída sem marcar "indicação": o card caiu em "Venda concluída (Leads)" e a
            // edição posterior do cadastro só trocou a etiqueta para "Indicação Lead". Leva o card e a oportunidade para onde deveriam estar.
            // O valor da indicação fica em branco para ser preenchido no CRM. Idempotente: só age enquanto o card estiver em "(Leads)".
            migrationBuilder.Sql("""
                UPDATE "CrmOpportunities" o
                SET "Indicacao" = TRUE, "TipoIndicacao" = COALESCE(o."TipoIndicacao", 'Indicação Lead')
                FROM "CrmLeads" l
                JOIN "CrmLeadStages" e ON e."Id" = l."EtapaId"
                WHERE o."LeadId" = l."Id"
                  AND l."Id" = '69ac1cdd-9b15-47f3-b5d1-58438c72ae3e'
                  AND e."Nome" = 'Venda concluída (Leads)'
                  AND o."Indicacao" IS NOT TRUE;

                UPDATE "CrmLeads" l
                SET "EtapaId" = d."Id"
                FROM "CrmLeadStages" d
                WHERE l."Id" = '69ac1cdd-9b15-47f3-b5d1-58438c72ae3e'
                  AND d."Nome" = 'Venda concluída (Indicação)'
                  AND l."EtapaId" = (SELECT "Id" FROM "CrmLeadStages" WHERE "Nome" = 'Venda concluída (Leads)' LIMIT 1);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
