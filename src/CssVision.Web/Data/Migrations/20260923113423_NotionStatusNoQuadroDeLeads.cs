using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class NotionStatusNoQuadroDeLeads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RealinhamentoConcluidoEm",
                table: "CrmNotionSyncCheckpoints",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NotionStatus",
                table: "CrmLeads",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RealinhamentoConcluidoEm",
                table: "CrmNotionSyncCheckpoints");

            migrationBuilder.DropColumn(
                name: "NotionStatus",
                table: "CrmLeads");
        }
    }
}
