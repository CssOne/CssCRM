using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCrm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NomeCompleto = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    GestorComercialId = table.Column<Guid>(type: "uuid", nullable: true),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUsers_AspNetUsers_GestorComercialId",
                        column: x => x.GestorComercialId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CrmLossReasons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Descricao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AtualizadoPorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmLossReasons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CrmPipelineStages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Ordem = table.Column<int>(type: "integer", nullable: false),
                    Tipo = table.Column<int>(type: "integer", nullable: false),
                    Ativa = table.Column<bool>(type: "boolean", nullable: false),
                    Cor = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: true),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AtualizadoPorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmPipelineStages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CrmTags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Cor = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: true),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AtualizadoPorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmTags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CrmAuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Acao = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    EntidadeTipo = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    EntidadeId = table.Column<Guid>(type: "uuid", nullable: true),
                    DetalhesJson = table.Column<string>(type: "text", nullable: true),
                    OcorridoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmAuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrmAuditLogs_AspNetUsers_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CrmLeads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NomeOuRazaoSocial = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TipoPessoa = table.Column<int>(type: "integer", nullable: false),
                    DocumentoNormalizado = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    TelefoneNormalizado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Telefone = table.Column<string>(type: "text", nullable: true),
                    WhatsApp = table.Column<string>(type: "text", nullable: true),
                    EmailNormalizado = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    DataNascimento = table.Column<DateOnly>(type: "date", nullable: true),
                    Cidade = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Estado = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    Regional = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Origem = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Campanha = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ProdutoInteresse = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ResponsavelId = table.Column<Guid>(type: "uuid", nullable: true),
                    Observacoes = table.Column<string>(type: "text", nullable: true),
                    ConsentimentoContato = table.Column<bool>(type: "boolean", nullable: false),
                    ConsentimentoDataEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConsentimentoOrigem = table.Column<string>(type: "text", nullable: true),
                    UltimoContatoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProximoContatoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AtualizadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Arquivado = table.Column<bool>(type: "boolean", nullable: false),
                    ArquivadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ArquivadoPorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmLeads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrmLeads_AspNetUsers_ResponsavelId",
                        column: x => x.ResponsavelId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CrmSalesGoals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VendedorId = table.Column<Guid>(type: "uuid", nullable: false),
                    MesReferencia = table.Column<DateOnly>(type: "date", nullable: false),
                    MetaValor = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    MetaQuantidadeVendas = table.Column<int>(type: "integer", nullable: true),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AtualizadoPorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmSalesGoals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrmSalesGoals_AspNetUsers_VendedorId",
                        column: x => x.VendedorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CrmLeadAssignmentHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LeadId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResponsavelAnteriorId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResponsavelNovoId = table.Column<Guid>(type: "uuid", nullable: false),
                    AlteradoPorId = table.Column<Guid>(type: "uuid", nullable: false),
                    AlteradoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Motivo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmLeadAssignmentHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrmLeadAssignmentHistories_AspNetUsers_AlteradoPorId",
                        column: x => x.AlteradoPorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CrmLeadAssignmentHistories_AspNetUsers_ResponsavelAnteriorId",
                        column: x => x.ResponsavelAnteriorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CrmLeadAssignmentHistories_AspNetUsers_ResponsavelNovoId",
                        column: x => x.ResponsavelNovoId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CrmLeadAssignmentHistories_CrmLeads_LeadId",
                        column: x => x.LeadId,
                        principalTable: "CrmLeads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CrmLeadTags",
                columns: table => new
                {
                    LeadId = table.Column<Guid>(type: "uuid", nullable: false),
                    TagId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmLeadTags", x => new { x.LeadId, x.TagId });
                    table.ForeignKey(
                        name: "FK_CrmLeadTags_CrmLeads_LeadId",
                        column: x => x.LeadId,
                        principalTable: "CrmLeads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CrmLeadTags_CrmTags_TagId",
                        column: x => x.TagId,
                        principalTable: "CrmTags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CrmNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LeadId = table.Column<Guid>(type: "uuid", nullable: false),
                    AutorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Texto = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AtualizadoPorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrmNotes_AspNetUsers_AutorId",
                        column: x => x.AutorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CrmNotes_CrmLeads_LeadId",
                        column: x => x.LeadId,
                        principalTable: "CrmLeads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CrmOpportunities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LeadId = table.Column<Guid>(type: "uuid", nullable: false),
                    Titulo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ResponsavelId = table.Column<Guid>(type: "uuid", nullable: false),
                    EtapaId = table.Column<Guid>(type: "uuid", nullable: false),
                    EtapaDesde = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProdutoOuServico = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ValorEstimado = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    ProbabilidadeFechamento = table.Column<int>(type: "integer", nullable: true),
                    DataPrevistaFechamento = table.Column<DateOnly>(type: "date", nullable: true),
                    ValorFinal = table.Column<decimal>(type: "numeric(14,2)", nullable: true),
                    DataEfetivaFechamento = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MotivoPerdaId = table.Column<Guid>(type: "uuid", nullable: true),
                    Concorrente = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Observacoes = table.Column<string>(type: "text", nullable: true),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AtualizadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Arquivado = table.Column<bool>(type: "boolean", nullable: false),
                    ArquivadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ArquivadoPorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmOpportunities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrmOpportunities_AspNetUsers_ResponsavelId",
                        column: x => x.ResponsavelId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CrmOpportunities_CrmLeads_LeadId",
                        column: x => x.LeadId,
                        principalTable: "CrmLeads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CrmOpportunities_CrmLossReasons_MotivoPerdaId",
                        column: x => x.MotivoPerdaId,
                        principalTable: "CrmLossReasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CrmOpportunities_CrmPipelineStages_EtapaId",
                        column: x => x.EtapaId,
                        principalTable: "CrmPipelineStages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CrmActivities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LeadId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpportunityId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResponsavelId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<int>(type: "integer", nullable: false),
                    Assunto = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Descricao = table.Column<string>(type: "text", nullable: true),
                    DataHoraPrevista = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DataHoraConclusao = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Resultado = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    LembreteMinutosAntes = table.Column<int>(type: "integer", nullable: true),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AtualizadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Arquivado = table.Column<bool>(type: "boolean", nullable: false),
                    ArquivadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ArquivadoPorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrmActivities_AspNetUsers_ResponsavelId",
                        column: x => x.ResponsavelId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CrmActivities_CrmLeads_LeadId",
                        column: x => x.LeadId,
                        principalTable: "CrmLeads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CrmActivities_CrmOpportunities_OpportunityId",
                        column: x => x.OpportunityId,
                        principalTable: "CrmOpportunities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CrmAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LeadId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpportunityId = table.Column<Guid>(type: "uuid", nullable: true),
                    NomeArquivo = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    CaminhoArmazenamento = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    TamanhoBytes = table.Column<long>(type: "bigint", nullable: false),
                    TipoConteudo = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    EnviadoPorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AtualizadoPorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrmAttachments_AspNetUsers_EnviadoPorId",
                        column: x => x.EnviadoPorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CrmAttachments_CrmLeads_LeadId",
                        column: x => x.LeadId,
                        principalTable: "CrmLeads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CrmAttachments_CrmOpportunities_OpportunityId",
                        column: x => x.OpportunityId,
                        principalTable: "CrmOpportunities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CrmStageHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OpportunityId = table.Column<Guid>(type: "uuid", nullable: false),
                    EtapaAnteriorId = table.Column<Guid>(type: "uuid", nullable: true),
                    EtapaNovaId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    AlteradoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MotivoPerdaDescricao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmStageHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrmStageHistories_AspNetUsers_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CrmStageHistories_CrmOpportunities_OpportunityId",
                        column: x => x.OpportunityId,
                        principalTable: "CrmOpportunities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CrmStageHistories_CrmPipelineStages_EtapaAnteriorId",
                        column: x => x.EtapaAnteriorId,
                        principalTable: "CrmPipelineStages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CrmStageHistories_CrmPipelineStages_EtapaNovaId",
                        column: x => x.EtapaNovaId,
                        principalTable: "CrmPipelineStages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_GestorComercialId",
                table: "AspNetUsers",
                column: "GestorComercialId");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CrmActivities_Arquivado",
                table: "CrmActivities",
                column: "Arquivado");

            migrationBuilder.CreateIndex(
                name: "IX_CrmActivities_DataHoraPrevista",
                table: "CrmActivities",
                column: "DataHoraPrevista");

            migrationBuilder.CreateIndex(
                name: "IX_CrmActivities_LeadId",
                table: "CrmActivities",
                column: "LeadId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmActivities_OpportunityId",
                table: "CrmActivities",
                column: "OpportunityId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmActivities_ResponsavelId",
                table: "CrmActivities",
                column: "ResponsavelId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmActivities_Status",
                table: "CrmActivities",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_CrmActivities_Tipo",
                table: "CrmActivities",
                column: "Tipo");

            migrationBuilder.CreateIndex(
                name: "IX_CrmAttachments_EnviadoPorId",
                table: "CrmAttachments",
                column: "EnviadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmAttachments_LeadId",
                table: "CrmAttachments",
                column: "LeadId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmAttachments_OpportunityId",
                table: "CrmAttachments",
                column: "OpportunityId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmAuditLogs_EntidadeTipo_EntidadeId",
                table: "CrmAuditLogs",
                columns: new[] { "EntidadeTipo", "EntidadeId" });

            migrationBuilder.CreateIndex(
                name: "IX_CrmAuditLogs_OcorridoEm",
                table: "CrmAuditLogs",
                column: "OcorridoEm");

            migrationBuilder.CreateIndex(
                name: "IX_CrmAuditLogs_UsuarioId",
                table: "CrmAuditLogs",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmLeadAssignmentHistories_AlteradoEm",
                table: "CrmLeadAssignmentHistories",
                column: "AlteradoEm");

            migrationBuilder.CreateIndex(
                name: "IX_CrmLeadAssignmentHistories_AlteradoPorId",
                table: "CrmLeadAssignmentHistories",
                column: "AlteradoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmLeadAssignmentHistories_LeadId",
                table: "CrmLeadAssignmentHistories",
                column: "LeadId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmLeadAssignmentHistories_ResponsavelAnteriorId",
                table: "CrmLeadAssignmentHistories",
                column: "ResponsavelAnteriorId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmLeadAssignmentHistories_ResponsavelNovoId",
                table: "CrmLeadAssignmentHistories",
                column: "ResponsavelNovoId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmLeads_Arquivado",
                table: "CrmLeads",
                column: "Arquivado");

            migrationBuilder.CreateIndex(
                name: "IX_CrmLeads_CriadoEm",
                table: "CrmLeads",
                column: "CriadoEm");

            migrationBuilder.CreateIndex(
                name: "IX_CrmLeads_DocumentoNormalizado",
                table: "CrmLeads",
                column: "DocumentoNormalizado",
                unique: true,
                filter: "\"DocumentoNormalizado\" IS NOT NULL AND \"Arquivado\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_CrmLeads_EmailNormalizado",
                table: "CrmLeads",
                column: "EmailNormalizado",
                unique: true,
                filter: "\"EmailNormalizado\" IS NOT NULL AND \"Arquivado\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_CrmLeads_Origem",
                table: "CrmLeads",
                column: "Origem");

            migrationBuilder.CreateIndex(
                name: "IX_CrmLeads_ProximoContatoEm",
                table: "CrmLeads",
                column: "ProximoContatoEm");

            migrationBuilder.CreateIndex(
                name: "IX_CrmLeads_Regional",
                table: "CrmLeads",
                column: "Regional");

            migrationBuilder.CreateIndex(
                name: "IX_CrmLeads_ResponsavelId",
                table: "CrmLeads",
                column: "ResponsavelId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmLeads_Status",
                table: "CrmLeads",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_CrmLeads_TelefoneNormalizado",
                table: "CrmLeads",
                column: "TelefoneNormalizado");

            migrationBuilder.CreateIndex(
                name: "IX_CrmLeads_UltimoContatoEm",
                table: "CrmLeads",
                column: "UltimoContatoEm");

            migrationBuilder.CreateIndex(
                name: "IX_CrmLeadTags_TagId",
                table: "CrmLeadTags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmLossReasons_Descricao",
                table: "CrmLossReasons",
                column: "Descricao",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CrmNotes_AutorId",
                table: "CrmNotes",
                column: "AutorId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmNotes_LeadId",
                table: "CrmNotes",
                column: "LeadId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmOpportunities_Arquivado",
                table: "CrmOpportunities",
                column: "Arquivado");

            migrationBuilder.CreateIndex(
                name: "IX_CrmOpportunities_CriadoEm",
                table: "CrmOpportunities",
                column: "CriadoEm");

            migrationBuilder.CreateIndex(
                name: "IX_CrmOpportunities_DataEfetivaFechamento",
                table: "CrmOpportunities",
                column: "DataEfetivaFechamento");

            migrationBuilder.CreateIndex(
                name: "IX_CrmOpportunities_DataPrevistaFechamento",
                table: "CrmOpportunities",
                column: "DataPrevistaFechamento");

            migrationBuilder.CreateIndex(
                name: "IX_CrmOpportunities_EtapaId",
                table: "CrmOpportunities",
                column: "EtapaId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmOpportunities_LeadId",
                table: "CrmOpportunities",
                column: "LeadId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmOpportunities_MotivoPerdaId",
                table: "CrmOpportunities",
                column: "MotivoPerdaId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmOpportunities_ResponsavelId",
                table: "CrmOpportunities",
                column: "ResponsavelId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmPipelineStages_Nome",
                table: "CrmPipelineStages",
                column: "Nome",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CrmPipelineStages_Ordem",
                table: "CrmPipelineStages",
                column: "Ordem");

            migrationBuilder.CreateIndex(
                name: "IX_CrmSalesGoals_VendedorId_MesReferencia",
                table: "CrmSalesGoals",
                columns: new[] { "VendedorId", "MesReferencia" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CrmStageHistories_AlteradoEm",
                table: "CrmStageHistories",
                column: "AlteradoEm");

            migrationBuilder.CreateIndex(
                name: "IX_CrmStageHistories_EtapaAnteriorId",
                table: "CrmStageHistories",
                column: "EtapaAnteriorId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmStageHistories_EtapaNovaId",
                table: "CrmStageHistories",
                column: "EtapaNovaId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmStageHistories_OpportunityId",
                table: "CrmStageHistories",
                column: "OpportunityId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmStageHistories_UsuarioId",
                table: "CrmStageHistories",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmTags_Nome",
                table: "CrmTags",
                column: "Nome",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "CrmActivities");

            migrationBuilder.DropTable(
                name: "CrmAttachments");

            migrationBuilder.DropTable(
                name: "CrmAuditLogs");

            migrationBuilder.DropTable(
                name: "CrmLeadAssignmentHistories");

            migrationBuilder.DropTable(
                name: "CrmLeadTags");

            migrationBuilder.DropTable(
                name: "CrmNotes");

            migrationBuilder.DropTable(
                name: "CrmSalesGoals");

            migrationBuilder.DropTable(
                name: "CrmStageHistories");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "CrmTags");

            migrationBuilder.DropTable(
                name: "CrmOpportunities");

            migrationBuilder.DropTable(
                name: "CrmLeads");

            migrationBuilder.DropTable(
                name: "CrmLossReasons");

            migrationBuilder.DropTable(
                name: "CrmPipelineStages");

            migrationBuilder.DropTable(
                name: "AspNetUsers");
        }
    }
}
