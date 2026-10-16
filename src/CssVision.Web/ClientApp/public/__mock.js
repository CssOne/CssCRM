// TEMPORÁRIO, NÃO COMMITAR: sessão e dados fictícios só para pré-visualizar o visual (carregado por mock.html).
const sessao = { id: "1", email: "a@b.c", nomeCompleto: "Naomi Lima", papeis: ["Admin"], areaInicial: "/app/crm", menu: [
  { chave: "portal", rotulo: "Portal do Consultor", icone: "briefcase", rota: "/app/portal" },
  { chave: "crm-overview", rotulo: "Visão geral", icone: "gauge", rota: "/app/crm" },
  { chave: "crm-leads", rotulo: "Leads", icone: "users", rota: "/app/crm/leads" },
  { chave: "crm-leads-kanban", rotulo: "Quadro de leads", icone: "layout-grid", rota: "/app/crm/leads/kanban" },
  { chave: "crm-pipeline", rotulo: "Pipeline", icone: "kanban-square", rota: "/app/crm/pipeline" },
  { chave: "crm-activities", rotulo: "Atividades", icone: "check-square", rota: "/app/crm/activities" },
  { chave: "crm-agenda", rotulo: "Agenda", icone: "calendar-days", rota: "/app/crm/agenda" },
  { chave: "crm-goals", rotulo: "Metas", icone: "target", rota: "/app/crm/goals" },
  { chave: "crm-relatorio", rotulo: "Relatório comercial", icone: "chart-column", rota: "/app/crm/relatorio-comercial" },
  { chave: "crm-management", rotulo: "Gestão comercial", icone: "users-round", rota: "/app/crm/gestao" },
  { chave: "crm-consultores", rotulo: "Consultores", icone: "id-card", rota: "/app/crm/consultores" },
  { chave: "crm-users", rotulo: "Usuários", icone: "user-cog", rota: "/app/crm/usuarios" },
  { chave: "avisos-pagamento", rotulo: "Avisos de pagamento", icone: "wallet", rota: "/app/crm/avisos-pagamento" },
  { chave: "tv-comercial", rotulo: "Painel da TV", icone: "monitor", rota: "/tv/comercial" },
  { chave: "marketing", rotulo: "Tráfego pago", icone: "megaphone", rota: "/app/marketing" },
  { chave: "admin-backups", rotulo: "Backups", icone: "database", rota: "/app/admin/backups" },
  { chave: "chat", rotulo: "Chat", icone: "messages-square", rota: "/app/chat" },
  { chave: "discord", rotulo: "Discord", icone: "message-circle", rota: "/app/discord" },
  { chave: "suporte", rotulo: "Suporte", icone: "life-buoy", rota: "/app/suporte" } ] };
const nomes = ["Carlos Almeida", "Fernanda Souza", "João Pereira", "Mariana Costa", "Ricardo Lima", "Beatriz Rocha"];
const card = (i, extra = {}) => ({ leadId: "l" + i, nomeOuRazaoSocial: nomes[i % 6], telefone: "11 9" + (1000 + i) + "-" + (2000 + i), email: "lead" + i + "@email.com", estado: "SP", origem: "Tráfego pago", placa: "ABC" + (1234 + i), utilidadeVeiculo: "Carro", tipoIndicacao: i % 3 ? "Lead" : "Indicação", criadoManualmente: false, responsavelNome: nomes[(i + 2) % 6], tags: i % 2 ? ["Quente"] : [], criadoEm: new Date(Date.now() - i * 3600e3 * 7).toISOString(), semContato: i % 4 === 0, arquivado: false, rowVersion: 1, ...extra });
const col = (nome, cor, ordem, n) => ({ etapa: { id: ordem ? "e" + ordem : null, nome, ordem, cor, fechada: false, ativa: true }, total: n, cartoes: Array.from({ length: Math.min(n, 3) }, (_, i) => card(i + ordem)) });
const mes = (i) => "2026-" + String(i + 1).padStart(2, "0");
const orig = window.fetch.bind(window);
window.fetch = async (url, opts) => {
  const u = String(url);
  if (!u.startsWith("/api")) return orig(url, opts);
  const r = (b) => new Response(JSON.stringify(b), { status: 200, headers: { "Content-Type": "application/json" } });
  if (u.includes("/account/session")) return r(sessao);
  if (u.includes("/crm/dashboard")) return r({
    indicadores: { novosLeads: 128, leadsSemContato: 34, contatosHoje: 12, atividadesAtrasadas: 7, oportunidadesAbertas: 56, valorPipeline: 482300, taxaConversao: 0.184, ticketMedio: 9150, vendasGanhasValor: 183400, vendasGanhasQuantidade: 20, vendasGanhasAdesaoValor: 22000, novosLeadsTrafegoSemEtapa: 41 },
    meta: { metaValor: 250000, realizadoValor: 183400, percentualAtingido: 73, metaQuantidade: 30, realizadoQuantidade: 20 },
    funil: [], desempenhoPorVendedor: [], atividadesDoDia: [], leadsParados: [],
    evolucaoVendas: Array.from({ length: 12 }, (_, i) => ({ periodo: mes(i), mes: mes(i), quantidade: 8 + (i * 7) % 13, valor: 40000 + (i * 37000) % 90000 })),
    origemVendas: [{ origem: "Tráfego pago", quantidade: 11, valor: 90000 }, { origem: "Indicação", quantidade: 6, valor: 60000 }, { origem: "Site", quantidade: 3, valor: 33000 }],
    funilLeads: [{ etapa: "Sem etapa", cor: "#94a3b8", quantidade: 41 }, { etapa: "Contato", cor: "#3f63c7", quantidade: 32 }, { etapa: "Cotação", cor: "#f59e0b", quantidade: 18 }, { etapa: "Venda concluída", cor: "#16a34a", quantidade: 20 }],
    resumoMensal: Array.from({ length: 12 }, (_, i) => ({ mes: mes(i), leads: 60 + (i * 11) % 40, perdidos: 10 + (i * 5) % 12, vendas: 8 + (i * 7) % 13, valorGanho: 40000 + (i * 37000) % 90000 })),
    leadsParadosTotal: 0 });
  if (u.includes("/marketing/dashboard")) return r({
    indicadores: { totalLeads: 412, leadsSemEtapa: 58, leadsGanhos: 37, leadsPerdidos: 91, taxaConversao: 0.29, leadsSemContato: 22, leadsEmAndamento: 141, leadsNaoFazemos: 12, leadsSemResponsavel: 9, taxaConversaoGeral: 0.09, valorAdesao: 48200, mensalidadeMedia: 189, tempoMedioPrimeiroContatoHoras: 3.4, totalLeadsPeriodoAnterior: 380, leadsGanhosPeriodoAnterior: 31, mediaLeadsPorDia: 13.7 },
    porOrigem: [], porCampanha: [], origensDisponiveis: ["Meta Ads", "Google Ads"], leads: [], totalLeadsLista: 0,
    evolucao: Array.from({ length: 14 }, (_, i) => ({ data: "2026-10-" + String(i + 1).padStart(2, "0"), total: 10 + (i * 5) % 14, ganhos: 1 + i % 3 })),
    evolucaoPorOQue: [], porOQue: [{ nome: "AGV", totalLeads: 210, ganhos: 20, perdidos: 40, taxaConversao: 0.33, semEtapa: 20 }, { nome: "AGV TRUCK", totalLeads: 130, ganhos: 11, perdidos: 30, taxaConversao: 0.26, semEtapa: 25 }],
    porCanal: [], porEstado: [], porConsultor: [], funil: [], porHorario: [], motivosPerda: [], porConsultorMensal: [], porRegional: [],
    opcoes: { oQue: ["AGV", "AGV ELÉTRICO", "AGV TRUCK"], origens: ["Meta Ads", "Google Ads"], campanhas: ["Black Friday", "Institucional"], canais: ["Facebook", "Instagram"], consultores: [{ id: "c1", nome: "Carlos Almeida" }], estados: ["SP", "MG"], etapas: ["Contato", "Cotação"], regionais: ["Sudeste", "Sul"] },
    periodoInicio: "2026-09-16", periodoFim: "2026-10-16" });
  const usuario = (i, papel) => ({ id: "u" + i, nomeCompleto: nomes[i % 6], email: "usuario" + i + "@cssbrasil.com", telefone: "11 98" + (100 + i) + "-0000", papeis: [papel], regionalId: "r1", regionalNome: "Sudeste", gestorComercialNome: "Naomi Lima", grupoNome: "Grupo A", ativo: i !== 3, limiteMensalLeads: 120, criadoEm: "2026-05-10T12:00:00Z", recebeSomenteOQue: null, limiteDiarioLeads: 10 });
  if (u.includes("/account/profile")) return r({ id: "1", nomeCompleto: "Naomi Lima", email: "naomi@cssbrasil.com", telefone: "11 99999-0000", fotoUrl: null, doisFatoresAtivo: false, papeis: ["Admin"], podeExcluirPropriaConta: false });
  if (/\/crm\/users\/[^/?]+$/.test(u)) return r(usuario(1, "Admin"));
  if (/\/crm\/users(\?|$)/.test(u)) return r({ itens: [usuario(0, "Comercial"), usuario(1, "GestorComercial"), usuario(2, "Comercial"), usuario(3, "Comercial"), usuario(4, "Marketing")], pagina: 1, tamanhoPagina: 20, totalRegistros: 5, totalPaginas: 1 });
  if (u.includes("/crm/management/vendedores")) return r([0, 1, 2, 3, 4].map((i) => ({ id: "u" + i, nome: nomes[i], leadsAtivos: 20 + i * 4, oportunidadesAbertas: 6 + i, limiteMensalLeads: 120, leadsRecebidosNoMes: 40 + i * 9, limiteDiarioLeads: 10, leadsRecebidosHoje: i + 1, ativo: i !== 3, recebeLeads: i % 2 === 0, regionalNome: "Sudeste", leadsTrafegoMesAnterior: 30 + i, leadsTrafegoNoMes: 18 + i })));
  if (u.includes("/crm/management/summary")) return r({ tempoMedioPrimeiroContatoHoras: 3.2, leadsComPrimeiroContato: 120, primeiroContatoPorVendedor: [], tempoMedioPorEtapa: [], oportunidadesSemMovimentacao: [], ranking: [], motivosPerda: [] });
  if (/redistribuicoes|recebe-leads-historico/.test(u)) return r([]);
  if (u.includes("/crm/relatorio-comercial/detalhe")) return r({ titulo: "Leads pegos", total: 5, rotuloData: "Chegada", rotuloValor: "Adesão", itens: [0, 1, 2, 3, 4].map((i) => ({ leadId: "l" + (i % 3), cliente: nomes[i], consultor: nomes[(i + 2) % 6], data: new Date(Date.now() - i * 86400000).toISOString(), etapa: ["Contato", "Cotação", "Venda concluída", "Perdido", "Sem etapa"][i], canal: "Meta Ads", valor: i === 2 ? 450 : null, placa: "ABC" + (1230 + i), telefone: "11 9100" + i + "-2000" })) });
  if (u.includes("/crm/relatorio-comercial")) return r({
    dataInicio: "2026-10-01", dataFim: "2026-10-16",
    totais: { leads: 412, leadsPerdidos: 91, vendas: 37, taxaConversao: 0.29, adesao: 48200, mensalidade: 6990, ticketMensalidade: 189, rastreador: 3100, vistoria: 1850, indicacao: 5200, vendasIndicacao: 8, leadsTrafegoPago: 300, vendasTrafegoPago: 22, taxaConversaoTrafegoPago: 0.07, leadsIndicacaoCard: 60, vendasIndicacaoCard: 8, taxaConversaoIndicacaoCard: 0.13, leadsSemOrigemIdentificada: 10, vendasSemOrigemIdentificada: 1 },
    porMes: Array.from({ length: 6 }, (_, i) => ({ mes: "2026-" + String(i + 5).padStart(2, "0"), leads: 300 + i * 20, leadsPerdidos: 60 + i * 3, vendas: 25 + i * 2, adesao: 30000 + i * 2500, mensalidade: 5000 + i * 300, rastreador: 2000, vistoria: 1200, indicacao: 3000 })),
    porSemana: Array.from({ length: 4 }, (_, i) => ({ inicio: "2026-10-" + String(1 + i * 7).padStart(2, "0"), leads: 80 + i * 9, leadsPerdidos: 18, vendas: 7 + i, adesao: 9000 + i * 800 })),
    porOrigem: [{ origem: "Meta Ads", leads: 200, vendas: 15, adesao: 21000 }, { origem: "Google Ads", leads: 100, vendas: 7, adesao: 9000 }],
    porVendedor: [0, 1, 2, 3].map((i) => ({ vendedorId: "u" + i, vendedor: nomes[i], leads: 90 - i * 8, vendas: 12 - i * 2, adesao: 15000 - i * 2000, mensalidade: 2200 - i * 300, taxaConversao: 0.3 - i * 0.04 })),
    porEstado: [{ estado: "SP", leads: 180, vendas: 15 }, { estado: "MG", leads: 90, vendas: 8 }],
    faixasFipe: [], porProduto: [], marketingNotion: [],
    porEtapa: [{ etapaId: null, etapa: "Sem etapa", leads: 58 }, { etapaId: "e1", etapa: "Contato", leads: 141 }] });
  const min = (m) => new Date(Date.now() - m * 60000).toISOString();
  const msg = (id, autor, texto, m, doCrm) => ({ id: "10000000000000000" + id, autorNome: autor, autorFotoUrl: null, conteudo: texto, criadaEm: min(m), anexos: [], doCrm: !!doCrm, reacoes: id === 2 ? [{ chave: "👍", texto: "👍", contagem: 3, reagi: true }] : [] });
  if (u.includes("/crm/discord/chat/canais") && u.includes("/mensagens")) return r({ temMais: false, conteudoOculto: false, mensagens: [msg(1, "Carlos Almeida", "Bom dia, pessoal! Alguém pegou o lead da placa ABC1234?", 62), msg(2, "Fernanda Souza", "Peguei agora, já estou ligando.", 58), msg(3, "Naomi Lima", "Ótimo. Lembrem de registrar o contato no CRM.", 40, true), msg(4, "João Pereira", "Fechei a venda do Hilux, adesão paga hoje 🎉", 12)] });
  if (u.includes("/crm/discord/chat/canais") && u.includes("/online")) return r({ pessoas: [{ id: "u1", nome: "Fernanda Souza" }, { id: "u2", nome: "João Pereira" }] });
  if (u.includes("/crm/discord/chat/canais") && u.includes("/topicos")) return r([]);
  if (u.includes("/crm/discord/chat/canais")) return r([{ chave: "gestao", nome: "Gestão", tipo: "grupo" }, { chave: "sudeste", nome: "Sudeste", tipo: "grupo" }, { chave: "dm1", nome: "Fernanda Souza", tipo: "direta", online: true }, { chave: "dm2", nome: "Carlos Almeida", tipo: "direta", online: false }]);
  if (u.includes("/crm/discord/chat/nao-lidas")) return r({ total: 3, porConversa: { dm1: 2, sudeste: 1 } });
  if (u.includes("/crm/discord/chat/extras")) return r({ emojis: [], figurinhas: [] });
  if (u.includes("/crm/discord/chat/contatos")) return r([]);
  if (u.includes("/crm/tv/ranking")) return r({ ranking: [] });
  if (/announcements|avisos-pagamento/.test(u)) return r([]);
  const dia = (d) => new Date(Date.now() + d * 86400000).toISOString();
  if (u.includes("/crm/pipeline")) return r({ colunas: [
    { etapa: { id: "p1", nome: "Qualificação", ordem: 1, tipo: 1, cor: "#467fc5", ativa: true }, valorTotal: 28400, cartoes: [0, 1, 2].map((i) => ({ opportunityId: "o" + i, leadId: "l" + i, leadNome: nomes[i], titulo: "Proteção veicular", produtoOuServico: "AGV", valorEstimado: 9000 + i * 400, responsavelId: "u1", responsavelNome: nomes[i + 1], origem: "Meta Ads", proximaAtividadeEm: dia(i), proximaAtividadeAssunto: "Ligar", etapaDesde: dia(-i - 1), atrasada: i === 2, rowVersion: 1 })) },
    { etapa: { id: "p2", nome: "Proposta", ordem: 2, tipo: 1, cor: "#f59e0b", ativa: true }, valorTotal: 18200, cartoes: [3, 4].map((i) => ({ opportunityId: "o" + i, leadId: "l" + i, leadNome: nomes[i], titulo: "Proteção veicular", produtoOuServico: "AGV TRUCK", valorEstimado: 9100, responsavelId: "u2", responsavelNome: nomes[i - 2], origem: "Indicação", proximaAtividadeEm: null, proximaAtividadeAssunto: null, etapaDesde: dia(-3), atrasada: false, rowVersion: 1 })) },
    { etapa: { id: "p3", nome: "Ganho", ordem: 3, tipo: 2, cor: "#16a34a", ativa: true }, valorTotal: 0, cartoes: [] },
    { etapa: { id: "p4", nome: "Perdido", ordem: 4, tipo: 3, cor: "#ef4444", ativa: true }, valorTotal: 0, cartoes: [] }] });
  if (u.includes("/crm/goals/regionais")) return r([{ id: "g1", regionalId: "r1", regionalNome: "Sudeste", mesReferencia: "2026-10-01", metaQuantidadeVendas: 80, metaValor: 600000, realizadoValor: 410000, realizadoQuantidade: 52 }]);
  if (/\/crm\/goals(\?|$)/.test(u)) return r([0, 1, 2, 3].map((i) => ({ id: "g" + i, vendedorId: "u" + i, vendedorNome: nomes[i], mesReferencia: "2026-10-01", metaQuantidadeVendas: 20, metaValor: 150000, realizadoValor: 130000 - i * 25000, realizadoQuantidade: 18 - i * 4 })));
  if (u.includes("/admin/backups")) return r([0, 1, 2].map((i) => ({ id: "b" + i, status: i === 2 ? "Falhou" : "Concluido", origem: i ? "Automatico" : "Manual", criadoEm: dia(-i), concluidoEm: dia(-i), tamanhoBytes: 48000000 + i * 1200000, nomeArquivo: "backup-" + i + ".zip", leads: 4120 - i * 30, oportunidades: 560, usuarios: 38, erro: i === 2 ? "Tempo esgotado ao enviar para o armazenamento." : null })));
  if (u.includes("/crm/suporte")) return r({ atende: true, abertos: 2, chamados: [0, 1, 2].map((i) => ({ id: "s" + i, assunto: ["Não consigo anexar comprovante", "Dúvida sobre meta do mês", "Sugestão para o quadro"][i], categoria: ["Problema", "Dúvida", "Sugestão"][i], status: ["Aberto", "EmAtendimento", "Resolvido"][i], solicitanteId: "u" + i, solicitanteNome: nomes[i], solicitanteEmail: "usuario" + i + "@cssbrasil.com", criadoEm: dia(-i - 1), ultimaMensagemEm: dia(-i), mensagens: 2 + i })) });
  if (u.includes("/crm/discord/status")) return r({ configurado: true, vinculado: true, discordNome: "naomi#0001", avisosAtivos: true, noServidor: true, vinculadoEm: dia(-12) });
  if (u.includes("/crm/discord/grupos/avisos")) return r({ venda: true, leadNovo: false, perdido: false });
  if (u.includes("/crm/discord/grupos")) return r([{ chave: "gestao", nome: "Gestão", ativo: true }, { chave: "sudeste", nome: "Sudeste", ativo: true }, { chave: "extra1", nome: "Avisos gerais", ativo: false, extra: true }]);
  if (u.includes("/crm/discord/membros")) return r({ total: 3, vinculados: 2, semVinculo: 1, bots: 0, foraDoServidor: [], itens: [0, 1, 2].map((i) => ({ id: "d" + i, nome: nomes[i], usuario: "usuario" + i, avatarUrl: null, bot: false, entrouEm: dia(-30 - i), cargos: ["Sudeste", "Comercial"], vinculo: i < 2 ? { usuarioId: "u" + i, nome: nomes[i], email: "usuario" + i + "@cssbrasil.com" } : null })) });
  if (/\/crm\/leads\/l\d+\/timeline/.test(u)) return r([1, 2, 3].map((i) => ({ id: "t" + i, tipo: i, titulo: ["Lead criado", "Contato realizado por Fernanda Souza", "Etapa alterada para Cotação"][i - 1], descricao: i === 2 ? "Cliente pediu cotação para a Hilux." : null, usuarioNome: nomes[i], ocorridoEm: new Date(Date.now() - i * 86400000).toISOString() })));
  if (/\/crm\/leads\/l\d+(\?|$)/.test(u)) return r({ id: "l1", nomeOuRazaoSocial: "Fernanda Souza", tipoPessoa: 1, documento: "12345678909", telefone: "11991001001", telefone2: null, whatsApp: "11991001001", email: "fernanda@email.com", dataNascimento: "1988-04-12", cidade: "São Paulo", estado: "SP", regional: "Sudeste", origem: "Meta Ads", campanha: "Black Friday", produtoInteresse: "AGV", placa: "ABC1235", temSeguro: false, utilidadeVeiculo: "Carro", tipoIndicacao: "Lead", criadoManualmente: false, etapaId: "e2", etapaNome: "Cotação", etapaCor: "#f59e0b", responsavelId: "u1", responsavelNome: "João Pereira", observacoes: "Prefere contato por WhatsApp à tarde.", consentimentoContato: true, consentimentoDataEm: new Date().toISOString(), consentimentoOrigem: "Formulário", tags: ["Quente", "Hilux"], oportunidades: [{ id: "o1", titulo: "Proteção veicular", etapaNome: "Proposta", etapaTipo: 1, valorEstimado: 9100, dataPrevistaFechamento: "2026-10-30", ativa: true, migracao: false, indicacao: false }], criadoEm: new Date(Date.now() - 3 * 86400000).toISOString(), atualizadoEm: new Date().toISOString(), rowVersion: 1, arquivado: false, valorAdesao: 450 });
  if (u.includes("/crm/leads/kanban")) return r({ colunas: [col("Sem etapa", "#94a3b8", 0, 41), col("Em contato", "#3f63c7", 1, 32), col("Cotação", "#f59e0b", 2, 18), col("Venda concluída", "#16a34a", 3, 20), col("Perdido", "#ef4444", 4, 9)] });
  if (/settings|lixeira|tags|regionais|groups/.test(u)) return r([]);
  return r({ itens: [], items: [], total: 0, pagina: 1, totalPaginas: 1, totalRegistros: 0, naoLidas: 0 });
};
