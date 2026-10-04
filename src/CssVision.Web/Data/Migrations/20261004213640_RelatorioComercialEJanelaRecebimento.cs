using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class RelatorioComercialEJanelaRecebimento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DiasSemanaLeads",
                table: "AspNetUsers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "HorarioFimLeads",
                table: "AspNetUsers",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "HorarioInicioLeads",
                table: "AspNetUsers",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CrmControleMarketingMeses",
                columns: table => new
                {
                    Mes = table.Column<DateOnly>(type: "date", nullable: false),
                    VendasQuantidade = table.Column<int>(type: "integer", nullable: false),
                    LeadsGerados = table.Column<int>(type: "integer", nullable: false),
                    FacebookAds = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    GoogleAds = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    FerramentasMarketing = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    Backlinks = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    FaturamentoTotal = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    MetaFaturamento = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmControleMarketingMeses", x => x.Mes);
                });

            // Carga única do controle mensal de marketing do Notion ("CONTROLE CSS BRASIL", jan–mai/2025,
            // as linhas mensais do relatório; o resto da base é semanal e parou de ser preenchido em jun/2025).
            migrationBuilder.Sql("""
                INSERT INTO "CrmControleMarketingMeses"
                    ("Mes", "VendasQuantidade", "LeadsGerados", "FacebookAds", "GoogleAds", "FerramentasMarketing", "Backlinks", "FaturamentoTotal", "MetaFaturamento")
                VALUES
                    ('2025-01-01', 164, 1982, 0, 16052.59, 545, 0, 44993.89, 150000),
                    ('2025-02-01', 150, 1484, 0, 15134.42, 545, 1000, 40839.15, 100000),
                    ('2025-03-01', 168, 1602, 0, 14950.52, 665, 0, 50022.66, 75000),
                    ('2025-04-01', 163, 1706, 0, 13821.93, 700, 1000, 44985.83, 75000),
                    ('2025-05-01', 160, 1448, 1533.12, 17742.38, 800, 0, 50575.88, 75000);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrmControleMarketingMeses");

            migrationBuilder.DropColumn(
                name: "DiasSemanaLeads",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "HorarioFimLeads",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "HorarioInicioLeads",
                table: "AspNetUsers");
        }
    }
}
