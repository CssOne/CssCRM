using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCrmGrupo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "GrupoId",
                table: "AspNetUsers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CrmGrupos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RegionalId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AtualizadoPorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmGrupos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrmGrupos_CrmRegionais_RegionalId",
                        column: x => x.RegionalId,
                        principalTable: "CrmRegionais",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_GrupoId",
                table: "AspNetUsers",
                column: "GrupoId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmGrupos_Ativo",
                table: "CrmGrupos",
                column: "Ativo");

            migrationBuilder.CreateIndex(
                name: "IX_CrmGrupos_RegionalId_Nome",
                table: "CrmGrupos",
                columns: new[] { "RegionalId", "Nome" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_CrmGrupos_GrupoId",
                table: "AspNetUsers",
                column: "GrupoId",
                principalTable: "CrmGrupos",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_CrmGrupos_GrupoId",
                table: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "CrmGrupos");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_GrupoId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "GrupoId",
                table: "AspNetUsers");
        }
    }
}
