using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class DataDaVendaDoFormularioAoMeioDia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Vendas registradas no próprio CRM (formulário de venda) guardavam a "Data da venda" à meia-noite UTC — 21h do dia
            // anterior em Brasília —, então a venda de hoje contava como de ontem (e a do dia 1º, como do mês passado). Passa para o
            // meio-dia UTC, no dia certo. Só mexe no que está exatamente à meia-noite UTC (um instante real nunca cai aí); rodar de novo
            // não muda nada.
            migrationBuilder.Sql("""
                UPDATE "CrmOpportunities"
                SET "DataEfetivaFechamento" = "DataEfetivaFechamento" + interval '12 hours'
                WHERE "DataEfetivaFechamento" IS NOT NULL
                  AND ("DataEfetivaFechamento" AT TIME ZONE 'UTC')::time = TIME '00:00:00';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
