using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSetorFinanceiro : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.CreateTable(
                name: "CrmCobrancasPagamento",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Item = table.Column<int>(type: "integer", nullable: false),
                    Competencia = table.Column<DateOnly>(type: "date", nullable: false),
                    Pago = table.Column<bool>(type: "boolean", nullable: false),
                    PagoEm = table.Column<DateOnly>(type: "date", nullable: true),
                    Observacao = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    RegistradoPorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AtualizadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmCobrancasPagamento", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrmCobrancasPagamento_AspNetUsers_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrmCobrancasPagamento_UsuarioId_Item_Competencia",
                table: "CrmCobrancasPagamento",
                columns: new[] { "UsuarioId", "Item", "Competencia" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrmCobrancasPagamento");

            migrationBuilder.DropColumn(
                name: "NotebookParcelas",
                table: "CrmFinanceiroPerfis");

            migrationBuilder.DropColumn(
                name: "NotebookPrimeiraParcela",
                table: "CrmFinanceiroPerfis");
        }
    }
}
