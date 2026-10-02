using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    /// <summary>Caroline Aguiar liberada para receber leads AGV TRUCK (pedido de 02/10/2026).</summary>
    public partial class CarolineRecebeAgvTruck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Caroline Aguiar (consórcio) passa a receber leads de AGV TRUCK no rodízio, como a Samys:
            // "Recebe leads" ligado, só AGV TRUCK, e o papel Comercial (o rodízio só distribui para
            // quem tem esse papel) — mantém o Admin que ela já tem.
            migrationBuilder.Sql("""
                UPDATE "AspNetUsers" SET "RecebeLeads" = true, "RecebeSomenteOQue" = 'AGV TRUCK'
                WHERE "NormalizedEmail" = 'CONSORCIOCAROLINEAGUIAR@GMAIL.COM';

                INSERT INTO "AspNetUserRoles" ("UserId", "RoleId")
                SELECT u."Id", r."Id" FROM "AspNetUsers" u CROSS JOIN "AspNetRoles" r
                WHERE u."NormalizedEmail" = 'CONSORCIOCAROLINEAGUIAR@GMAIL.COM' AND r."NormalizedName" = 'COMERCIAL'
                  AND NOT EXISTS (SELECT 1 FROM "AspNetUserRoles" x WHERE x."UserId" = u."Id" AND x."RoleId" = r."Id");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
