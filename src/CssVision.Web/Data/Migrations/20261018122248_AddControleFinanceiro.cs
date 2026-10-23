using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddControleFinanceiro : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CrmFinanceiroConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ano = table.Column<int>(type: "integer", nullable: false),
                    PercentualReserva = table.Column<decimal>(type: "numeric(5,4)", nullable: false),
                    CaixaInicial = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AtualizadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmFinanceiroConfigs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrmFinanceiroConfigs_AspNetUsers_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CrmFinanceiroCustos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Data = table.Column<DateOnly>(type: "date", nullable: false),
                    Categoria = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Descricao = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Valor = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    Vencimento = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DataPagamento = table.Column<DateOnly>(type: "date", nullable: true),
                    ComprovanteSalvo = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AtualizadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmFinanceiroCustos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrmFinanceiroCustos_AspNetUsers_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CrmFinanceiroMetas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ano = table.Column<int>(type: "integer", nullable: false),
                    Mes = table.Column<int>(type: "integer", nullable: false),
                    MetaAdesao = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    MetaBonus = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    MetaRecorrencia = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    MetaVendas = table.Column<int>(type: "integer", nullable: false),
                    Observacoes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AtualizadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmFinanceiroMetas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrmFinanceiroMetas_AspNetUsers_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CrmFinanceiroVendas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    DataVenda = table.Column<DateOnly>(type: "date", nullable: false),
                    Cliente = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Produto = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ValorAdesao = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    ValorBonus = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    Premiacoes = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    DataPrevistaAdesao = table.Column<DateOnly>(type: "date", nullable: true),
                    StatusAdesao = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DataPrevistaBonus = table.Column<DateOnly>(type: "date", nullable: true),
                    StatusBonus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Observacoes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AtualizadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmFinanceiroVendas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrmFinanceiroVendas_AspNetUsers_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrmFinanceiroConfigs_UsuarioId_Ano",
                table: "CrmFinanceiroConfigs",
                columns: new[] { "UsuarioId", "Ano" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CrmFinanceiroCustos_UsuarioId_Data",
                table: "CrmFinanceiroCustos",
                columns: new[] { "UsuarioId", "Data" });

            migrationBuilder.CreateIndex(
                name: "IX_CrmFinanceiroMetas_UsuarioId_Ano_Mes",
                table: "CrmFinanceiroMetas",
                columns: new[] { "UsuarioId", "Ano", "Mes" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CrmFinanceiroVendas_UsuarioId_DataVenda",
                table: "CrmFinanceiroVendas",
                columns: new[] { "UsuarioId", "DataVenda" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrmFinanceiroConfigs");

            migrationBuilder.DropTable(
                name: "CrmFinanceiroCustos");

            migrationBuilder.DropTable(
                name: "CrmFinanceiroMetas");

            migrationBuilder.DropTable(
                name: "CrmFinanceiroVendas");
        }
    }
}
