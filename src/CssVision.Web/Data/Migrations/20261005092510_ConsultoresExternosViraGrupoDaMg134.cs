using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConsultoresExternosViraGrupoDaMg134 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A regional "MG134 Consultores Externos" passa a ser o grupo "Consultores Externos" da regional MG134 (pedido
            // de 05/10/2026) — mesmo molde da Growth Sales na MG132. Usuários (no grupo), grupos, leads e metas vão para a
            // MG134 e a regional é excluída. Sem uma das duas regionais, não faz nada.
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    externos_id uuid;
                    externos_nome text;
                    mg134_id uuid;
                    grupo_id uuid;
                BEGIN
                    SELECT "Id" INTO mg134_id FROM "CrmRegionais" WHERE "Nome" = 'MG134' LIMIT 1;
                    SELECT "Id", "Nome" INTO externos_id, externos_nome FROM "CrmRegionais"
                        WHERE "Nome" = 'MG134 Consultores Externos' LIMIT 1;
                    IF mg134_id IS NULL OR externos_id IS NULL THEN
                        RETURN;
                    END IF;

                    SELECT "Id" INTO grupo_id FROM "CrmGrupos" WHERE "RegionalId" = mg134_id AND "Nome" = 'Consultores Externos' LIMIT 1;
                    IF grupo_id IS NULL THEN
                        grupo_id := gen_random_uuid();
                        INSERT INTO "CrmGrupos" ("Id", "RegionalId", "Nome", "Ativo", "CriadoEm")
                        VALUES (grupo_id, mg134_id, 'Consultores Externos', TRUE, now());
                    END IF;

                    -- Grupos que a regional já tinha continuam, agora dentro da MG134.
                    UPDATE "CrmGrupos" g SET "RegionalId" = mg134_id,
                        "Nome" = CASE WHEN EXISTS (SELECT 1 FROM "CrmGrupos" o WHERE o."RegionalId" = mg134_id AND o."Nome" = g."Nome")
                                      THEN g."Nome" || ' (Externos)' ELSE g."Nome" END
                    WHERE g."RegionalId" = externos_id;

                    -- Usuários: regional MG134; quem não estava em grupo vai para o grupo Consultores Externos.
                    UPDATE "AspNetUsers" SET "RegionalId" = mg134_id, "GrupoId" = COALESCE("GrupoId", grupo_id)
                    WHERE "RegionalId" = externos_id;

                    -- Leads guardam o nome da regional como texto.
                    UPDATE "CrmLeads" SET "Regional" = 'MG134' WHERE "Regional" = externos_nome;

                    -- Metas por regional: soma na da MG134 do mesmo mês (ou passa para ela, se não houver).
                    UPDATE "CrmRegionalGoals" m SET
                        "MetaQuantidadeVendas" = m."MetaQuantidadeVendas" + g."MetaQuantidadeVendas",
                        "MetaValor" = CASE WHEN m."MetaValor" IS NULL AND g."MetaValor" IS NULL THEN NULL
                                           ELSE COALESCE(m."MetaValor", 0) + COALESCE(g."MetaValor", 0) END
                    FROM "CrmRegionalGoals" g
                    WHERE g."RegionalId" = externos_id AND m."RegionalId" = mg134_id AND m."MesReferencia" = g."MesReferencia";
                    DELETE FROM "CrmRegionalGoals" g
                    WHERE g."RegionalId" = externos_id
                      AND EXISTS (SELECT 1 FROM "CrmRegionalGoals" m WHERE m."RegionalId" = mg134_id AND m."MesReferencia" = g."MesReferencia");
                    UPDATE "CrmRegionalGoals" SET "RegionalId" = mg134_id WHERE "RegionalId" = externos_id;

                    DELETE FROM "CrmRegionais" WHERE "Id" = externos_id;
                END $$;
                """);


        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
