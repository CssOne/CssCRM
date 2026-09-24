using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

public sealed class LeadKanbanService(ApplicationDbContext db, IEquipeComercialService equipe) : ILeadKanbanService
{
    public async Task<LeadKanbanBoardDto> ObterBoardAsync(LeadKanbanFilterRequest filtro, CancellationToken ct)
    {
        var etapas = await db.CrmLeadStages.AsNoTracking()
            .Where(s => s.Ativa)
            .OrderBy(s => s.Ordem)
            .ToListAsync(ct);

        var query = db.CrmLeads.AsNoTracking()
            .Include(l => l.Responsavel)
            .Include(l => l.LeadTags).ThenInclude(lt => lt.Tag)
            .Include(l => l.Oportunidades).ThenInclude(o => o.Veiculo)
            .AsQueryable();

        if (!filtro.IncluirArquivados) query = query.Where(l => !l.Arquivado);
        if (filtro.CriadoManualmente.HasValue) query = query.Where(l => l.CriadoManualmente == filtro.CriadoManualmente.Value);

        var visiveis = await equipe.ObterVendedoresVisiveisAsync(ct);
        if (visiveis is not null)
        {
            query = query.Where(l => l.ResponsavelId != null && visiveis.Contains(l.ResponsavelId.Value));
        }

        if (!string.IsNullOrWhiteSpace(filtro.Busca))
        {
            var busca = filtro.Busca.Trim();
            var buscaDigitos = DocumentValidation.SomenteDigitos(busca);
            query = query.Where(l =>
                EF.Functions.ILike(l.NomeOuRazaoSocial, $"%{busca}%") ||
                (l.Email != null && EF.Functions.ILike(l.Email, $"%{busca}%")) ||
                (buscaDigitos != "" && l.DocumentoNormalizado != null && l.DocumentoNormalizado.Contains(buscaDigitos)) ||
                (buscaDigitos != "" && l.TelefoneNormalizado != null && l.TelefoneNormalizado.Contains(buscaDigitos)));
        }

        if (filtro.ResponsavelId.HasValue) query = query.Where(l => l.ResponsavelId == filtro.ResponsavelId);
        // Origem é informação só de administrador (visão total): para os demais, nem filtra nem aparece no cartão.
        var podeVerOrigem = visiveis is null;
        if (podeVerOrigem && !string.IsNullOrWhiteSpace(filtro.Origem)) query = query.Where(l => l.Origem == filtro.Origem);
        if (!string.IsNullOrWhiteSpace(filtro.Regional)) query = query.Where(l => l.Regional == filtro.Regional);

        // Dados migrados em épocas diferentes gravaram TipoIndicacao com capitalização distinta
        // (ex.: "LEAD" vs "Lead") — ILike compara sem diferenciar maiúsculas/minúsculas.
        query = filtro.Categoria switch
        {
            // Pelo marcador da migração, não pelo texto da Origem (que vira a tag de campanha).
            "Migração" => query.Where(l => l.ConsentimentoOrigem == OrigemLead.MarcadorMigracaoNotion),
            "Indicação" => query.Where(l => l.TipoIndicacao != null && EF.Functions.ILike(l.TipoIndicacao, "Indicação")),
            "Lead" => query.Where(l => l.TipoIndicacao != null && EF.Functions.ILike(l.TipoIndicacao, "Lead")),
            _ => query,
        };

        query = filtro.Fonte switch
        {
            "Notion" => query.Where(l => l.ConsentimentoOrigem == OrigemLead.MarcadorMigracaoNotion
                || l.ConsentimentoOrigem == OrigemLead.MarcadorSincronizacaoNotion),
            // Direto dos anúncios: webhook do Meta Lead Ads (tem o ID do lead no Meta) ou formulário do site.
            "TrafegoPago" => query.Where(l => l.MetaLeadId != null || l.ConsentimentoOrigem == OrigemLead.MarcadorFormularioSite),
            _ => query,
        };

        if (filtro.DataChegadaInicio is { } chegadaInicio)
        {
            var inicioUtc = chegadaInicio.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(l => l.CriadoEm >= inicioUtc);
        }
        if (filtro.DataChegadaFim is { } chegadaFim)
        {
            var fimUtc = chegadaFim.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            query = query.Where(l => l.CriadoEm <= fimUtc);
        }
        if (filtro.DataVendaInicio is { } vendaInicio)
        {
            var inicioUtc = vendaInicio.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(l => l.Oportunidades.Any(o => o.DataEfetivaFechamento >= inicioUtc));
        }
        if (filtro.DataVendaFim is { } vendaFim)
        {
            var fimUtc = vendaFim.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            query = query.Where(l => l.Oportunidades.Any(o => o.DataEfetivaFechamento <= fimUtc));
        }

        var leads = await query.ToListAsync(ct);

        LeadKanbanCardDto ParaCartao(CrmLead l)
        {
            // A oportunidade mais recente é a fonte dos selos Migração/Indicação — normalmente é a
            // que fechou a venda (o lead só chega na coluna "Venda concluída" depois disso).
            var oportunidade = l.Oportunidades.OrderByDescending(o => o.CriadoEm).FirstOrDefault();
            // "Sem contato" agora reflete se o lead tem ALGUM telefone cadastrado (não mais se já
            // houve uma atividade registrada) — some sozinho assim que um telefone é preenchido.
            var semTelefone = string.IsNullOrWhiteSpace(l.Telefone) && string.IsNullOrWhiteSpace(l.Telefone2);
            return new(
                l.Id, l.NomeOuRazaoSocial, l.Telefone, l.Telefone2, l.Email, l.Estado, podeVerOrigem ? l.Origem : null, l.Campanha,
                // Placa do lead; se ainda não tiver, a do veículo da oportunidade mais recente.
                l.Placa ?? oportunidade?.Veiculo?.Placa, l.TemSeguro, l.UtilidadeVeiculo, l.TipoIndicacao,
                oportunidade?.Migracao ?? false, oportunidade?.Indicacao, l.CriadoManualmente,
                l.ResponsavelId, l.Responsavel?.NomeCompleto,
                l.LeadTags.Select(lt => lt.Tag.Nome).ToList(),
                l.CriadoEm, l.UltimoContatoEm, semTelefone, l.Arquivado, l.RowVersion, l.ProdutoInteresse, l.ValorAdesao);
        }

        // Coluna virtual (sem linha em CrmLeadStage): leads que ainda não foram trabalhados por
        // ninguém. Fica sempre em primeiro, pra vendedora enxergar de cara quem ainda não pegou.
        var semEtapa = new LeadKanbanColumnDto(
            new LeadStageDto(null, "Sem etapa", -1, "#94a3b8", false, true),
            leads.Where(l => l.EtapaId is null).OrderByDescending(l => l.CriadoEm).Select(ParaCartao).ToList());

        var colunas = new List<LeadKanbanColumnDto> { semEtapa };
        colunas.AddRange(etapas.Select(etapa =>
        {
            var cartoes = leads
                .Where(l => l.EtapaId == etapa.Id)
                .OrderByDescending(l => l.CriadoEm)
                .Select(ParaCartao)
                .ToList();

            var etapaDto = new LeadStageDto(etapa.Id, etapa.Nome, etapa.Ordem, etapa.Cor, etapa.Fechada, etapa.Ativa);
            return new LeadKanbanColumnDto(etapaDto, cartoes);
        }));

        return new LeadKanbanBoardDto(colunas);
    }
}
