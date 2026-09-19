using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenomearColunasVendaConcluida : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A de menor Ordem é a que só aceita "Lead" (ver AddSegundaColunaVendaConcluidaIndicacao);
            // renomeada primeiro, sobra só uma linha com "Venda concluída" pro segundo UPDATE pegar.
            migrationBuilder.Sql(
                """
                UPDATE "CrmLeadStages" SET "Nome" = 'Venda concluída (Leads)'
                WHERE "Id" = (SELECT "Id" FROM "CrmLeadStages" WHERE "Nome" = 'Venda concluída' ORDER BY "Ordem" ASC LIMIT 1);
                """);

            migrationBuilder.Sql(
                """
                UPDATE "CrmLeadStages" SET "Nome" = 'Venda concluída (Indicação)' WHERE "Nome" = 'Venda concluída';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "CrmLeadStages" SET "Nome" = 'Venda concluída' WHERE "Nome" IN ('Venda concluída (Leads)', 'Venda concluída (Indicação)');
                """);
        }
    }
}
