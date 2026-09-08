using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Domain.Identity;
using CssVision.Web.Services.Crm;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace CssVision.Web.Tests.Infrastructure;

/// <summary>
/// Cria um ApplicationDbContext de testes sobre SQLite in-memory. O modelo de produção é
/// desenhado para PostgreSQL (xmin como concorrência otimista, índices únicos filtrados via SQL
/// bruto) — SQLite aceita a mesma DDL (tipos de coluna são apenas texto para o SQLite, e a
/// sintaxe do filtro usa identificadores entre aspas duplas e TRUE/FALSE, suportados desde o
/// SQLite 3.23). A única limitação conhecida é que a concorrência otimista via xmin não é
/// exercitada de verdade aqui (SQLite nunca gera um novo valor para a coluna), por isso testes
/// de conflito de concorrência devem validar apenas a lógica de serviço, não o mecanismo do BD.
/// </summary>
public sealed class TestDbContextFactory : IDisposable
{
    private readonly SqliteConnection _connection;

    public TestDbContextFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var context = CreateContext();
        context.Database.EnsureCreated();

        // Toda criação de lead exige uma CrmLeadStage (FK obrigatória) — semeia uma padrão aqui
        // pra não obrigar cada teste que só cria leads incidentalmente a se preocupar com isso.
        context.CrmLeadStages.Add(new CrmLeadStage { Nome = "Pré-cadastro", Ordem = 1 });
        context.SaveChanges();
    }

    public ApplicationDbContext CreateContext(ICurrentUserService? currentUser = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        return new ApplicationDbContext(options, currentUser ?? MockCurrentUser(Guid.Empty).Object);
    }

    public static Mock<ICurrentUserService> MockCurrentUser(Guid userId, bool visaoTotal = false, bool gestorComercial = false, bool podeGerir = false)
    {
        var mock = new Mock<ICurrentUserService>();
        mock.SetupGet(m => m.UserId).Returns(userId);
        mock.SetupGet(m => m.IsAuthenticated).Returns(true);
        mock.SetupGet(m => m.TemVisaoTotal).Returns(visaoTotal);
        mock.SetupGet(m => m.IsGestorComercial).Returns(gestorComercial);
        mock.SetupGet(m => m.PodeGerirComercial).Returns(podeGerir || visaoTotal);
        // Mocks sem nenhuma flag de gestão representam uma vendedora comum (papel Comercial) —
        // usado por LeadService.CriarAsync pra decidir entre "atribui a si mesma" e round-robin.
        mock.Setup(m => m.IsInRole(Roles.Comercial)).Returns(!visaoTotal && !gestorComercial && !podeGerir);
        return mock;
    }

    public async Task<ApplicationUser> CriarUsuarioAsync(ApplicationDbContext db, string nome, Guid? gestorId = null)
    {
        var usuario = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = $"{nome.ToLowerInvariant()}@teste.local",
            NormalizedUserName = $"{nome.ToUpperInvariant()}@TESTE.LOCAL",
            Email = $"{nome.ToLowerInvariant()}@teste.local",
            NormalizedEmail = $"{nome.ToUpperInvariant()}@TESTE.LOCAL",
            NomeCompleto = nome,
            GestorComercialId = gestorId,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.Users.Add(usuario);
        await db.SaveChangesAsync();
        return usuario;
    }

    public async Task<CrmPipelineStage> CriarEtapaAsync(ApplicationDbContext db, string nome, int ordem, TipoEtapaPipeline tipo = TipoEtapaPipeline.Aberta)
    {
        var etapa = new CrmPipelineStage { Nome = nome, Ordem = ordem, Tipo = tipo };
        db.CrmPipelineStages.Add(etapa);
        await db.SaveChangesAsync();
        return etapa;
    }

    /// <summary>Cria (e reaproveita, se já existir) a etapa de lead padrão usada pelos testes que não se importam com qual etapa é.</summary>
    public async Task<CrmLeadStage> ObterOuCriarEtapaLeadAsync(ApplicationDbContext db, string nome = "Pré-cadastro", int ordem = 1)
    {
        var existente = await db.CrmLeadStages.FirstOrDefaultAsync(s => s.Nome == nome);
        if (existente is not null) return existente;

        var etapa = new CrmLeadStage { Nome = nome, Ordem = ordem };
        db.CrmLeadStages.Add(etapa);
        await db.SaveChangesAsync();
        return etapa;
    }

    /// <summary>Cria o papel (se ainda não existir) e o atribui ao usuário — usado nos testes de distribuição automática.</summary>
    public async Task AtribuirPapelAsync(ApplicationDbContext db, ApplicationUser usuario, string papel)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == papel);
        if (role is null)
        {
            role = new ApplicationRole(papel) { NormalizedName = papel.ToUpperInvariant() };
            db.Roles.Add(role);
            await db.SaveChangesAsync();
        }

        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = usuario.Id, RoleId = role.Id });
        await db.SaveChangesAsync();
    }

    public void Dispose() => _connection.Dispose();
}
