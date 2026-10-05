using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReaplicaUnificacaoDeContas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // O ConsultorSeeder recriava na inicialização as contas duplicadas já unificadas (carolbarcelosagv,
            // isabelcrg19, lauradiniz09az), ativas e com a senha inicial. O seeder agora respeita os aliases;
            // esta migration repete a unificação (idempotente) para remover as que voltaram, movendo qualquer
            // referência que tenham ganhado para a conta que prevalece.
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    par record;
                    ref record;
                    dup_id uuid;
                    keep_id uuid;
                BEGIN
                    FOR par IN SELECT * FROM (VALUES
                        ('carolbarcelosagv@gmail.com',          'carolinedebarcelos@icloud.com'),
                        ('carolineaguiarconsorcios@gmail.com',  'consorciocarolineaguiar@gmail.com'),
                        ('regionalcssbrasil1@gmail.com',        'consorciocarolineaguiar@gmail.com'),
                        ('isabelcrg19@gmail.com',               'isabeloliveiraagv@gmail.com'),
                        ('lauradiniz09az@gmail.com',            'vendas.apvs02@gmail.com'),
                        ('lucasbenevenuto974@gmail.com',        'benevenuto.uagv@outlook.com'),
                        ('gscvendas07@gmail.com',               'gsccvendas07@gmail.com')
                    ) AS t(dup_email, keep_email)
                    LOOP
                        SELECT "Id" INTO dup_id FROM "AspNetUsers" WHERE "NormalizedEmail" = upper(par.dup_email);
                        SELECT "Id" INTO keep_id FROM "AspNetUsers" WHERE "NormalizedEmail" = upper(par.keep_email);
                        IF dup_id IS NULL OR keep_id IS NULL OR dup_id = keep_id THEN
                            CONTINUE;
                        END IF;

                        INSERT INTO "CrmUsuarioAliases" ("EmailNormalizado", "UsuarioId")
                        VALUES (upper(par.dup_email), keep_id) ON CONFLICT DO NOTHING;

                        -- Metas do mesmo mês nas duas contas somam; as demais passam para a conta que fica.
                        UPDATE "CrmSalesGoals" k SET
                            "MetaQuantidadeVendas" = k."MetaQuantidadeVendas" + d."MetaQuantidadeVendas",
                            "MetaValor" = CASE WHEN k."MetaValor" IS NULL AND d."MetaValor" IS NULL THEN NULL
                                               ELSE COALESCE(k."MetaValor", 0) + COALESCE(d."MetaValor", 0) END
                        FROM "CrmSalesGoals" d
                        WHERE d."VendedorId" = dup_id AND k."VendedorId" = keep_id AND k."MesReferencia" = d."MesReferencia";
                        DELETE FROM "CrmSalesGoals" d
                        WHERE d."VendedorId" = dup_id
                          AND EXISTS (SELECT 1 FROM "CrmSalesGoals" k WHERE k."VendedorId" = keep_id AND k."MesReferencia" = d."MesReferencia");
                        UPDATE "CrmSalesGoals" SET "VendedorId" = keep_id WHERE "VendedorId" = dup_id;

                        -- Toda coluna que aponta para um usuário (responsável, autor, criado/atualizado/arquivado por...).
                        FOR ref IN
                            SELECT table_name, column_name FROM information_schema.columns
                            WHERE table_schema = 'public' AND data_type = 'uuid'
                              AND column_name IN ('ResponsavelId', 'CriadoPorId', 'AtualizadoPorId', 'ArquivadoPorId', 'EnviadoPorId',
                                                  'UsuarioId', 'AlteradoPorId', 'ResponsavelAnteriorId', 'ResponsavelNovoId',
                                                  'AutorId', 'VistoriadorId', 'GestorComercialId')
                              AND table_name NOT LIKE 'AspNetUser%' AND table_name <> 'CrmUsuarioAliases'
                        LOOP
                            EXECUTE format('UPDATE %I SET %I = $1 WHERE %I = $2', ref.table_name, ref.column_name, ref.column_name)
                                USING keep_id, dup_id;
                        END LOOP;

                        -- Papéis da conta duplicada que a outra ainda não tem.
                        INSERT INTO "AspNetUserRoles" ("UserId", "RoleId")
                        SELECT keep_id, "RoleId" FROM "AspNetUserRoles" WHERE "UserId" = dup_id
                        ON CONFLICT DO NOTHING;

                        -- Configurações: a conta que fica manda; o que ela não tem vem da duplicada.
                        UPDATE "AspNetUsers" k SET
                            "Ativo" = k."Ativo" OR d."Ativo",
                            "LimiteMensalLeads" = COALESCE(k."LimiteMensalLeads", d."LimiteMensalLeads"),
                            "LimiteDiarioLeads" = COALESCE(k."LimiteDiarioLeads", d."LimiteDiarioLeads"),
                            "RecebeSomenteOQue" = COALESCE(k."RecebeSomenteOQue", d."RecebeSomenteOQue"),
                            "RegionalId" = COALESCE(k."RegionalId", d."RegionalId"),
                            "GrupoId" = COALESCE(k."GrupoId", d."GrupoId"),
                            "FotoUrl" = COALESCE(k."FotoUrl", d."FotoUrl"),
                            "PhoneNumber" = COALESCE(k."PhoneNumber", d."PhoneNumber"),
                            "NotionUserId" = COALESCE(k."NotionUserId", d."NotionUserId")
                        FROM "AspNetUsers" d
                        WHERE k."Id" = keep_id AND d."Id" = dup_id;

                        DELETE FROM "AspNetUsers" WHERE "Id" = dup_id;
                    END LOOP;
                END $$;
                """);


        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
