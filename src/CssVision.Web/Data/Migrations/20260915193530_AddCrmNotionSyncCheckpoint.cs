using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCrmNotionSyncCheckpoint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CrmNotionSyncCheckpoints",
                columns: table => new
                {
                    DataSourceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RegionalNome = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    UltimaSincronizacaoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmNotionSyncCheckpoints", x => x.DataSourceId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrmNotionSyncCheckpoints");
        }
    }
}
