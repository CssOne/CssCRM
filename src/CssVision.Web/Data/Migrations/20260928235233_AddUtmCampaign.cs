using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUtmCampaign : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UtmCampaign",
                table: "CrmLeads",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UtmCampaign",
                table: "CrmLeads");
        }
    }
}
