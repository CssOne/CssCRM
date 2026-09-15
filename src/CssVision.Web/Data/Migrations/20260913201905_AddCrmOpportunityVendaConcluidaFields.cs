using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCrmOpportunityVendaConcluidaFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Cpf",
                table: "CrmOpportunities",
                type: "character varying(14)",
                maxLength: 14,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Estado",
                table: "CrmOpportunities",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Indicacao",
                table: "CrmOpportunities",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PagamentoAdesaoArquivoUrl",
                table: "CrmOpportunities",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TermoAdesaoArquivoUrl",
                table: "CrmOpportunities",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TipoIndicacao",
                table: "CrmOpportunities",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Total",
                table: "CrmOpportunities",
                type: "numeric(14,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ValorIndicacao",
                table: "CrmOpportunities",
                type: "numeric(14,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Cpf",
                table: "CrmOpportunities");

            migrationBuilder.DropColumn(
                name: "Estado",
                table: "CrmOpportunities");

            migrationBuilder.DropColumn(
                name: "Indicacao",
                table: "CrmOpportunities");

            migrationBuilder.DropColumn(
                name: "PagamentoAdesaoArquivoUrl",
                table: "CrmOpportunities");

            migrationBuilder.DropColumn(
                name: "TermoAdesaoArquivoUrl",
                table: "CrmOpportunities");

            migrationBuilder.DropColumn(
                name: "TipoIndicacao",
                table: "CrmOpportunities");

            migrationBuilder.DropColumn(
                name: "Total",
                table: "CrmOpportunities");

            migrationBuilder.DropColumn(
                name: "ValorIndicacao",
                table: "CrmOpportunities");
        }
    }
}
