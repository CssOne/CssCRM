using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class DataDaVendaDoNotionAoMeioDia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // "Data da venda" do Notion vem só com o dia e foi gravada à meia-noite UTC — 21h do dia anterior em Brasília —, então a
            // venda de hoje contava como de ontem (e a do dia 1º, como do mês passado). Passa para o meio-dia UTC (9h em Brasília),
            // no dia certo. Só mexe nas vendas do Notion que estão exatamente à meia-noite UTC; rodar de novo não muda nada.
            migrationBuilder.Sql("""
                UPDATE "CrmOpportunities"
                SET "DataEfetivaFechamento" = "DataEfetivaFechamento" + interval '12 hours'
                WHERE "NotionPageId" IS NOT NULL
                  AND "DataEfetivaFechamento" IS NOT NULL
                  AND ("DataEfetivaFechamento" AT TIME ZONE 'UTC')::time = TIME '00:00:00';

                UPDATE "CrmOpportunities"
                SET "EtapaDesde" = "EtapaDesde" + interval '12 hours'
                WHERE "NotionPageId" IS NOT NULL
                  AND "EtapaDesde" IS NOT NULL
                  AND ("EtapaDesde" AT TIME ZONE 'UTC')::time = TIME '00:00:00';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
