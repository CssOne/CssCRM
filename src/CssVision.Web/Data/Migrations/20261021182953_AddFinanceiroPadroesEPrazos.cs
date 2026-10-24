using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFinanceiroPadroesEPrazos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CrmFinanceiroPadroes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Parametro = table.Column<int>(type: "integer", nullable: false),
                    Valor = table.Column<decimal>(type: "numeric(14,4)", nullable: true),
                    CarenciaMeses = table.Column<int>(type: "integer", nullable: true),
                    DefinidoPorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AtualizadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmFinanceiroPadroes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CrmFinanceiroPrazos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Parametro = table.Column<int>(type: "integer", nullable: false),
                    CarenciaMeses = table.Column<int>(type: "integer", nullable: true),
                    AlertaConfirmadoPara = table.Column<DateOnly>(type: "date", nullable: true),
                    DefinidoPorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AtualizadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmFinanceiroPrazos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrmFinanceiroPrazos_AspNetUsers_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrmFinanceiroPadroes_Parametro",
                table: "CrmFinanceiroPadroes",
                column: "Parametro",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CrmFinanceiroPrazos_UsuarioId_Parametro",
                table: "CrmFinanceiroPrazos",
                columns: new[] { "UsuarioId", "Parametro" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrmFinanceiroPadroes");

            migrationBuilder.DropTable(
                name: "CrmFinanceiroPrazos");
        }
    }
}
