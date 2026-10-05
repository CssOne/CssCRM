using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class VendasSemDataContamNoMesDeChegada : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Vendas (etapa Ganho) cadastradas no Notion sem "Data da venda" ficavam sem data de fechamento e sumiam da
            // Evolução de vendas, do Resumo por mês, do ranking e do Desempenho por vendedor. Passam a contar no mês em que o
            // lead chegou; quando a data for preenchida no Notion, a sincronização a substitui.
            migrationBuilder.Sql("""
                UPDATE "CrmOpportunities" o
                SET "DataEfetivaFechamento" = l."CriadoEm"
                FROM "CrmLeads" l, "CrmPipelineStages" s
                WHERE l."Id" = o."LeadId"
                  AND s."Id" = o."EtapaId"
                  AND s."Tipo" = 2
                  AND NOT o."Arquivado"
                  AND o."DataEfetivaFechamento" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
