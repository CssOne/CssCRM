using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    /// <summary>A regional "CSS Growth Sales" passa a ser um grupo da regional MG132 (pedido de 04/10/2026).</summary>
    public partial class GrowthSalesViraGrupoDaMg132 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Sem a regional Growth ou sem a MG132, não faz nada. Usuários, leads, grupos e metas da
            // regional Growth vão para a MG132 (os usuários, dentro do grupo "CSS Growth Sales"), e a
            // regional Growth é excluída — a sincronização com o Notion já passa a usar MG132 + grupo.
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    growth_id uuid;
                    growth_nome text;
                    mg132_id uuid;
                    grupo_id uuid;
                BEGIN
                    SELECT "Id" INTO mg132_id FROM "CrmRegionais" WHERE "Nome" = 'MG132' LIMIT 1;
                    SELECT "Id", "Nome" INTO growth_id, growth_nome FROM "CrmRegionais"
                        WHERE LOWER("Nome") LIKE '%growth%' AND "Id" <> COALESCE(mg132_id, '00000000-0000-0000-0000-000000000000')
                        ORDER BY "CriadoEm" LIMIT 1;
                    IF mg132_id IS NULL OR growth_id IS NULL THEN
                        RETURN;
                    END IF;

                    SELECT "Id" INTO grupo_id FROM "CrmGrupos" WHERE "RegionalId" = mg132_id AND "Nome" = 'CSS Growth Sales' LIMIT 1;
                    IF grupo_id IS NULL THEN
                        grupo_id := gen_random_uuid();
                        INSERT INTO "CrmGrupos" ("Id", "RegionalId", "Nome", "Ativo", "CriadoEm")
                        VALUES (grupo_id, mg132_id, 'CSS Growth Sales', TRUE, now());
                    END IF;

                    -- Grupos que a regional Growth já tinha continuam existindo, agora dentro da MG132
                    -- (nome repetido na MG132 ganha o sufixo " (Growth)").
                    UPDATE "CrmGrupos" g SET "RegionalId" = mg132_id,
                        "Nome" = CASE WHEN EXISTS (SELECT 1 FROM "CrmGrupos" o WHERE o."RegionalId" = mg132_id AND o."Nome" = g."Nome")
                                      THEN g."Nome" || ' (Growth)' ELSE g."Nome" END
                    WHERE g."RegionalId" = growth_id;

                    -- Usuários: regional MG132; quem não estava em grupo vai para o grupo Growth.
                    UPDATE "AspNetUsers" SET "RegionalId" = mg132_id, "GrupoId" = COALESCE("GrupoId", grupo_id)
                    WHERE "RegionalId" = growth_id;

                    -- Leads guardam o nome da regional como texto.
                    UPDATE "CrmLeads" SET "Regional" = 'MG132' WHERE "Regional" = growth_nome;

                    -- Metas por regional: soma na meta da MG132 do mesmo mês (ou passa para ela, se não houver).
                    UPDATE "CrmRegionalGoals" m SET
                        "MetaQuantidadeVendas" = m."MetaQuantidadeVendas" + g."MetaQuantidadeVendas",
                        "MetaValor" = CASE WHEN m."MetaValor" IS NULL AND g."MetaValor" IS NULL THEN NULL
                                           ELSE COALESCE(m."MetaValor", 0) + COALESCE(g."MetaValor", 0) END
                    FROM "CrmRegionalGoals" g
                    WHERE g."RegionalId" = growth_id AND m."RegionalId" = mg132_id AND m."MesReferencia" = g."MesReferencia";
                    DELETE FROM "CrmRegionalGoals" g
                    WHERE g."RegionalId" = growth_id
                      AND EXISTS (SELECT 1 FROM "CrmRegionalGoals" m WHERE m."RegionalId" = mg132_id AND m."MesReferencia" = g."MesReferencia");
                    UPDATE "CrmRegionalGoals" SET "RegionalId" = mg132_id WHERE "RegionalId" = growth_id;

                    DELETE FROM "CrmRegionais" WHERE "Id" = growth_id;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
