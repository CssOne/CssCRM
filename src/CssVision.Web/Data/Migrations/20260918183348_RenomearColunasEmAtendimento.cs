using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenomearColunasEmAtendimento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A de menor Ordem é a que só aceita "Lead" (ver AddSegundaColunaEmAtendimentoIndicacao);
            // renomeada primeiro, sobra só uma linha com "Em atendimento" pro segundo UPDATE pegar.
            migrationBuilder.Sql(
                """
                UPDATE "CrmLeadStages" SET "Nome" = 'Em atendimento (Leads)'
                WHERE "Id" = (SELECT "Id" FROM "CrmLeadStages" WHERE "Nome" = 'Em atendimento' ORDER BY "Ordem" ASC LIMIT 1);
                """);

            migrationBuilder.Sql(
                """
                UPDATE "CrmLeadStages" SET "Nome" = 'Em atendimento (Indicação)' WHERE "Nome" = 'Em atendimento';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "CrmLeadStages" SET "Nome" = 'Em atendimento' WHERE "Nome" IN ('Em atendimento (Leads)', 'Em atendimento (Indicação)');
                """);
        }
    }
}
