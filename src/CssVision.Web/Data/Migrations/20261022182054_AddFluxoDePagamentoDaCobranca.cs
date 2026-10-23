using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFluxoDePagamentoDaCobranca : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ComprovanteUrl",
                table: "CrmCobrancasPagamento",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DataInformada",
                table: "CrmCobrancasPagamento",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "InformadoEm",
                table: "CrmCobrancasPagamento",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "LembreteEm",
                table: "CrmCobrancasPagamento",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LembreteEnviadoEm",
                table: "CrmCobrancasPagamento",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotivoRecusa",
                table: "CrmCobrancasPagamento",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ObservacaoConsultor",
                table: "CrmCobrancasPagamento",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RecusadoEm",
                table: "CrmCobrancasPagamento",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CrmCobrancasPagamento_LembreteEm",
                table: "CrmCobrancasPagamento",
                column: "LembreteEm",
                filter: "\"LembreteEm\" IS NOT NULL AND \"LembreteEnviadoEm\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CrmCobrancasPagamento_LembreteEm",
                table: "CrmCobrancasPagamento");

            migrationBuilder.DropColumn(
                name: "ComprovanteUrl",
                table: "CrmCobrancasPagamento");

            migrationBuilder.DropColumn(
                name: "DataInformada",
                table: "CrmCobrancasPagamento");

            migrationBuilder.DropColumn(
                name: "InformadoEm",
                table: "CrmCobrancasPagamento");

            migrationBuilder.DropColumn(
                name: "LembreteEm",
                table: "CrmCobrancasPagamento");

            migrationBuilder.DropColumn(
                name: "LembreteEnviadoEm",
                table: "CrmCobrancasPagamento");

            migrationBuilder.DropColumn(
                name: "MotivoRecusa",
                table: "CrmCobrancasPagamento");

            migrationBuilder.DropColumn(
                name: "ObservacaoConsultor",
                table: "CrmCobrancasPagamento");

            migrationBuilder.DropColumn(
                name: "RecusadoEm",
                table: "CrmCobrancasPagamento");
        }
    }
}
