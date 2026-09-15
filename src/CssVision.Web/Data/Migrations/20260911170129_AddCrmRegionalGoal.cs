using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCrmRegionalGoal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CrmRegionalGoals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RegionalId = table.Column<Guid>(type: "uuid", nullable: false),
                    MesReferencia = table.Column<DateOnly>(type: "date", nullable: false),
                    MetaQuantidadeVendas = table.Column<int>(type: "integer", nullable: false),
                    MetaValor = table.Column<decimal>(type: "numeric(14,2)", nullable: true),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AtualizadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmRegionalGoals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrmRegionalGoals_CrmRegionais_RegionalId",
                        column: x => x.RegionalId,
                        principalTable: "CrmRegionais",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrmRegionalGoals_RegionalId_MesReferencia",
                table: "CrmRegionalGoals",
                columns: new[] { "RegionalId", "MesReferencia" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrmRegionalGoals");
        }
    }
}
