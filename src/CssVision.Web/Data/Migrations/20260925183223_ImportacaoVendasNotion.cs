using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class ImportacaoVendasNotion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NotionPageId",
                table: "CrmOpportunities",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ImportacaoVendasConcluidaEm",
                table: "CrmNotionSyncCheckpoints",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CrmOpportunities_NotionPageId",
                table: "CrmOpportunities",
                column: "NotionPageId",
                unique: true,
                filter: "\"NotionPageId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CrmOpportunities_NotionPageId",
                table: "CrmOpportunities");

            migrationBuilder.DropColumn(
                name: "NotionPageId",
                table: "CrmOpportunities");

            migrationBuilder.DropColumn(
                name: "ImportacaoVendasConcluidaEm",
                table: "CrmNotionSyncCheckpoints");
        }
    }
}
