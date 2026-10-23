using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustosDoConsultor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CrmCobrancasPagamento_UsuarioId_Item_Competencia",
                table: "CrmCobrancasPagamento");

            migrationBuilder.DropColumn(
                name: "NotebookParcelas",
                table: "CrmFinanceiroPerfis");

            migrationBuilder.DropColumn(
                name: "NotebookPrimeiraParcela",
                table: "CrmFinanceiroPerfis");

            migrationBuilder.AddColumn<Guid>(
                name: "CustoId",
                table: "CrmCobrancasPagamento",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CrmCustosConsultor",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<int>(type: "integer", nullable: false),
                    Descricao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ValorTotal = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    Parcelas = table.Column<int>(type: "integer", nullable: false),
                    PrimeiraParcela = table.Column<DateOnly>(type: "date", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AtualizadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmCustosConsultor", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrmCustosConsultor_AspNetUsers_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrmCobrancasPagamento_CustoId_Competencia",
                table: "CrmCobrancasPagamento",
                columns: new[] { "CustoId", "Competencia" },
                unique: true,
                filter: "\"CustoId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CrmCobrancasPagamento_UsuarioId_Item_Competencia",
                table: "CrmCobrancasPagamento",
                columns: new[] { "UsuarioId", "Item", "Competencia" },
                unique: true,
                filter: "\"CustoId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CrmCustosConsultor_UsuarioId",
                table: "CrmCustosConsultor",
                column: "UsuarioId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrmCustosConsultor");

            migrationBuilder.DropIndex(
                name: "IX_CrmCobrancasPagamento_CustoId_Competencia",
                table: "CrmCobrancasPagamento");

            migrationBuilder.DropIndex(
                name: "IX_CrmCobrancasPagamento_UsuarioId_Item_Competencia",
                table: "CrmCobrancasPagamento");

            migrationBuilder.DropColumn(
                name: "CustoId",
                table: "CrmCobrancasPagamento");

            migrationBuilder.AddColumn<int>(
                name: "NotebookParcelas",
                table: "CrmFinanceiroPerfis",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "NotebookPrimeiraParcela",
                table: "CrmFinanceiroPerfis",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CrmCobrancasPagamento_UsuarioId_Item_Competencia",
                table: "CrmCobrancasPagamento",
                columns: new[] { "UsuarioId", "Item", "Competencia" },
                unique: true);
        }
    }
}
