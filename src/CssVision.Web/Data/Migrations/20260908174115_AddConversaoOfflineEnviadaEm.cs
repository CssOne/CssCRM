using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConversaoOfflineEnviadaEm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ConversaoOfflineEnviadaEm",
                table: "CrmOpportunities",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConversaoOfflineEnviadaEm",
                table: "CrmOpportunities");
        }
    }
}
