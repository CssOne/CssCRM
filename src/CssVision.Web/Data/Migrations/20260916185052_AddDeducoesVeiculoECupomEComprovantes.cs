using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeducoesVeiculoECupomEComprovantes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ALTER COLUMN TYPE de varchar pra numeric exige USING explícito no Postgres — os valores
            // existentes vieram sempre de page.Number(...).ToString(InvariantCulture), então são
            // números limpos ou nulos, mas NULLIF cobre o caso improvável de string vazia.
            migrationBuilder.Sql(
                "ALTER TABLE \"CrmVeiculos\" ALTER COLUMN \"Rastreador\" TYPE numeric(14,2) USING NULLIF(\"Rastreador\", '')::numeric(14,2);");

            migrationBuilder.AddColumn<decimal>(
                name: "ValorVistoria",
                table: "CrmVeiculos",
                type: "numeric(14,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ComprovanteIndicacaoArquivoUrl",
                table: "CrmOpportunities",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ComprovanteVistoriaArquivoUrl",
                table: "CrmOpportunities",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MensalidadeComCupom",
                table: "CrmOpportunities",
                type: "numeric(14,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ValorVistoria",
                table: "CrmVeiculos");

            migrationBuilder.DropColumn(
                name: "ComprovanteIndicacaoArquivoUrl",
                table: "CrmOpportunities");

            migrationBuilder.DropColumn(
                name: "ComprovanteVistoriaArquivoUrl",
                table: "CrmOpportunities");

            migrationBuilder.DropColumn(
                name: "MensalidadeComCupom",
                table: "CrmOpportunities");

            migrationBuilder.Sql(
                "ALTER TABLE \"CrmVeiculos\" ALTER COLUMN \"Rastreador\" TYPE character varying(120) USING \"Rastreador\"::character varying(120);");
        }
    }
}
