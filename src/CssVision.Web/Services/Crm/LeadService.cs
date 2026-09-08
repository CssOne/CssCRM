using System.Linq.Expressions;
using ClosedXML.Excel;
using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Authorization;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

public sealed class LeadService(
    ApplicationDbContext db,
    ICurrentUserService currentUser,
    IEquipeComercialService equipe,
    ILeadAssignmentService assignment,
    IAuditSink audit) : ILeadService
{
    public async Task<PagedResult<LeadListItemDto>> ListarAsync(LeadFilterRequest filtro, CancellationToken ct)
    {
        var query = await QueryEscopadaAsync(filtro.IncluirArquivados, ct);
        query = AplicarFiltros(query, filtro);
        query = AplicarOrdenacao(query, filtro.OrdenarPor, filtro.OrdemDescendente);

        var total = await query.CountAsync(ct);

        var itens = await query
            .Skip((filtro.Pagina - 1) * filtro.TamanhoPagina)
            .Take(filtro.TamanhoPagina)
            .Select(l => new LeadListItemDto(
                l.Id,
                l.NomeOuRazaoSocial,
                l.TipoPessoa,
                l.DocumentoNormalizado,
                l.Telefone,
                l.Email,
                l.Cidade,
                l.Estado,
                l.Regional,
                l.Origem,
                l.EtapaId,
                l.Etapa != null ? l.Etapa.Nome : null,
                l.Etapa != null ? l.Etapa.Cor : null,
                l.Oportunidades
                    .Where(o => !o.Arquivado && o.Etapa.Tipo == TipoEtapaPipeline.Aberta)
                    .OrderByDescending(o => o.EtapaDesde)
                    .Select(o => o.Etapa.Nome)
                    .FirstOrDefault(),
                l.ResponsavelId,
                l.Responsavel != null ? l.Responsavel.NomeCompleto : null,
                l.LeadTags.Select(lt => lt.Tag.Nome).ToList(),
                l.CriadoEm,
                l.UltimoContatoEm,
                l.ProximoContatoEm,
                l.UltimoContatoEm == null,
                l.Arquivado
            ))
            .ToListAsync(ct);

        var mascarados = itens.Select(i => i with { DocumentoMascarado = DocumentValidation.MascararDocumento(i.DocumentoMascarado) }).ToList();

        return new PagedResult<LeadListItemDto>
        {
            Itens = mascarados,
            Pagina = filtro.Pagina,
            TamanhoPagina = filtro.TamanhoPagina,
            TotalRegistros = total
        };
    }

    public async Task<LeadDetailDto> ObterPorIdAsync(Guid id, CancellationToken ct)
    {
        var lead = await CarregarComEscopoAsync(id, ct);
        return ParaDetailDto(lead);
    }

    public async Task<IReadOnlyList<LeadTimelineItemDto>> ObterTimelineAsync(Guid id, CancellationToken ct)
    {
        var lead = await CarregarComEscopoAsync(id, ct);
        var itens = new List<LeadTimelineItemDto>
        {
            new(lead.Id, TipoEventoTimeline.LeadCriado, "Lead cadastrado", lead.Origem is null ? null : $"Origem: {lead.Origem}", null, lead.CriadoEm)
        };

        var auditorias = await db.CrmAuditLogs.AsNoTracking()
            .Where(a => a.EntidadeTipo == nameof(CrmLead) && a.EntidadeId == id && a.Acao == "LeadAtualizado")
            .Select(a => new { a.Id, a.OcorridoEm, Usuario = a.Usuario.NomeCompleto })
            .ToListAsync(ct);
        itens.AddRange(auditorias.Select(a =>
            new LeadTimelineItemDto(a.Id, TipoEventoTimeline.LeadAtualizado, "Cadastro atualizado", null, a.Usuario, a.OcorridoEm)));

        var atribuicoes = await db.CrmLeadAssignmentHistories.AsNoTracking()
            .Where(h => h.LeadId == id)
            .Select(h => new
            {
                h.Id,
                h.AlteradoEm,
                h.Motivo,
                ResponsavelNovo = h.ResponsavelNovo.NomeCompleto,
                AlteradoPor = h.AlteradoPor.NomeCompleto
            })
            .ToListAsync(ct);
        itens.AddRange(atribuicoes.Select(a =>
            new LeadTimelineItemDto(a.Id, TipoEventoTimeline.TrocaResponsavel, $"Atribuído para {a.ResponsavelNovo}", a.Motivo, a.AlteradoPor, a.AlteradoEm)));

        var oportunidadeIds = await db.CrmOpportunities.AsNoTracking()
            .Where(o => o.LeadId == id)
            .Select(o => o.Id)
            .ToListAsync(ct);

        if (oportunidadeIds.Count > 0)
        {
            var etapas = await db.CrmStageHistories.AsNoTracking()
                .Where(h => oportunidadeIds.Contains(h.OpportunityId))
                .Select(h => new
                {
                    h.Id,
                    h.AlteradoEm,
                    h.MotivoPerdaDescricao,
                    EtapaNova = h.EtapaNova.Nome,
                    EtapaTipo = h.EtapaNova.Tipo,
                    Usuario = h.Usuario.NomeCompleto
                })
                .ToListAsync(ct);
            itens.AddRange(etapas.Select(e => new LeadTimelineItemDto(
                e.Id,
                e.EtapaTipo switch
                {
                    TipoEtapaPipeline.Ganho => TipoEventoTimeline.OportunidadeGanha,
                    TipoEtapaPipeline.Perdido => TipoEventoTimeline.OportunidadePerdida,
                    _ => TipoEventoTimeline.MudancaEtapa
                },
                $"Movida para {e.EtapaNova}",
                e.MotivoPerdaDescricao,
                e.Usuario,
                e.AlteradoEm)));
        }

        var atividades = await db.CrmActivities.AsNoTracking()
            .Where(a => a.LeadId == id)
            .Select(a => new { a.Id, a.Tipo, a.Assunto, a.Resultado, a.DataHoraConclusao, a.DataHoraPrevista, Responsavel = a.Responsavel.NomeCompleto })
            .ToListAsync(ct);
        itens.AddRange(atividades.Select(a => new LeadTimelineItemDto(
            a.Id, TipoEventoTimeline.Atividade, a.Assunto, a.Resultado, a.Responsavel, a.DataHoraConclusao ?? a.DataHoraPrevista)));

        var notas = await db.CrmNotes.AsNoTracking()
            .Where(n => n.LeadId == id)
            .Select(n => new { n.Id, n.Texto, n.CriadoEm, Autor = n.Autor.NomeCompleto })
            .ToListAsync(ct);
        itens.AddRange(notas.Select(n => new LeadTimelineItemDto(n.Id, TipoEventoTimeline.Nota, "Anotação", n.Texto, n.Autor, n.CriadoEm)));

        var anexos = await db.CrmAttachments.AsNoTracking()
            .Where(a => a.LeadId == id)
            .Select(a => new { a.Id, a.NomeArquivo, a.CriadoEm, EnviadoPor = a.EnviadoPor.NomeCompleto })
            .ToListAsync(ct);
        itens.AddRange(anexos.Select(a => new LeadTimelineItemDto(a.Id, TipoEventoTimeline.Proposta, $"Arquivo enviado: {a.NomeArquivo}", null, a.EnviadoPor, a.CriadoEm)));

        return itens.OrderByDescending(i => i.OcorridoEm).ToList();
    }

    public async Task<CriarLeadResultado> CriarAsync(LeadCreateRequest request, CancellationToken ct)
    {
        var documentoNormalizado = DocumentValidation.NormalizarDocumento(request.Documento, out var documentoValido);
        if (!string.IsNullOrEmpty(documentoNormalizado) && !documentoValido)
        {
            throw new CrmBusinessException("CPF/CNPJ informado é inválido.", "documento_invalido");
        }

        var emailNormalizado = DocumentValidation.NormalizarEmail(request.Email);
        if (!string.IsNullOrWhiteSpace(request.Email) && emailNormalizado is null)
        {
            throw new CrmBusinessException("E-mail informado é inválido.", "email_invalido");
        }

        var telefoneNormalizado = DocumentValidation.NormalizarTelefone(request.Telefone);

        var duplicidade = await DetectarDuplicidadeAsync(documentoNormalizado, emailNormalizado, telefoneNormalizado, request.IgnorarDuplicidade, ct);
        if (duplicidade is not null) return new CriarLeadResultado(null, duplicidade);

        // Responsável explícito respeita o escopo de quem está criando. Sem escolha explícita:
        // uma vendedora cadastrando um contato dela mesma continua caindo pra ela (comportamento
        // intuitivo, "é meu"); só quando não há um dono natural (admin/gestor cadastrando sem
        // escolher alguém, ou nenhum usuário logado) a distribuição automática decide — round-robin
        // por quem tem menos leads no mês, respeitando o limite mensal de cada vendedor. Pode ficar
        // sem responsável se ninguém estiver elegível, do mesmo jeito que o lead pode ficar sem etapa.
        Guid? responsavelId;
        if (request.ResponsavelId.HasValue)
        {
            if (!await equipe.PodeAcessarVendedorAsync(request.ResponsavelId.Value, ct))
            {
                throw new CrmForbiddenException("Você não pode atribuir leads para este vendedor.");
            }
            responsavelId = request.ResponsavelId.Value;
        }
        else if (currentUser.IsInRole(Roles.Comercial))
        {
            responsavelId = currentUser.UserId;
        }
        else
        {
            responsavelId = await assignment.ProximoResponsavelAsync(ct);
        }

        var lead = new CrmLead
        {
            // Fica nula de propósito: lead novo sem etapa marcada é como a vendedora enxerga
            // "ninguém pegou ainda" — ela mesma arrasta pra uma etapa quando começa a trabalhar.
            EtapaId = request.EtapaId,
            NomeOuRazaoSocial = request.NomeOuRazaoSocial.Trim(),
            TipoPessoa = request.TipoPessoa,
            DocumentoNormalizado = documentoNormalizado,
            Telefone = request.Telefone,
            TelefoneNormalizado = telefoneNormalizado,
            WhatsApp = request.WhatsApp,
            Email = request.Email?.Trim(),
            EmailNormalizado = emailNormalizado,
            DataNascimento = request.DataNascimento,
            Cidade = request.Cidade,
            Estado = request.Estado?.ToUpperInvariant(),
            Regional = request.Regional,
            Origem = request.Origem,
            Campanha = request.Campanha,
            ProdutoInteresse = request.ProdutoInteresse,
            Gclid = request.Gclid,
            UtmMedium = request.UtmMedium,
            UtmSource = request.UtmSource,
            UtmTerm = request.UtmTerm,
            MetaClickId = request.MetaClickId,
            MetaFormId = request.MetaFormId,
            MetaLeadId = request.MetaLeadId,
            IndicadoPorLeadId = request.IndicadoPorLeadId,
            TipoIndicacao = request.TipoIndicacao,
            ResponsavelId = responsavelId,
            Observacoes = request.Observacoes,
            ConsentimentoContato = request.ConsentimentoContato,
            ConsentimentoDataEm = request.ConsentimentoContato ? DateTimeOffset.UtcNow : null,
            ConsentimentoOrigem = request.ConsentimentoOrigem
        };

        await AplicarTagsAsync(lead, request.Tags, ct);

        db.CrmLeads.Add(lead);
        await db.SaveChangesAsync(ct);

        await audit.RegistrarAsync("LeadCriado", nameof(CrmLead), lead.Id, new { lead.NomeOuRazaoSocial }, ct);

        return new CriarLeadResultado(await ObterPorIdAsync(lead.Id, ct), null);
    }

    public async Task<LeadDetailDto> AtualizarAsync(Guid id, LeadUpdateRequest request, CancellationToken ct)
    {
        var lead = await CarregarComEscopoAsync(id, ct);

        db.Entry(lead).Property(l => l.RowVersion).OriginalValue = request.RowVersion;

        var documentoNormalizado = DocumentValidation.NormalizarDocumento(request.Documento, out var documentoValido);
        if (!string.IsNullOrEmpty(documentoNormalizado) && !documentoValido)
        {
            throw new CrmBusinessException("CPF/CNPJ informado é inválido.", "documento_invalido");
        }

        var emailNormalizado = DocumentValidation.NormalizarEmail(request.Email);
        if (!string.IsNullOrWhiteSpace(request.Email) && emailNormalizado is null)
        {
            throw new CrmBusinessException("E-mail informado é inválido.", "email_invalido");
        }

        if (documentoNormalizado != lead.DocumentoNormalizado || emailNormalizado != lead.EmailNormalizado)
        {
            var duplicidade = await DetectarDuplicidadeAsync(
                documentoNormalizado == lead.DocumentoNormalizado ? null : documentoNormalizado,
                emailNormalizado == lead.EmailNormalizado ? null : emailNormalizado,
                null, true, ct, ignorarLeadId: lead.Id);
            if (duplicidade is not null)
            {
                throw new CrmBusinessException(
                    $"Já existe outro lead com o mesmo {duplicidade.CampoDuplicado} ({duplicidade.NomeExistente}).",
                    "duplicidade", duplicidade);
            }
        }

        lead.NomeOuRazaoSocial = request.NomeOuRazaoSocial.Trim();
        lead.TipoPessoa = request.TipoPessoa;
        lead.DocumentoNormalizado = documentoNormalizado;
        lead.Telefone = request.Telefone;
        lead.TelefoneNormalizado = DocumentValidation.NormalizarTelefone(request.Telefone);
        lead.WhatsApp = request.WhatsApp;
        lead.Email = request.Email?.Trim();
        lead.EmailNormalizado = emailNormalizado;
        lead.DataNascimento = request.DataNascimento;
        lead.Cidade = request.Cidade;
        lead.Estado = request.Estado?.ToUpperInvariant();
        lead.Regional = request.Regional;
        lead.Origem = request.Origem;
        lead.Campanha = request.Campanha;
        lead.ProdutoInteresse = request.ProdutoInteresse;
        lead.Gclid = request.Gclid;
        lead.UtmMedium = request.UtmMedium;
        lead.UtmSource = request.UtmSource;
        lead.UtmTerm = request.UtmTerm;
        lead.MetaClickId = request.MetaClickId;
        lead.MetaFormId = request.MetaFormId;
        lead.MetaLeadId = request.MetaLeadId;
        lead.IndicadoPorLeadId = request.IndicadoPorLeadId;
        lead.TipoIndicacao = request.TipoIndicacao;
        lead.Observacoes = request.Observacoes;
        lead.ConsentimentoContato = request.ConsentimentoContato;
        lead.ConsentimentoOrigem = request.ConsentimentoOrigem;
        if (request.ConsentimentoContato && lead.ConsentimentoDataEm is null)
        {
            lead.ConsentimentoDataEm = DateTimeOffset.UtcNow;
        }

        await AplicarTagsAsync(lead, request.Tags, ct);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new CrmConcurrencyException();
        }

        await audit.RegistrarAsync("LeadAtualizado", nameof(CrmLead), lead.Id, null, ct);

        return await ObterPorIdAsync(lead.Id, ct);
    }

    public async Task AtribuirAsync(Guid id, LeadAssignRequest request, CancellationToken ct)
    {
        if (!currentUser.PodeGerirComercial)
        {
            throw new CrmForbiddenException("Apenas gestores comerciais podem redistribuir leads.");
        }

        var lead = await CarregarComEscopoAsync(id, ct);

        if (!await equipe.PodeAcessarVendedorAsync(request.ResponsavelId, ct))
        {
            throw new CrmForbiddenException("Você não pode atribuir leads para este vendedor.");
        }

        var anteriorId = lead.ResponsavelId;
        lead.ResponsavelId = request.ResponsavelId;

        db.CrmLeadAssignmentHistories.Add(new CrmLeadAssignmentHistory
        {
            LeadId = lead.Id,
            ResponsavelAnteriorId = anteriorId,
            ResponsavelNovoId = request.ResponsavelId,
            AlteradoPorId = currentUser.UserId,
            Motivo = request.Motivo
        });

        await db.SaveChangesAsync(ct);
        await audit.RegistrarAsync("LeadAtribuido", nameof(CrmLead), lead.Id, new { request.ResponsavelId }, ct);
    }

    public async Task<LeadDetailDto> MudarEtapaAsync(Guid id, ChangeLeadStageRequest request, CancellationToken ct)
    {
        var lead = await CarregarComEscopoAsync(id, ct);
        db.Entry(lead).Property(l => l.RowVersion).OriginalValue = request.RowVersion;

        if (request.NovaEtapaId.HasValue)
        {
            var novaEtapa = await db.CrmLeadStages.FirstOrDefaultAsync(s => s.Id == request.NovaEtapaId, ct)
                ?? throw new CrmNotFoundException("Etapa de lead", request.NovaEtapaId.Value);
            lead.EtapaId = novaEtapa.Id;
        }
        else
        {
            lead.EtapaId = null;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new CrmConcurrencyException();
        }

        await audit.RegistrarAsync("LeadMudouEtapa", nameof(CrmLead), lead.Id, new { EtapaNova = request.NovaEtapaId }, ct);

        return await ObterPorIdAsync(lead.Id, ct);
    }

    public async Task<int> AtribuirEmLoteAsync(LeadBulkAssignRequest request, CancellationToken ct)
    {
        if (!currentUser.PodeGerirComercial)
        {
            throw new CrmForbiddenException("Apenas gestores comerciais podem redistribuir leads.");
        }

        if (!await equipe.PodeAcessarVendedorAsync(request.ResponsavelId, ct))
        {
            throw new CrmForbiddenException("Você não pode atribuir leads para este vendedor.");
        }

        var visiveis = await equipe.ObterVendedoresVisiveisAsync(ct);
        var query = db.CrmLeads.Where(l => request.LeadIds.Contains(l.Id) && !l.Arquivado);
        if (visiveis is not null)
        {
            query = query.Where(l => l.ResponsavelId != null && visiveis.Contains(l.ResponsavelId.Value));
        }

        var leads = await query.ToListAsync(ct);
        var agora = DateTimeOffset.UtcNow;

        foreach (var lead in leads)
        {
            var anteriorId = lead.ResponsavelId;
            lead.ResponsavelId = request.ResponsavelId;
            db.CrmLeadAssignmentHistories.Add(new CrmLeadAssignmentHistory
            {
                LeadId = lead.Id,
                ResponsavelAnteriorId = anteriorId,
                ResponsavelNovoId = request.ResponsavelId,
                AlteradoPorId = currentUser.UserId,
                AlteradoEm = agora,
                Motivo = request.Motivo
            });
        }

        await db.SaveChangesAsync(ct);
        await audit.RegistrarAsync("LeadRedistribuicaoLote", nameof(CrmLead), null, new { Quantidade = leads.Count, request.ResponsavelId }, ct);

        return leads.Count;
    }

    public async Task<Guid> AdicionarNotaAsync(Guid leadId, string texto, CancellationToken ct)
    {
        var lead = await CarregarComEscopoAsync(leadId, ct);
        var nota = new CrmNote { LeadId = lead.Id, AutorId = currentUser.UserId, Texto = texto.Trim() };
        db.CrmNotes.Add(nota);
        await db.SaveChangesAsync(ct);
        return nota.Id;
    }

    public async Task<LeadImportResultDto> ImportarAsync(Stream planilha, CancellationToken ct)
    {
        if (!currentUser.PodeGerirComercial)
        {
            throw new CrmForbiddenException("Apenas gestores comerciais podem importar leads em lote.");
        }

        using var workbook = new XLWorkbook(planilha);
        var sheet = workbook.Worksheets.First();
        var linhas = sheet.RowsUsed().Skip(1).ToList();

        var erros = new List<string>();
        var importados = 0;
        var duplicados = 0;

        var usuariosPorEmail = await db.Users.AsNoTracking()
            .ToDictionaryAsync(u => u.Email!.ToLowerInvariant(), u => u.Id, ct);

        foreach (var linha in linhas)
        {
            var numeroLinha = linha.RowNumber();
            try
            {
                var nome = linha.Cell(1).GetString().Trim();
                if (string.IsNullOrWhiteSpace(nome))
                {
                    erros.Add($"Linha {numeroLinha}: nome é obrigatório.");
                    continue;
                }

                var tipoPessoaTexto = linha.Cell(2).GetString().Trim();
                var tipoPessoa = tipoPessoaTexto.Equals("juridica", StringComparison.OrdinalIgnoreCase) ||
                                  tipoPessoaTexto.Equals("jurídica", StringComparison.OrdinalIgnoreCase)
                    ? TipoPessoa.Juridica
                    : TipoPessoa.Fisica;

                var documento = linha.Cell(3).GetString().Trim();
                var telefone = linha.Cell(4).GetString().Trim();
                var email = linha.Cell(5).GetString().Trim();
                var cidade = linha.Cell(6).GetString().Trim();
                var estado = linha.Cell(7).GetString().Trim();
                var regional = linha.Cell(8).GetString().Trim();
                var origem = linha.Cell(9).GetString().Trim();
                var responsavelEmail = linha.Cell(10).GetString().Trim().ToLowerInvariant();

                var documentoNormalizado = DocumentValidation.NormalizarDocumento(documento, out var documentoValido);
                if (!string.IsNullOrEmpty(documentoNormalizado) && !documentoValido)
                {
                    erros.Add($"Linha {numeroLinha}: CPF/CNPJ inválido.");
                    continue;
                }

                var emailNormalizado = DocumentValidation.NormalizarEmail(email);
                var duplicidade = await DetectarDuplicidadeAsync(documentoNormalizado, emailNormalizado, null, false, ct);
                if (duplicidade is not null)
                {
                    duplicados++;
                    continue;
                }

                Guid? responsavelId = null;
                if (!string.IsNullOrWhiteSpace(responsavelEmail) && usuariosPorEmail.TryGetValue(responsavelEmail, out var uid))
                {
                    if (await equipe.PodeAcessarVendedorAsync(uid, ct)) responsavelId = uid;
                }
                responsavelId ??= await assignment.ProximoResponsavelAsync(ct);

                db.CrmLeads.Add(new CrmLead
                {
                    NomeOuRazaoSocial = nome,
                    TipoPessoa = tipoPessoa,
                    DocumentoNormalizado = documentoNormalizado,
                    Telefone = telefone,
                    TelefoneNormalizado = DocumentValidation.NormalizarTelefone(telefone),
                    Email = email,
                    EmailNormalizado = emailNormalizado,
                    Cidade = cidade,
                    Estado = string.IsNullOrWhiteSpace(estado) ? null : estado.ToUpperInvariant(),
                    Regional = regional,
                    Origem = string.IsNullOrWhiteSpace(origem) ? "Importação" : origem,
                    ResponsavelId = responsavelId
                });

                importados++;
            }
            catch (Exception ex)
            {
                erros.Add($"Linha {numeroLinha}: {ex.Message}");
            }
        }

        await db.SaveChangesAsync(ct);
        await audit.RegistrarAsync("LeadsImportados", nameof(CrmLead), null, new { importados, duplicados, erros = erros.Count }, ct);

        return new LeadImportResultDto(linhas.Count, importados, duplicados, erros.Count, erros);
    }

    public async Task<byte[]> ExportarAsync(LeadFilterRequest filtro, CancellationToken ct)
    {
        var query = await QueryEscopadaAsync(filtro.IncluirArquivados, ct);
        query = AplicarFiltros(query, filtro);

        var leads = await query
            .Select(l => new
            {
                l.NomeOuRazaoSocial,
                l.TipoPessoa,
                l.DocumentoNormalizado,
                l.Telefone,
                l.Email,
                l.Cidade,
                l.Estado,
                l.Regional,
                l.Origem,
                EtapaNome = l.Etapa != null ? l.Etapa.Nome : "Sem etapa",
                Responsavel = l.Responsavel != null ? l.Responsavel.NomeCompleto : null,
                l.CriadoEm
            })
            .ToListAsync(ct);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Leads");
        string[] cabecalho = ["Nome/Razão Social", "Tipo", "CPF/CNPJ", "Telefone", "E-mail", "Cidade", "UF", "Regional", "Origem", "Etapa", "Responsável", "Criado em"];
        for (var i = 0; i < cabecalho.Length; i++) sheet.Cell(1, i + 1).Value = cabecalho[i];

        var linha = 2;
        foreach (var l in leads)
        {
            sheet.Cell(linha, 1).Value = l.NomeOuRazaoSocial;
            sheet.Cell(linha, 2).Value = l.TipoPessoa == TipoPessoa.Fisica ? "Física" : "Jurídica";
            sheet.Cell(linha, 3).Value = DocumentValidation.FormatarDocumento(l.DocumentoNormalizado);
            sheet.Cell(linha, 4).Value = l.Telefone;
            sheet.Cell(linha, 5).Value = l.Email;
            sheet.Cell(linha, 6).Value = l.Cidade;
            sheet.Cell(linha, 7).Value = l.Estado;
            sheet.Cell(linha, 8).Value = l.Regional;
            sheet.Cell(linha, 9).Value = l.Origem;
            sheet.Cell(linha, 10).Value = l.EtapaNome;
            sheet.Cell(linha, 11).Value = l.Responsavel;
            sheet.Cell(linha, 12).Value = l.CriadoEm.ToString("dd/MM/yyyy HH:mm");
            linha++;
        }

        sheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    // --- Métodos auxiliares privados ---

    private async Task<IQueryable<CrmLead>> QueryEscopadaAsync(bool incluirArquivados, CancellationToken ct)
    {
        var query = db.CrmLeads.AsNoTracking().AsQueryable();
        if (!incluirArquivados) query = query.Where(l => !l.Arquivado);

        var visiveis = await equipe.ObterVendedoresVisiveisAsync(ct);
        if (visiveis is not null)
        {
            query = query.Where(l => l.ResponsavelId != null && visiveis.Contains(l.ResponsavelId.Value));
        }

        return query;
    }

    private async Task<CrmLead> CarregarComEscopoAsync(Guid id, CancellationToken ct)
    {
        var lead = await db.CrmLeads
            .Include(l => l.LeadTags).ThenInclude(lt => lt.Tag)
            .Include(l => l.Responsavel)
            .Include(l => l.IndicadoPorLead)
            .Include(l => l.Etapa)
            .Include(l => l.Oportunidades).ThenInclude(o => o.Etapa)
            .FirstOrDefaultAsync(l => l.Id == id, ct)
            ?? throw new CrmNotFoundException("Lead", id);

        if (!await equipe.PodeAcessarVendedorAsync(lead.ResponsavelId ?? Guid.Empty, ct))
        {
            throw new CrmForbiddenException();
        }

        return lead;
    }

    private static IQueryable<CrmLead> AplicarFiltros(IQueryable<CrmLead> query, LeadFilterRequest filtro)
    {
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
        if (!string.IsNullOrWhiteSpace(filtro.Regional)) query = query.Where(l => l.Regional == filtro.Regional);
        if (!string.IsNullOrWhiteSpace(filtro.Origem)) query = query.Where(l => l.Origem == filtro.Origem);
        if (filtro.LeadEtapaId.HasValue) query = query.Where(l => l.EtapaId == filtro.LeadEtapaId);
        if (filtro.EtapaId.HasValue) query = query.Where(l => l.Oportunidades.Any(o => o.EtapaId == filtro.EtapaId && !o.Arquivado));
        if (filtro.DataInicio.HasValue)
        {
            var inicio = filtro.DataInicio.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(l => l.CriadoEm >= inicio);
        }
        if (filtro.DataFim.HasValue)
        {
            var fim = filtro.DataFim.Value.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            query = query.Where(l => l.CriadoEm <= fim);
        }
        if (filtro.Tags is { Count: > 0 })
        {
            query = query.Where(l => l.LeadTags.Any(lt => filtro.Tags.Contains(lt.Tag.Nome)));
        }

        return query;
    }

    private static IQueryable<CrmLead> AplicarOrdenacao(IQueryable<CrmLead> query, string? ordenarPor, bool desc)
    {
        Expression<Func<CrmLead, object?>> chave = ordenarPor?.ToLowerInvariant() switch
        {
            "nome" => l => l.NomeOuRazaoSocial,
            "etapa" => l => l.Etapa != null ? l.Etapa.Nome : null,
            "ultimocontato" => l => l.UltimoContatoEm,
            "proximocontato" => l => l.ProximoContatoEm,
            _ => l => l.CriadoEm
        };

        return desc ? query.OrderByDescending(chave) : query.OrderBy(chave);
    }

    private async Task<LeadDuplicateWarningDto?> DetectarDuplicidadeAsync(
        string? documentoNormalizado, string? emailNormalizado, string? telefoneNormalizado, bool ignorarDuplicidade,
        CancellationToken ct, Guid? ignorarLeadId = null)
    {
        if (!string.IsNullOrEmpty(documentoNormalizado))
        {
            var existente = await db.CrmLeads.AsNoTracking()
                .Where(l => l.DocumentoNormalizado == documentoNormalizado && !l.Arquivado && l.Id != ignorarLeadId)
                .Select(l => new { l.Id, l.NomeOuRazaoSocial })
                .FirstOrDefaultAsync(ct);
            if (existente is not null)
            {
                return new LeadDuplicateWarningDto(existente.Id, existente.NomeOuRazaoSocial, "CPF/CNPJ");
            }
        }

        if (!string.IsNullOrEmpty(emailNormalizado))
        {
            var existente = await db.CrmLeads.AsNoTracking()
                .Where(l => l.EmailNormalizado == emailNormalizado && !l.Arquivado && l.Id != ignorarLeadId)
                .Select(l => new { l.Id, l.NomeOuRazaoSocial })
                .FirstOrDefaultAsync(ct);
            if (existente is not null)
            {
                return new LeadDuplicateWarningDto(existente.Id, existente.NomeOuRazaoSocial, "e-mail");
            }
        }

        if (!ignorarDuplicidade && !string.IsNullOrEmpty(telefoneNormalizado))
        {
            var existente = await db.CrmLeads.AsNoTracking()
                .Where(l => l.TelefoneNormalizado == telefoneNormalizado && !l.Arquivado && l.Id != ignorarLeadId)
                .Select(l => new { l.Id, l.NomeOuRazaoSocial })
                .FirstOrDefaultAsync(ct);
            if (existente is not null)
            {
                return new LeadDuplicateWarningDto(existente.Id, existente.NomeOuRazaoSocial, "telefone");
            }
        }

        return null;
    }

    private async Task AplicarTagsAsync(CrmLead lead, List<string>? tags, CancellationToken ct)
    {
        if (tags is null) return;

        var nomes = tags.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var existentes = await db.CrmTags.Where(t => nomes.Contains(t.Nome)).ToListAsync(ct);
        var faltantes = nomes.Where(n => !existentes.Any(e => e.Nome.Equals(n, StringComparison.OrdinalIgnoreCase))).ToList();
        var novas = faltantes.Select(n => new CrmTag { Nome = n }).ToList();
        if (novas.Count > 0)
        {
            db.CrmTags.AddRange(novas);
            existentes.AddRange(novas);
        }

        lead.LeadTags.Clear();
        foreach (var tag in existentes)
        {
            lead.LeadTags.Add(new CrmLeadTag { LeadId = lead.Id, TagId = tag.Id });
        }
    }

    private static LeadDetailDto ParaDetailDto(CrmLead lead) => new(
        lead.Id,
        lead.NomeOuRazaoSocial,
        lead.TipoPessoa,
        DocumentValidation.FormatarDocumento(lead.DocumentoNormalizado),
        lead.Telefone,
        lead.WhatsApp,
        lead.Email,
        lead.DataNascimento,
        lead.Cidade,
        lead.Estado,
        lead.Regional,
        lead.Origem,
        lead.Campanha,
        lead.ProdutoInteresse,
        lead.Gclid,
        lead.UtmMedium,
        lead.UtmSource,
        lead.UtmTerm,
        lead.MetaClickId,
        lead.MetaFormId,
        lead.MetaLeadId,
        lead.IndicadoPorLeadId,
        lead.IndicadoPorLead?.NomeOuRazaoSocial,
        lead.TipoIndicacao,
        lead.EtapaId,
        lead.Etapa?.Nome,
        lead.Etapa?.Cor,
        lead.ResponsavelId,
        lead.Responsavel?.NomeCompleto,
        lead.Observacoes,
        lead.ConsentimentoContato,
        lead.ConsentimentoDataEm,
        lead.ConsentimentoOrigem,
        lead.LeadTags.Select(lt => lt.Tag.Nome).ToList(),
        lead.Oportunidades
            .OrderByDescending(o => o.CriadoEm)
            .Select(o => new LeadOpportunitySummaryDto(o.Id, o.Titulo, o.Etapa.Nome, o.ValorEstimado, o.DataPrevistaFechamento, o.Etapa.Tipo == TipoEtapaPipeline.Aberta))
            .ToList(),
        lead.CriadoEm,
        lead.AtualizadoEm,
        lead.RowVersion,
        lead.Arquivado);
}
