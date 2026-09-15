using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCrmLeadMotivoPerda : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MotivoPerdaId",
                table: "CrmLeads",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CrmLeads_MotivoPerdaId",
                table: "CrmLeads",
                column: "MotivoPerdaId");

            migrationBuilder.AddForeignKey(
                name: "FK_CrmLeads_CrmLossReasons_MotivoPerdaId",
                table: "CrmLeads",
                column: "MotivoPerdaId",
                principalTable: "CrmLossReasons",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CrmLeads_CrmLossReasons_MotivoPerdaId",
                table: "CrmLeads");

            migrationBuilder.DropIndex(
                name: "IX_CrmLeads_MotivoPerdaId",
                table: "CrmLeads");

            migrationBuilder.DropColumn(
                name: "MotivoPerdaId",
                table: "CrmLeads");
        }
    }
}
