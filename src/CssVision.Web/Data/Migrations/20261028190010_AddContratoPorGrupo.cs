using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddContratoPorGrupo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CrmGrupoContratoPerfis",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GrupoId = table.Column<Guid>(type: "uuid", nullable: false),
                    VigenteDesde = table.Column<DateOnly>(type: "date", nullable: false),
                    Tipo = table.Column<int>(type: "integer", nullable: true),
                    ModeloPj = table.Column<int>(type: "integer", nullable: true),
                    Transporte = table.Column<int>(type: "integer", nullable: false),
                    DefinidoPorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AtualizadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmGrupoContratoPerfis", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrmGrupoContratoPerfis_CrmGrupos_GrupoId",
                        column: x => x.GrupoId,
                        principalTable: "CrmGrupos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CrmGrupoContratoPrazos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GrupoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Parametro = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_CrmGrupoContratoPrazos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrmGrupoContratoPrazos_CrmGrupos_GrupoId",
                        column: x => x.GrupoId,
                        principalTable: "CrmGrupos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CrmGrupoContratoValores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GrupoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Parametro = table.Column<int>(type: "integer", nullable: false),
                    VigenteDesde = table.Column<DateOnly>(type: "date", nullable: false),
                    Valor = table.Column<decimal>(type: "numeric(14,4)", nullable: false),
                    Observacao = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    DefinidoPorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AtualizadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmGrupoContratoValores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrmGrupoContratoValores_CrmGrupos_GrupoId",
                        column: x => x.GrupoId,
                        principalTable: "CrmGrupos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CrmGrupoMembros",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    GrupoId = table.Column<Guid>(type: "uuid", nullable: true),
                    DesdeMes = table.Column<DateOnly>(type: "date", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AtualizadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmGrupoMembros", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrmGrupoMembros_AspNetUsers_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CrmGrupoMembros_CrmGrupos_GrupoId",
                        column: x => x.GrupoId,
                        principalTable: "CrmGrupos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Quem já está em um grupo entra no histórico: o grupo vale desde o início do contrato dele (ou, sem contrato, desde o mês em que a conta foi criada).
            migrationBuilder.Sql(@"
                INSERT INTO ""CrmGrupoMembros"" (""Id"", ""CriadoEm"", ""UsuarioId"", ""GrupoId"", ""DesdeMes"")
                SELECT gen_random_uuid(), now(), u.""Id"", u.""GrupoId"",
                       COALESCE(p.""InicioContrato"", date_trunc('month', u.""CriadoEm"")::date)
                FROM ""AspNetUsers"" u
                LEFT JOIN ""CrmFinanceiroPerfis"" p ON p.""UsuarioId"" = u.""Id""
                WHERE u.""GrupoId"" IS NOT NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_CrmGrupoContratoPerfis_GrupoId_VigenteDesde",
                table: "CrmGrupoContratoPerfis",
                columns: new[] { "GrupoId", "VigenteDesde" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CrmGrupoContratoPrazos_GrupoId_Parametro",
                table: "CrmGrupoContratoPrazos",
                columns: new[] { "GrupoId", "Parametro" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CrmGrupoContratoValores_GrupoId_Parametro_VigenteDesde",
                table: "CrmGrupoContratoValores",
                columns: new[] { "GrupoId", "Parametro", "VigenteDesde" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CrmGrupoMembros_GrupoId",
                table: "CrmGrupoMembros",
                column: "GrupoId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmGrupoMembros_UsuarioId_DesdeMes",
                table: "CrmGrupoMembros",
                columns: new[] { "UsuarioId", "DesdeMes" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrmGrupoContratoPerfis");

            migrationBuilder.DropTable(
                name: "CrmGrupoContratoPrazos");

            migrationBuilder.DropTable(
                name: "CrmGrupoContratoValores");

            migrationBuilder.DropTable(
                name: "CrmGrupoMembros");
        }
    }
}
