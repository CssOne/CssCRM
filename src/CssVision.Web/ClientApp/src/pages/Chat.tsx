import { BarChart3, ChevronUp, CornerUpLeft, Hash, MessageSquarePlus, MessagesSquare, Paperclip, Pencil, Phone, Pin, Plus, Search, Send, Smile, SmilePlus, Trash2, X } from "lucide-react";
import { useCallback, useEffect, useLayoutEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { useAuth } from "../context/AuthContext";
import { api, ApiRequestError, isAbortError, uploadFile } from "../lib/api";
import { desligarNotificacao, definirSom, ligarNotificacao, notificacaoLigada, notificacaoSuportada, somLigado } from "../lib/avisosDoChat";
import { formatarDataHora } from "../lib/format";
import { rotuloNaoLidas, useChatNaoLidas } from "../lib/useChatNaoLidas";
import type { DiscordChatCanal, DiscordChatChamada, DiscordChatContato, DiscordChatExtras, DiscordChatMensagem, DiscordChatMensagens, DiscordChatOnline, DiscordChatReacao, DiscordChatTopico } from "../lib/types";
import { SeletorDeEmojis } from "../components/chat/SeletorDeEmojis";
import { NovaEnquete, NovoTopico } from "../components/chat/NovoTopicoEEnquete";
import { PainelDeMensagens, ReacoesDaMensagem, ReacoesRapidas } from "../components/chat/ReacoesEPaineis";
import { Avatar, Badge, Button, Card, Checkbox, ConfirmDialog, EmptyState, ErrorState, Input, Modal, Skeleton, useToast } from "../components/ui";

const LIMITE_TEXTO = 2000;
const LIMITE_ARQUIVO = 10 * 1024 * 1024;
const TIPOS_DE_ARQUIVO = ".png,.jpg,.jpeg,.gif,.webp,.pdf,.doc,.docx,.xls,.xlsx,.ppt,.pptx,.txt,.csv,.zip";
const INTERVALO_MS = 4000;

/**
 * Chat de texto: grupos da empresa e conversas 1:1. As conversas ficam no Discord (um canal por grupo, uma thread privada por conversa 1:1) e aparecem aqui, com o nome e a foto de cada pessoa;
 * o que se escreve aqui chega ao Discord (inclusive no celular de quem usa o app). Quem pode abrir cada grupo é decidido pelo CRM.
 * Sem tempo real de verdade: a lista se atualiza sozinha a cada poucos segundos enquanto a aba está aberta.
 */
export function ChatPage() {
  const { sessao } = useAuth();
  const { notificar } = useToast();
  const [canais, setCanais] = useState<DiscordChatCanal[] | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [chave, setChave] = useState<string | null>(null);
  const [dados, setDados] = useState<DiscordChatMensagens | null>(null);
  const [erroMensagens, setErroMensagens] = useState<string | null>(null);
  const [texto, setTexto] = useState("");
  const [enviando, setEnviando] = useState(false);
  const [carregandoAntigas, setCarregandoAntigas] = useState(false);
  const [novaConversa, setNovaConversa] = useState(false);
  const [ligando, setLigando] = useState(false);
  const [arquivo, setArquivo] = useState<File | null>(null);
  const [respondendo, setRespondendo] = useState<DiscordChatMensagem | null>(null);
  const [mencoes, setMencoes] = useState<{ id: string; nome: string }[]>([]);
  const [mencaoAtiva, setMencaoAtiva] = useState<{ inicio: number; fim: number; termo: string } | null>(null);
  const [sugestoes, setSugestoes] = useState<DiscordChatContato[]>([]);
  const [editando, setEditando] = useState<{ id: string; texto: string } | null>(null);
  const [salvandoEdicao, setSalvandoEdicao] = useState(false);
  const [apagando, setApagando] = useState<DiscordChatMensagem | null>(null);
  const [apagandoEmAndamento, setApagandoEmAndamento] = useState(false);
  const campoDeTexto = useRef<HTMLTextAreaElement>(null);
  const [painel, setPainel] = useState<null | "busca" | "fixadas">(null);
  const [termoDeBusca, setTermoDeBusca] = useState("");
  const [resultados, setResultados] = useState<DiscordChatMensagem[] | null>(null);
  const [carregandoPainel, setCarregandoPainel] = useState(false);
  const [topicos, setTopicos] = useState<DiscordChatTopico[]>([]);
  const [topicosAbertos, setTopicosAbertos] = useState(false);
  const [versaoDosTopicos, setVersaoDosTopicos] = useState(0);
  const [topicoAtual, setTopicoAtual] = useState<{ chave: string; nome: string; grupo: string } | null>(null);
  const [menuMaisAberto, setMenuMaisAberto] = useState(false);
  const [topicoAberto, setTopicoAberto] = useState(false);
  const [enqueteAberta, setEnqueteAberta] = useState(false);
  const [seletorAberto, setSeletorAberto] = useState(false);
  const [extras, setExtras] = useState<DiscordChatExtras | null>(null);
  const [carregandoExtras, setCarregandoExtras] = useState(false);
  const seletorDeArquivo = useRef<HTMLInputElement>(null);
  const [online, setOnline] = useState<DiscordChatOnline["pessoas"]>([]);
  const [linkDaChamada, setLinkDaChamada] = useState<string | null>(null);
  const rolagem = useRef<HTMLDivElement>(null);
  const colarNoFim = useRef(true);
  const chaveAtual = useRef<string | null>(null);
  const naoLidas = useChatNaoLidas(true, 8000);
  const { atualizar: atualizarNaoLidas } = naoLidas;

  // A conversa aberta é marcada como lida pelo servidor ao carregar: logo depois, atualiza as marcas da lista.
  useEffect(() => {
    if (!chave) return;
    const t = window.setTimeout(atualizarNaoLidas, 1500);
    return () => window.clearTimeout(t);
  }, [chave, atualizarNaoLidas]);

  useEffect(() => {
    const controller = new AbortController();
    const carregar = (primeira: boolean) =>
      api
        .get<DiscordChatCanal[]>("/crm/discord/chat/canais", controller.signal)
        .then((lista) => {
          // Nas atualizações seguintes só renova o "online" (mantém as conversas que a pessoa acabou de abrir nesta tela).
          setCanais((atual) => (primeira || !atual ? lista : [...lista, ...atual.filter((c) => !lista.some((l) => l.chave === c.chave))]));
          setChave((atual) => atual ?? lista[0]?.chave ?? null);
        })
        .catch((e) => {
          if (primeira && !isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar as conversas.");
        });
    void carregar(true);
    const timer = window.setInterval(() => {
      if (document.visibilityState === "visible") void carregar(false);
    }, 30000);
    return () => {
      controller.abort();
      window.clearInterval(timer);
    };
  }, []);

  const carregarMensagens = useCallback((qual: string, sinal?: AbortSignal) => {
    api
      .get<DiscordChatMensagens>(`/crm/discord/chat/canais/${encodeURIComponent(qual)}/mensagens`, sinal)
      .then((r) => {
        if (chaveAtual.current !== qual) return; // trocou de conversa no meio da consulta
        setErroMensagens(null);
        // Mantém as mensagens antigas já carregadas ("Carregar anteriores") e atualiza só o trecho recente.
        setDados((atual) => {
          if (!atual || atual.mensagens.length === 0) return r;
          const primeiroRecente = r.mensagens[0]?.id;
          const antigas = primeiroRecente ? atual.mensagens.filter((m) => BigInt(m.id) < BigInt(primeiroRecente)) : [];
          return { ...r, mensagens: [...antigas, ...r.mensagens], temMais: antigas.length > 0 ? atual.temMais : r.temMais };
        });
      })
      .catch((e) => {
        if (!isAbortError(e) && chaveAtual.current === qual) setErroMensagens(e instanceof Error ? e.message : "Não foi possível carregar as mensagens.");
      });
  }, []);

  // Troca de conversa: limpa e carrega; depois atualiza a cada poucos segundos enquanto a aba está visível.
  useEffect(() => {
    chaveAtual.current = chave;
    setDados(null);
    setErroMensagens(null);
    colarNoFim.current = true;
    if (!chave) return;
    const controller = new AbortController();
    carregarMensagens(chave, controller.signal);
    const timer = window.setInterval(() => {
      if (document.visibilityState === "visible") carregarMensagens(chave);
    }, INTERVALO_MS);
    return () => {
      controller.abort();
      window.clearInterval(timer);
    };
  }, [chave, carregarMensagens]);

  // Rola para o fim quando chega mensagem nova, mas só se a pessoa já estava lendo o fim (não puxa de volta quem subiu para ler).
  const ultimaId = dados?.mensagens.at(-1)?.id;
  useLayoutEffect(() => {
    const el = rolagem.current;
    if (el && colarNoFim.current) el.scrollTop = el.scrollHeight;
  }, [ultimaId]);

  function aoRolar() {
    const el = rolagem.current;
    if (el) colarNoFim.current = el.scrollHeight - el.scrollTop - el.clientHeight < 80;
  }

  async function carregarAnteriores() {
    if (!chave || !dados || dados.mensagens.length === 0) return;
    setCarregandoAntigas(true);
    const el = rolagem.current;
    const alturaAntes = el?.scrollHeight ?? 0;
    try {
      const r = await api.get<DiscordChatMensagens>(
        `/crm/discord/chat/canais/${encodeURIComponent(chave)}/mensagens?antes=${encodeURIComponent(dados.mensagens[0].id)}`
      );
      colarNoFim.current = false;
      setDados((atual) => atual && { ...atual, mensagens: [...r.mensagens, ...atual.mensagens], temMais: r.temMais });
      requestAnimationFrame(() => {
        if (el) el.scrollTop = el.scrollHeight - alturaAntes; // mantém a leitura no mesmo ponto
      });
    } catch (e) {
      notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível carregar as mensagens anteriores.");
    } finally {
      setCarregandoAntigas(false);
    }
  }

  // Abre o seletor; emojis e figurinhas do servidor só são buscados na primeira vez.
  function alternarSeletor() {
    const abrir = !seletorAberto;
    setSeletorAberto(abrir);
    if (abrir && !extras && !carregandoExtras) {
      setCarregandoExtras(true);
      api
        .get<DiscordChatExtras>("/crm/discord/chat/extras")
        .then(setExtras)
        .catch(() => setExtras({ emojis: [], figurinhas: [] }))
        .finally(() => setCarregandoExtras(false));
    }
  }

  // Escreve o emoji onde está o cursor (ou no fim) sem mexer no resto do texto.
  function inserirNoTexto(trecho: string) {
    const campo = campoDeTexto.current;
    const inicio = campo?.selectionStart ?? texto.length;
    const fim = campo?.selectionEnd ?? texto.length;
    const novo = (texto.slice(0, inicio) + trecho + texto.slice(fim)).slice(0, LIMITE_TEXTO);
    setTexto(novo);
    requestAnimationFrame(() => {
      campo?.focus();
      const posicao = Math.min(inicio + trecho.length, novo.length);
      campo?.setSelectionRange(posicao, posicao);
    });
  }

  const podeFixar = !!sessao?.papeis.some((p) => ["Admin", "GestorMaster", "SupervisorComercial", "GestorComercial"].includes(p));
  const emTopico = chave?.startsWith("topico:") ?? false;
  const grupoDeBase = topicoAtual?.grupo ?? chave;

  // Trocou de conversa pela lista: fecha busca/fixadas e esquece o tópico aberto (o tópico guarda o grupo a que pertence).
  useEffect(() => {
    setPainel(null);
    setTermoDeBusca("");
    setResultados(null);
    setTopicosAbertos(false);
    if (!chave?.startsWith("topico:")) setTopicoAtual(null);
  }, [chave]);

  // Tópicos do grupo aberto (atualiza a cada 30 s com a aba visível).
  useEffect(() => {
    setTopicos([]);
    const grupo = topicoAtual?.grupo ?? chave;
    if (!grupo || grupo.startsWith("dm:") || grupo.startsWith("topico:")) return;
    const controller = new AbortController();
    const carregar = () =>
      api
        .get<DiscordChatTopico[]>(`/crm/discord/chat/canais/${encodeURIComponent(grupo)}/topicos`, controller.signal)
        .then(setTopicos)
        .catch(() => undefined);
    void carregar();
    const timer = window.setInterval(() => {
      if (document.visibilityState === "visible") void carregar();
    }, 30000);
    return () => {
      controller.abort();
      window.clearInterval(timer);
    };
  }, [chave, topicoAtual?.grupo, versaoDosTopicos]);

  function abrirTopico(t: DiscordChatTopico) {
    setTopicosAbertos(false);
    setTopicoAtual({ chave: t.chave, nome: t.nome, grupo: grupoDeBase ?? "" });
    setChave(t.chave);
  }

  async function reagir(mensagemId: string, emoji: string) {
    if (!chave) return;
    try {
      const reacoes = await api.post<DiscordChatReacao[]>(`/crm/discord/chat/canais/${encodeURIComponent(chave)}/mensagens/${mensagemId}/reacoes`, { emoji });
      setDados((atual) => atual && { ...atual, mensagens: atual.mensagens.map((m) => (m.id === mensagemId ? { ...m, reacoes } : m)) });
      setResultados((atual) => atual && atual.map((m) => (m.id === mensagemId ? { ...m, reacoes } : m)));
    } catch (e) {
      notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível reagir à mensagem.");
    }
  }

  async function alternarFixada(m: DiscordChatMensagem) {
    if (!chave) return;
    const fixar = !m.fixada;
    try {
      await api.put(`/crm/discord/chat/canais/${encodeURIComponent(chave)}/mensagens/${m.id}/fixada`, { fixar });
      setDados((atual) => atual && { ...atual, mensagens: atual.mensagens.map((x) => (x.id === m.id ? { ...x, fixada: fixar } : x)) });
      if (painel === "fixadas") void abrirPainel("fixadas");
      notificar("success", fixar ? "Mensagem fixada." : "Mensagem desafixada.");
    } catch (e) {
      notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível fixar a mensagem.");
    }
  }

  async function abrirPainel(qual: "busca" | "fixadas") {
    if (!chave) return;
    setPainel(qual);
    if (qual === "fixadas") {
      setCarregandoPainel(true);
      try {
        setResultados(await api.get<DiscordChatMensagem[]>(`/crm/discord/chat/canais/${encodeURIComponent(chave)}/fixadas`));
      } catch (e) {
        setResultados([]);
        notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível carregar as mensagens fixadas.");
      } finally {
        setCarregandoPainel(false);
      }
    } else {
      setResultados(null);
    }
  }

  async function buscar() {
    const termo = termoDeBusca.trim();
    if (!chave || termo.length < 2) return;
    setCarregandoPainel(true);
    try {
      setResultados(await api.get<DiscordChatMensagem[]>(`/crm/discord/chat/canais/${encodeURIComponent(chave)}/busca?termo=${encodeURIComponent(termo)}`));
    } catch (e) {
      setResultados([]);
      notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível buscar.");
    } finally {
      setCarregandoPainel(false);
    }
  }

  // Mensagem que o servidor devolveu (tópico, enquete): entra no fim da conversa sem esperar a próxima atualização.
  function acrescentarMensagem(nova: DiscordChatMensagem) {
    colarNoFim.current = true;
    setDados((atual) => ({ mensagens: [...(atual?.mensagens ?? []), nova], temMais: atual?.temMais ?? false, conteudoOculto: atual?.conteudoOculto ?? false }));
  }

  async function enviarFigurinha(figurinhaId: string) {
    if (!chave || enviando) return;
    setSeletorAberto(false);
    setEnviando(true);
    try {
      const nova = await api.post<DiscordChatMensagem>(`/crm/discord/chat/canais/${encodeURIComponent(chave)}/figurinhas`, { figurinhaId });
      colarNoFim.current = true;
      setDados((atual) => ({ mensagens: [...(atual?.mensagens ?? []), nova], temMais: atual?.temMais ?? false, conteudoOculto: atual?.conteudoOculto ?? false }));
    } catch (e) {
      notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível enviar a figurinha.");
    } finally {
      setEnviando(false);
    }
  }

  async function enviar() {
    const conteudo = texto.trim();
    if (!chave || (!conteudo && !arquivo) || enviando) return;
    setEnviando(true);
    try {
      // Resposta = citação no começo ("> Fulano: trecho"): o Discord não deixa o CRM responder "de verdade" por webhook, mas a citação aparece nos dois.
      const citacao = respondendo ? `> ${respondendo.autorNome}: ${trechoParaCitar(respondendo.conteudo)}\n` : "";
      const marcadas = mencoes.filter((m) => conteudo.includes(`@${m.nome}`)).map((m) => m.id);
      const nova = arquivo
        ? await uploadFile<DiscordChatMensagem>(`/crm/discord/chat/canais/${encodeURIComponent(chave)}/anexos`, arquivo, conteudo ? { texto: citacao + conteudo } : {})
        : await api.post<DiscordChatMensagem>(`/crm/discord/chat/canais/${encodeURIComponent(chave)}/mensagens`, { texto: citacao + conteudo, mencoes: marcadas });
      setTexto("");
      setArquivo(null);
      setRespondendo(null);
      setMencoes([]);
      setMencaoAtiva(null);
      colarNoFim.current = true;
      setDados((atual) => ({ mensagens: [...(atual?.mensagens ?? []), nova], temMais: atual?.temMais ?? false, conteudoOculto: atual?.conteudoOculto ?? false }));
    } catch (e) {
      notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível enviar a mensagem.");
    } finally {
      setEnviando(false);
    }
  }

  // Quem está online na conversa aberta (atualiza a cada 20 s, só com a aba visível).
  useEffect(() => {
    setOnline([]);
    if (!chave) return;
    const controller = new AbortController();
    const carregar = () =>
      api
        .get<DiscordChatOnline>(`/crm/discord/chat/canais/${encodeURIComponent(chave)}/online`, controller.signal)
        .then((r) => setOnline(r.pessoas))
        .catch(() => undefined);
    void carregar();
    const timer = window.setInterval(() => {
      if (document.visibilityState === "visible") void carregar();
    }, 20000);
    return () => {
      controller.abort();
      window.clearInterval(timer);
    };
  }, [chave]);

  // Troca de conversa: o aviso de "abrir a chamada manualmente" e o arquivo escolhido eram da conversa anterior.
  useEffect(() => {
    setLinkDaChamada(null);
    setArquivo(null);
    setRespondendo(null);
    setMencoes([]);
    setMencaoAtiva(null);
    setEditando(null);
  }, [chave]);

  // Lista de menção: busca as pessoas pelo que foi digitado depois do "@" (só quem pode receber o aviso: vinculou o Discord).
  const termoDaMencao = mencaoAtiva?.termo;
  useEffect(() => {
    if (termoDaMencao === undefined) {
      setSugestoes([]);
      return;
    }
    const controller = new AbortController();
    const timer = window.setTimeout(() => {
      const consulta = termoDaMencao ? `?busca=${encodeURIComponent(termoDaMencao)}` : "";
      api
        .get<DiscordChatContato[]>(`/crm/discord/chat/contatos${consulta}`, controller.signal)
        .then((lista) => setSugestoes(lista.slice(0, 5)))
        .catch(() => undefined);
    }, 200);
    return () => {
      window.clearTimeout(timer);
      controller.abort();
    };
  }, [termoDaMencao]);

  function aoDigitar(valor: string, cursor: number) {
    setTexto(valor);
    // "@" no começo ou depois de espaço, seguido de até 30 letras sem espaço, logo antes do cursor.
    const m = /(^|\s)@([^\s@]{0,30})$/.exec(valor.slice(0, cursor));
    setMencaoAtiva(m ? { inicio: cursor - m[2].length - 1, fim: cursor, termo: m[2] } : null);
  }

  function escolherMencao(pessoa: DiscordChatContato) {
    if (!mencaoAtiva) return;
    const novo = `${texto.slice(0, mencaoAtiva.inicio)}@${pessoa.nome} ${texto.slice(mencaoAtiva.fim)}`;
    setTexto(novo);
    setMencoes((atual) => (atual.some((m) => m.id === pessoa.id) ? atual : [...atual, { id: pessoa.id, nome: pessoa.nome }]));
    setMencaoAtiva(null);
    window.setTimeout(() => campoDeTexto.current?.focus(), 0);
  }

  async function salvarEdicao() {
    if (!chave || !editando || salvandoEdicao) return;
    const novoTexto = editando.texto.trim();
    if (!novoTexto) return;
    setSalvandoEdicao(true);
    try {
      await api.put(`/crm/discord/chat/canais/${encodeURIComponent(chave)}/mensagens/${editando.id}`, { texto: novoTexto });
      setDados((atual) => atual && { ...atual, mensagens: atual.mensagens.map((m) => (m.id === editando.id ? { ...m, conteudo: novoTexto, editada: true } : m)) });
      setEditando(null);
    } catch (e) {
      notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível editar a mensagem.");
    } finally {
      setSalvandoEdicao(false);
    }
  }

  async function apagarMensagem() {
    if (!chave || !apagando) return;
    setApagandoEmAndamento(true);
    try {
      await api.del(`/crm/discord/chat/canais/${encodeURIComponent(chave)}/mensagens/${apagando.id}`);
      setDados((atual) => atual && { ...atual, mensagens: atual.mensagens.filter((m) => m.id !== apagando.id) });
      setApagando(null);
    } catch (e) {
      notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível apagar a mensagem.");
    } finally {
      setApagandoEmAndamento(false);
    }
  }

  /**
   * O Discord não deixa embutir a chamada: pede o canal de voz da conversa (o servidor também avisa a conversa de que você está numa chamada)
   * e abre o Discord numa nova aba. Se o navegador bloquear a aba, o link fica na tela para clicar.
   */
  async function ligar() {
    if (!chave || ligando) return;
    setLigando(true);
    setLinkDaChamada(null);
    try {
      const r = await api.post<DiscordChatChamada>(`/crm/discord/chat/canais/${encodeURIComponent(chave)}/chamada`);
      colarNoFim.current = true;
      carregarMensagens(chave); // mostra o aviso que o CRM acabou de publicar
      if (window.open(r.url, "_blank", "noopener,noreferrer")) {
        notificar("info", "Abrindo o Discord. Entre no canal de voz para falar.");
      } else {
        setLinkDaChamada(r.url);
      }
    } catch (e) {
      notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível iniciar a chamada.");
    } finally {
      setLigando(false);
    }
  }

  function aoAbrirConversa(conversa: DiscordChatCanal) {
    setNovaConversa(false);
    setCanais((atual) => (atual?.some((c) => c.chave === conversa.chave) ? atual : [...(atual ?? []), conversa]));
    setChave(conversa.chave);
  }

  const atual = canais?.find((c) => c.chave === chave);
  const nomeDoGrupo = atual?.nome;
  const grupos = canais?.filter((c) => c.tipo !== "direta") ?? [];
  const diretas = canais?.filter((c) => c.tipo === "direta") ?? [];

  return (
    <div className="mx-auto max-w-5xl space-y-4">
      <div>
        <h1 className="flex items-center gap-2 text-xl font-bold text-[var(--fg)]">
          <MessagesSquare className="size-5 text-[var(--brand)]" aria-hidden /> Chat
        </h1>
        <p className="text-sm text-[var(--fg-muted)]">Conversa com os grupos da empresa. As mensagens também aparecem no Discord, inclusive no celular.</p>
        <PreferenciasDeAviso />
      </div>

      {erro ? (
        <ErrorState message={erro} onRetry={() => window.location.reload()} />
      ) : !canais ? (
        <Skeleton className="h-96" />
      ) : canais.length === 0 ? (
        <EmptyState
          title="Nenhuma conversa disponível ainda"
          description="Os grupos ainda não foram criados no Discord, ou a integração não foi ativada. Peça a um administrador para sincronizar os grupos na página do Discord."
          action={
            <Link to="/app/discord" className="text-sm font-medium text-[var(--brand)] hover:underline">
              Ir para o Discord
            </Link>
          }
        />
      ) : (
        <div className="grid gap-4 md:grid-cols-[14rem_1fr]">
          <nav aria-label="Conversas" className="flex gap-2 overflow-x-auto md:flex-col md:overflow-visible">
            {grupos.map((c) => (
              <ItemDaLista key={c.chave} conversa={c} ativa={c.chave === chave} naoLidas={naoLidas.porConversa[c.chave] ?? 0} aoEscolher={() => setChave(c.chave)} />
            ))}
            <div className="flex shrink-0 items-center gap-2 md:mt-2 md:justify-between">
              <span className="hidden text-xs font-semibold uppercase tracking-wide text-[var(--fg-muted)] md:inline">Conversas diretas</span>
              <Button variant="ghost" size="sm" onClick={() => setNovaConversa(true)} aria-label="Nova conversa">
                <Plus className="size-4" /> <span className="md:hidden">Nova conversa</span>
              </Button>
            </div>
            {diretas.map((c) => (
              <ItemDaLista key={c.chave} conversa={c} ativa={c.chave === chave} naoLidas={naoLidas.porConversa[c.chave] ?? 0} aoEscolher={() => setChave(c.chave)} />
            ))}
          </nav>

          <Card className="flex h-[calc(100dvh-15rem)] min-h-[22rem] flex-col p-0">
            <div className="flex items-center justify-between border-b border-[var(--border)] px-4 py-2">
              <p className="flex items-center gap-1.5 text-sm font-semibold text-[var(--fg)]">
                {atual?.tipo === "direta" ? (
                  <Avatar nome={atual.nome} fotoUrl={atual.fotoUrl} className="size-6 text-[10px]" />
                ) : (
                  <Hash className="size-4 text-[var(--fg-muted)]" aria-hidden />
                )}{" "}
                {emTopico ? (
                  <>
                    <button type="button" onClick={() => setChave(topicoAtual?.grupo ?? null)} className="text-[var(--fg-muted)] hover:underline" title="Voltar ao grupo">
                      {canais?.find((c) => c.chave === topicoAtual?.grupo)?.nome ?? "Grupo"}
                    </button>
                    <span aria-hidden className="text-[var(--fg-muted)]">›</span> 🧵 {topicoAtual?.nome}
                  </>
                ) : (
                  nomeDoGrupo
                )}
              </p>
              <div className="flex items-center gap-3">
              {online.length > 0 && (
                <span
                  className="flex items-center gap-1.5 text-xs text-[var(--fg-muted)]"
                  title={online.map((p) => p.nome).join(", ")}
                  aria-label={`Online agora: ${online.map((p) => p.nome).join(", ")}`}
                >
                  <span className="size-2 rounded-full bg-[var(--success)]" aria-hidden />
                  {atual?.tipo === "direta" ? "online" : `${online.length} online`}
                </span>
              )}
              <div className="relative">
                {topicos.length > 0 && !emTopico && (
                  <Button variant="ghost" size="sm" onClick={() => setTopicosAbertos((v) => !v)} aria-expanded={topicosAbertos} title="Tópicos deste grupo">
                    <MessageSquarePlus className="size-4" /> Tópicos ({topicos.length})
                  </Button>
                )}
                {topicosAbertos && (
                  <>
                    <div className="fixed inset-0 z-10" onClick={() => setTopicosAbertos(false)} aria-hidden />
                    <ul role="menu" aria-label="Tópicos" className="absolute right-0 top-full z-20 mt-1 max-h-64 w-60 overflow-y-auto rounded-xl border border-[var(--border)] bg-[var(--bg)] py-1 shadow-xl">
                      {topicos.map((t) => (
                        <li key={t.chave} role="none">
                          <button type="button" role="menuitem" onClick={() => abrirTopico(t)} className="flex w-full items-center gap-2 px-3 py-2 text-left text-sm hover:bg-[var(--surface-hover)]">
                            🧵 <span className="truncate">{t.nome}</span>
                          </button>
                        </li>
                      ))}
                    </ul>
                  </>
                )}
              </div>
              <Button variant="ghost" size="sm" onClick={() => (painel === "fixadas" ? setPainel(null) : void abrirPainel("fixadas"))} aria-pressed={painel === "fixadas"} title="Mensagens fixadas">
                <Pin className="size-4" /> <span className="hidden sm:inline">Fixadas</span>
              </Button>
              <Button variant="ghost" size="sm" onClick={() => (painel === "busca" ? setPainel(null) : void abrirPainel("busca"))} aria-pressed={painel === "busca"} title="Buscar nas mensagens">
                <Search className="size-4" /> <span className="hidden sm:inline">Buscar</span>
              </Button>
              {!emTopico && (
                <Button variant="secondary" size="sm" onClick={ligar} loading={ligando} title="Abre o canal de voz no Discord e avisa a conversa">
                  <Phone className="size-4" /> Chamada de voz
                </Button>
              )}
              </div>
            </div>

            {linkDaChamada && (
              <p className="border-b border-[var(--border)] bg-[var(--brand)]/10 px-4 py-2 text-xs text-[var(--fg)]">
                O navegador bloqueou a abertura do Discord.{" "}
                <a href={linkDaChamada} target="_blank" rel="noopener noreferrer" className="font-semibold text-[var(--brand)] underline">
                  Clique aqui para entrar na chamada
                </a>
                .
              </p>
            )}

            {dados?.conteudoOculto && (
              <p className="border-b border-[var(--border)] bg-[var(--warning)]/10 px-4 py-2 text-xs text-[var(--fg)]">
                O Discord não está entregando o texto das mensagens de algumas pessoas. Um administrador precisa ligar <strong>Message Content Intent</strong> no portal do
                desenvolvedor do Discord (aplicação → Bot → Privileged Gateway Intents) e clicar em <strong>Save Changes</strong>.
              </p>
            )}

            {painel === "busca" && (
              <form
                className="flex items-center gap-2 border-b border-[var(--border)] px-4 py-2"
                onSubmit={(e) => {
                  e.preventDefault();
                  void buscar();
                }}
              >
                <Search className="size-4 shrink-0 text-[var(--fg-muted)]" aria-hidden />
                <Input value={termoDeBusca} onChange={(e) => setTermoDeBusca(e.target.value)} placeholder="Buscar nas últimas mensagens (texto ou nome)" aria-label="Buscar nas mensagens" autoFocus maxLength={100} />
                <Button type="submit" size="sm" loading={carregandoPainel} disabled={termoDeBusca.trim().length < 2}>
                  Buscar
                </Button>
              </form>
            )}
            {painel && (painel === "fixadas" || resultados !== null) && (
              <PainelDeMensagens
                titulo={painel === "fixadas" ? "Fixadas" : `Resultados da busca (${resultados?.length ?? 0})`}
                mensagens={resultados}
                carregando={carregandoPainel}
                vazio={painel === "fixadas" ? "Nenhuma mensagem fixada nesta conversa." : "Nada encontrado nas últimas 300 mensagens."}
                aoFechar={() => setPainel(null)}
              />
            )}

            <div ref={rolagem} onScroll={aoRolar} className="flex-1 space-y-3 overflow-y-auto px-4 py-3" aria-live="polite">
              {erroMensagens ? (
                <ErrorState message={erroMensagens} onRetry={() => chave && carregarMensagens(chave)} />
              ) : !dados ? (
                <Skeleton className="h-full" />
              ) : dados.mensagens.length === 0 ? (
                <p className="py-10 text-center text-sm text-[var(--fg-muted)]">Ainda não há mensagens. Escreva a primeira!</p>
              ) : (
                <>
                  {dados.temMais && (
                    <div className="flex justify-center">
                      <Button variant="ghost" size="sm" onClick={carregarAnteriores} loading={carregandoAntigas}>
                        <ChevronUp className="size-4" /> Carregar mensagens anteriores
                      </Button>
                    </div>
                  )}
                  {dados.mensagens.map((m) => (
                    <Balao
                      key={m.id}
                      mensagem={m}
                      minha={m.doCrm && m.autorNome === sessao?.nomeCompleto}
                      aoResponder={() => {
                        setRespondendo(m);
                        campoDeTexto.current?.focus();
                      }}
                      aoEditar={() => setEditando({ id: m.id, texto: m.conteudo })}
                      aoApagar={() => setApagando(m)}
                      edicao={editando?.id === m.id ? editando.texto : null}
                      aoMudarEdicao={(t) => setEditando({ id: m.id, texto: t })}
                      aoSalvarEdicao={() => void salvarEdicao()}
                      aoCancelarEdicao={() => setEditando(null)}
                      salvandoEdicao={salvandoEdicao}
                      aoReagir={(emoji) => void reagir(m.id, emoji)}
                      podeFixar={podeFixar}
                      aoFixar={() => void alternarFixada(m)}
                    />
                  ))}
                </>
              )}
            </div>

            {respondendo && (
              <div className="flex items-center gap-2 border-t border-[var(--border)] px-3 pt-2 text-xs text-[var(--fg)]">
                <CornerUpLeft className="size-3.5 text-[var(--fg-muted)]" aria-hidden />
                <span className="truncate">
                  Respondendo a <strong>{respondendo.autorNome}</strong>: {trechoParaCitar(respondendo.conteudo)}
                </span>
                <button type="button" onClick={() => setRespondendo(null)} aria-label="Cancelar resposta" className="ml-auto rounded p-0.5 hover:bg-[var(--bg-muted)]">
                  <X className="size-3.5" />
                </button>
              </div>
            )}
            {arquivo && (
              <div className="flex items-center gap-2 border-t border-[var(--border)] px-3 pt-2 text-xs text-[var(--fg)]">
                <Paperclip className="size-3.5 text-[var(--fg-muted)]" aria-hidden />
                <span className="truncate">{arquivo.name}</span>
                <span className="shrink-0 text-[var(--fg-muted)]">({(arquivo.size / 1024 / 1024).toFixed(1)} MB)</span>
                <button type="button" onClick={() => setArquivo(null)} aria-label="Remover arquivo" className="ml-auto rounded p-0.5 hover:bg-[var(--bg-muted)]">
                  <X className="size-3.5" />
                </button>
              </div>
            )}
            <form
              className="flex items-end gap-2 border-t border-[var(--border)] p-3"
              onSubmit={(e) => {
                e.preventDefault();
                void enviar();
              }}
            >
              <input
                ref={seletorDeArquivo}
                type="file"
                accept={TIPOS_DE_ARQUIVO}
                className="hidden"
                onChange={(e) => {
                  const escolhido = e.target.files?.[0];
                  e.target.value = ""; // permite escolher o mesmo arquivo de novo depois de remover
                  if (!escolhido) return;
                  if (escolhido.size > LIMITE_ARQUIVO) notificar("error", "O arquivo pode ter no máximo 10 MB.");
                  else setArquivo(escolhido);
                }}
              />
              <div className="relative">
                <Button type="button" variant="ghost" onClick={() => setMenuMaisAberto((v) => !v)} aria-label="Mais opções" aria-haspopup="menu" aria-expanded={menuMaisAberto} title="Enviar arquivo, criar tópico ou enquete">
                  <Plus className="size-4" />
                </Button>
                {menuMaisAberto && (
                  <>
                    <div className="fixed inset-0 z-10" onClick={() => setMenuMaisAberto(false)} aria-hidden />
                    <ul role="menu" aria-label="Mais opções" className="absolute bottom-full left-0 z-20 mb-2 w-52 overflow-hidden rounded-xl border border-[var(--border)] bg-[var(--bg)] py-1 shadow-xl">
                      <li role="none">
                        <button
                          type="button"
                          role="menuitem"
                          onClick={() => {
                            setMenuMaisAberto(false);
                            seletorDeArquivo.current?.click();
                          }}
                          className="flex w-full items-center gap-3 px-3 py-2 text-left text-sm hover:bg-[var(--surface-hover)]"
                          title="Anexar imagem, PDF, planilha ou documento (até 10 MB)"
                        >
                          <Paperclip className="size-4 text-[var(--fg-muted)]" aria-hidden /> Enviar um arquivo
                        </button>
                      </li>
                      <li role="none">
                        <button
                          type="button"
                          role="menuitem"
                          disabled={(chave?.startsWith("dm:") || chave?.startsWith("topico:")) ?? false}
                          onClick={() => {
                            setMenuMaisAberto(false);
                            setTopicoAberto(true);
                          }}
                          className="flex w-full items-center gap-3 px-3 py-2 text-left text-sm hover:bg-[var(--surface-hover)] disabled:cursor-not-allowed disabled:opacity-50"
                          title={(chave?.startsWith("dm:") || chave?.startsWith("topico:")) ? "Tópicos só existem nos grupos" : "Abrir um tópico para conversar sobre um assunto"}
                        >
                          <MessageSquarePlus className="size-4 text-[var(--fg-muted)]" aria-hidden /> Criar tópico
                        </button>
                      </li>
                      <li role="none">
                        <button
                          type="button"
                          role="menuitem"
                          onClick={() => {
                            setMenuMaisAberto(false);
                            setEnqueteAberta(true);
                          }}
                          className="flex w-full items-center gap-3 px-3 py-2 text-left text-sm hover:bg-[var(--surface-hover)]"
                        >
                          <BarChart3 className="size-4 text-[var(--fg-muted)]" aria-hidden /> Criar enquete
                        </button>
                      </li>
                    </ul>
                  </>
                )}
              </div>
              <div className="relative">
                <Button type="button" variant="ghost" onClick={alternarSeletor} aria-label="Emojis e figurinhas" aria-expanded={seletorAberto} title="Emojis e figurinhas">
                  <Smile className="size-4" />
                </Button>
                {seletorAberto && (
                  <SeletorDeEmojis
                    extras={extras}
                    carregando={carregandoExtras}
                    aoEscolherEmoji={inserirNoTexto}
                    aoEscolherEmojiDoServidor={(marca) => inserirNoTexto(`${marca} `)}
                    aoEscolherFigurinha={(id) => void enviarFigurinha(id)}
                  />
                )}
              </div>
              <div className="relative flex-1">
              {mencaoAtiva && sugestoes.length > 0 && (
                <ul role="listbox" aria-label="Marcar pessoa" className="absolute bottom-full left-0 z-10 mb-1 w-64 overflow-hidden rounded-lg border border-[var(--border)] bg-[var(--bg)] shadow-lg">
                  {sugestoes.map((p) => (
                    <li key={p.id}>
                      <button
                        type="button"
                        role="option"
                        aria-selected="false"
                        onMouseDown={(e) => {
                          e.preventDefault(); // não tira o foco do campo de texto
                          escolherMencao(p);
                        }}
                        className="flex w-full items-center gap-2 px-3 py-2 text-left text-sm hover:bg-[var(--bg-muted)]"
                      >
                        <Avatar nome={p.nome} fotoUrl={p.fotoUrl} className="size-6 text-[10px]" />
                        <span className="flex-1 truncate">{p.nome}</span>
                        {p.online && <span className="size-2 rounded-full bg-[var(--success)]" aria-label="online" />}
                      </button>
                    </li>
                  ))}
                </ul>
              )}
              <textarea
                ref={campoDeTexto}
                value={texto}
                onChange={(e) => aoDigitar(e.target.value, e.target.selectionStart)}
                onKeyDown={(e) => {
                  if (e.key === "Escape" && mencaoAtiva) {
                    setMencaoAtiva(null);
                    return;
                  }
                  if (e.key === "Enter" && !e.shiftKey) {
                    e.preventDefault();
                    void enviar();
                  }
                }}
                maxLength={LIMITE_TEXTO}
                rows={2}
                placeholder="Escreva uma mensagem…"
                title="Enter envia; Shift+Enter quebra a linha"
                aria-label={`Mensagem para ${nomeDoGrupo ?? "o grupo"}`}
                className="min-h-10 w-full resize-none rounded-lg border border-[var(--border)] bg-[var(--bg)] px-3 py-2 text-sm text-[var(--fg)] outline-none focus:border-[var(--brand)]"
              />
              </div>
              <Button type="submit" loading={enviando} disabled={!texto.trim() && !arquivo}>
                <Send className="size-4" /> Enviar
              </Button>
            </form>
          </Card>
        </div>
      )}

      <NovaConversa aberta={novaConversa} aoFechar={() => setNovaConversa(false)} aoAbrir={aoAbrirConversa} />
      {chave && <NovoTopico chave={chave} aberto={topicoAberto} aoFechar={() => setTopicoAberto(false)} aoCriado={(aviso) => {
          acrescentarMensagem(aviso);
          setVersaoDosTopicos((v) => v + 1);
        }} />}
      {chave && <NovaEnquete chave={chave} aberto={enqueteAberta} aoFechar={() => setEnqueteAberta(false)} aoCriada={acrescentarMensagem} />}

      <ConfirmDialog
        open={!!apagando}
        title="Apagar mensagem"
        message="A mensagem será apagada para todos, também no Discord. Não dá para desfazer."
        confirmLabel="Apagar"
        danger
        loading={apagandoEmAndamento}
        onConfirm={() => void apagarMensagem()}
        onCancel={() => setApagando(null)}
      />
    </div>
  );
}

function ItemDaLista({ conversa, ativa, naoLidas, aoEscolher }: { conversa: DiscordChatCanal; ativa: boolean; naoLidas: number; aoEscolher: () => void }) {
  return (
    <button
      type="button"
      onClick={aoEscolher}
      aria-current={ativa ? "true" : undefined}
      className={`flex shrink-0 items-center gap-2 rounded-lg border px-3 py-2 text-left text-sm transition-colors ${
        ativa ? "border-[var(--brand)] bg-[var(--brand)]/10 font-semibold text-[var(--fg)]" : "border-[var(--border)] text-[var(--fg-muted)] hover:bg-[var(--bg-muted)]"
      }`}
    >
      {conversa.tipo === "direta" ? (
        <span className="relative shrink-0">
          <Avatar nome={conversa.nome} fotoUrl={conversa.fotoUrl} className="size-5 text-[9px]" />
          {conversa.online && <span className="absolute -bottom-0.5 -right-0.5 size-2 rounded-full border border-[var(--bg)] bg-[var(--success)]" aria-label="online" />}
        </span>
      ) : (
        <Hash className="size-4 shrink-0" aria-hidden />
      )}
      <span className="truncate">{conversa.nome}</span>
      {naoLidas > 0 && !ativa && (
        <span className="ml-auto rounded-full bg-[var(--brand)] px-1.5 text-xs font-semibold text-white" aria-label={`${naoLidas} não lidas`}>
          {rotuloNaoLidas(naoLidas)}
        </span>
      )}
    </button>
  );
}

/** Busca uma pessoa que já vinculou o Discord e abre (ou cria) a conversa 1:1 com ela. */
function NovaConversa({ aberta, aoFechar, aoAbrir }: { aberta: boolean; aoFechar: () => void; aoAbrir: (conversa: DiscordChatCanal) => void }) {
  const { notificar } = useToast();
  const [busca, setBusca] = useState("");
  const [contatos, setContatos] = useState<DiscordChatContato[] | null>(null);
  const [abrindo, setAbrindo] = useState<string | null>(null);

  useEffect(() => {
    if (!aberta) return;
    const controller = new AbortController();
    setContatos(null);
    // Espera a pessoa parar de digitar um instante antes de consultar.
    const timer = window.setTimeout(() => {
      const consulta = busca.trim() ? `?busca=${encodeURIComponent(busca.trim())}` : "";
      api
        .get<DiscordChatContato[]>(`/crm/discord/chat/contatos${consulta}`, controller.signal)
        .then(setContatos)
        .catch((e) => {
          if (!isAbortError(e)) setContatos([]);
        });
    }, 250);
    return () => {
      window.clearTimeout(timer);
      controller.abort();
    };
  }, [aberta, busca]);

  async function abrir(contato: DiscordChatContato) {
    setAbrindo(contato.id);
    try {
      aoAbrir(await api.post<DiscordChatCanal>("/crm/discord/chat/conversas", { usuarioId: contato.id }));
    } catch (e) {
      notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível abrir a conversa.");
    } finally {
      setAbrindo(null);
    }
  }

  return (
    <Modal open={aberta} onClose={aoFechar} title="Nova conversa">
      <div className="space-y-3">
        <div className="relative">
          <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-[var(--fg-muted)]" aria-hidden />
          <Input value={busca} onChange={(e) => setBusca(e.target.value)} placeholder="Buscar pessoa pelo nome" aria-label="Buscar pessoa" className="pl-9" autoFocus />
        </div>
        <p className="text-xs text-[var(--fg-muted)]">Aparecem as pessoas que já vincularam o Discord e entraram no servidor da empresa.</p>
        {!contatos ? (
          <Skeleton className="h-32" />
        ) : contatos.length === 0 ? (
          <p className="py-6 text-center text-sm text-[var(--fg-muted)]">Ninguém encontrado.</p>
        ) : (
          <ul className="max-h-80 space-y-1 overflow-y-auto">
            {contatos.map((c) => (
              <li key={c.id}>
                <button
                  type="button"
                  onClick={() => abrir(c)}
                  disabled={abrindo !== null}
                  className="flex w-full items-center gap-3 rounded-lg px-2 py-2 text-left hover:bg-[var(--bg-muted)] disabled:opacity-60"
                >
                  <span className="relative shrink-0">
                    <Avatar nome={c.nome} fotoUrl={c.fotoUrl} className="size-8 text-xs" />
                    {c.online && <span className="absolute -bottom-0.5 -right-0.5 size-2.5 rounded-full border-2 border-[var(--bg)] bg-[var(--success)]" aria-label="online" />}
                  </span>
                  <span className="flex-1 text-sm font-medium text-[var(--fg)]">{c.nome}</span>
                  {c.regional && <Badge variant="neutral">{c.regional}</Badge>}
                </button>
              </li>
            ))}
          </ul>
        )}
      </div>
    </Modal>
  );
}

function Balao({
  mensagem,
  minha,
  aoResponder,
  aoEditar,
  aoApagar,
  edicao,
  aoMudarEdicao,
  aoSalvarEdicao,
  aoCancelarEdicao,
  salvandoEdicao,
  aoReagir,
  podeFixar,
  aoFixar,
}: {
  mensagem: DiscordChatMensagem;
  minha: boolean;
  aoResponder: () => void;
  aoEditar: () => void;
  aoApagar: () => void;
  /** Texto em edição (null = não está editando esta mensagem). */
  edicao: string | null;
  aoMudarEdicao: (texto: string) => void;
  aoSalvarEdicao: () => void;
  aoCancelarEdicao: () => void;
  salvandoEdicao: boolean;
  aoReagir: (emoji: string) => void;
  podeFixar: boolean;
  aoFixar: () => void;
}) {
  const [reagindo, setReagindo] = useState(false);
  return (
    <div className={`group flex items-start gap-2 ${minha ? "flex-row-reverse" : ""}`}>
      <Avatar nome={mensagem.autorNome} fotoUrl={mensagem.autorFotoUrl} className="size-8 text-xs" />
      <div className={`max-w-[80%] rounded-xl px-3 py-2 ${minha ? "bg-[var(--brand)]/15" : "bg-[var(--bg-muted)]"}`}>
        <p className="flex flex-wrap items-center gap-x-2 text-xs text-[var(--fg-muted)]">
          <span className="font-semibold text-[var(--fg)]">{mensagem.autorNome}</span>
          {!mensagem.doCrm && <Badge variant="neutral">Discord</Badge>}
          <span>{formatarDataHora(mensagem.criadaEm)}</span>
          {mensagem.editada && <span>(editada)</span>}
          {mensagem.fixada && <span title="Mensagem fixada" aria-label="Mensagem fixada">📌</span>}
        </p>
        {edicao !== null ? (
          <div className="mt-1 space-y-2">
            <textarea
              value={edicao}
              onChange={(e) => aoMudarEdicao(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === "Escape") aoCancelarEdicao();
                if (e.key === "Enter" && !e.shiftKey) {
                  e.preventDefault();
                  aoSalvarEdicao();
                }
              }}
              maxLength={LIMITE_TEXTO}
              rows={2}
              autoFocus
              aria-label="Editar mensagem"
              className="w-full resize-none rounded-lg border border-[var(--border)] bg-[var(--bg)] px-2 py-1 text-sm text-[var(--fg)] outline-none focus:border-[var(--brand)]"
            />
            <div className="flex justify-end gap-2">
              <Button variant="ghost" size="sm" onClick={aoCancelarEdicao} disabled={salvandoEdicao}>
                Cancelar
              </Button>
              <Button size="sm" onClick={aoSalvarEdicao} loading={salvandoEdicao} disabled={!edicao.trim()}>
                Salvar
              </Button>
            </div>
          </div>
        ) : (
          mensagem.conteudo && <ConteudoDaMensagem texto={mensagem.conteudo} />
        )}
        {mensagem.anexos.map((a) =>
          a.url.startsWith("https://") ? (
            a.imagem ? (
              <a key={a.url} href={a.url} target="_blank" rel="noopener noreferrer" className="mt-1 block">
                <img src={a.url} alt={a.nome} loading="lazy" className="max-h-48 rounded-lg" />
              </a>
            ) : (
              <a key={a.url} href={a.url} target="_blank" rel="noopener noreferrer" className="mt-1 block text-xs text-[var(--brand)] hover:underline">
                {a.nome}
              </a>
            )
          ) : null
        )}
        {mensagem.enquete && <EnqueteNaMensagem enquete={mensagem.enquete} />}
        {mensagem.reacoes && <ReacoesDaMensagem reacoes={mensagem.reacoes} aoReagir={aoReagir} />}
        {edicao === null && (
          <div className="mt-1 flex gap-1 opacity-60 transition-opacity focus-within:opacity-100 group-hover:opacity-100">
            <span className="relative">
              <button type="button" onClick={() => setReagindo((v) => !v)} aria-label="Reagir" title="Reagir" aria-expanded={reagindo} className="rounded p-1 text-[var(--fg-muted)] hover:bg-[var(--bg)] hover:text-[var(--fg)]">
                <SmilePlus className="size-3.5" />
              </button>
              {reagindo && (
                <ReacoesRapidas
                  aoEscolher={(e) => {
                    setReagindo(false);
                    aoReagir(e);
                  }}
                />
              )}
            </span>
            {podeFixar && (
              <button type="button" onClick={aoFixar} aria-label={mensagem.fixada ? "Desafixar" : "Fixar"} title={mensagem.fixada ? "Desafixar" : "Fixar"} className="rounded p-1 text-[var(--fg-muted)] hover:bg-[var(--bg)] hover:text-[var(--fg)]">
                <Pin className="size-3.5" />
              </button>
            )}
            <button type="button" onClick={aoResponder} aria-label="Responder" title="Responder" className="rounded p-1 text-[var(--fg-muted)] hover:bg-[var(--bg)] hover:text-[var(--fg)]">
              <CornerUpLeft className="size-3.5" />
            </button>
            {minha && mensagem.doCrm && (
              <>
                <button type="button" onClick={aoEditar} aria-label="Editar" title="Editar" className="rounded p-1 text-[var(--fg-muted)] hover:bg-[var(--bg)] hover:text-[var(--fg)]">
                  <Pencil className="size-3.5" />
                </button>
                <button type="button" onClick={aoApagar} aria-label="Apagar" title="Apagar" className="rounded p-1 text-[var(--fg-muted)] hover:bg-[var(--bg)] hover:text-[var(--danger)]">
                  <Trash2 className="size-3.5" />
                </button>
              </>
            )}
          </div>
        )}
      </div>
    </div>
  );
}

/** Uma só linha, curta, para citar numa resposta (tira quebras de linha, citações antigas e o excesso). */
function trechoParaCitar(texto: string): string {
  const semCitacao = texto.split("\n").filter((l) => !l.startsWith("> ")).join(" ").replace(/\s+/g, " ").trim();
  const base = semCitacao || "(anexo)";
  return base.length > 80 ? `${base.slice(0, 80)}…` : base;
}

/** Texto da mensagem: linhas que começam com "> " viram citação; endereços viram links; o resto é texto puro. */
/** Enquete do Discord: pergunta, cada resposta com os votos e uma barra de proporção. Votar é no Discord (o CRM só acompanha). */
function EnqueteNaMensagem({ enquete }: { enquete: NonNullable<DiscordChatMensagem["enquete"]> }) {
  const total = enquete.respostas.reduce((soma, r) => soma + r.votos, 0);
  return (
    <div className="mt-1 w-full max-w-sm space-y-2 rounded-lg border border-[var(--border)] bg-[var(--bg)] p-3">
      <p className="flex items-start gap-2 text-sm font-semibold text-[var(--fg)]">
        <BarChart3 className="mt-0.5 size-4 shrink-0 text-[var(--brand)]" aria-hidden /> <span className="break-words">{enquete.pergunta}</span>
      </p>
      <ul className="space-y-1.5">
        {enquete.respostas.map((r, i) => {
          const pct = total > 0 ? Math.round((r.votos / total) * 100) : 0;
          return (
            <li key={i} className="text-xs text-[var(--fg)]">
              <div className="flex justify-between gap-2">
                <span className="break-words">{r.texto}</span>
                <span className="shrink-0 text-[var(--fg-muted)]">{r.votos} · {pct}%</span>
              </div>
              <div className="mt-0.5 h-1.5 overflow-hidden rounded-full bg-[var(--surface-hover)]">
                <div className="h-full rounded-full bg-[var(--brand)]" style={{ width: `${pct}%` }} />
              </div>
            </li>
          );
        })}
      </ul>
      <p className="text-[11px] text-[var(--fg-muted)]">
        {total} {total === 1 ? "voto" : "votos"}
        {enquete.variasEscolhas ? " · várias respostas" : ""}
        {enquete.encerrada ? " · encerrada" : enquete.encerraEm ? ` · até ${formatarDataHora(enquete.encerraEm)}` : ""} · vote no Discord
      </p>
    </div>
  );
}

function ConteudoDaMensagem({ texto }: { texto: string }) {
  const blocos: { citacao: boolean; linhas: string[] }[] = [];
  for (const linha of texto.split("\n")) {
    const citacao = linha.startsWith("> ");
    const ultimo = blocos[blocos.length - 1];
    const conteudo = citacao ? linha.slice(2) : linha;
    if (ultimo && ultimo.citacao === citacao) ultimo.linhas.push(conteudo);
    else blocos.push({ citacao, linhas: [conteudo] });
  }

  return (
    <div className="space-y-1 break-words text-sm text-[var(--fg)]">
      {blocos.map((b, i) =>
        b.citacao ? (
          <blockquote key={i} className="whitespace-pre-wrap border-l-2 border-[var(--brand)]/50 pl-2 text-xs text-[var(--fg-muted)]">
            {comLinks(b.linhas.join("\n"))}
          </blockquote>
        ) : (
          <p key={i} className="whitespace-pre-wrap">
            {comLinks(b.linhas.join("\n"))}
          </p>
        )
      )}
    </div>
  );
}

/**
 * Endereços (http/https) viram links clicáveis e **texto entre dois asteriscos** vira negrito (é como o Discord mostra, e os avisos do CRM usam).
 * O resto continua texto puro: nada de HTML, então nenhuma mensagem consegue injetar código na tela.
 */
function comLinks(texto: string) {
  // Emoji personalizado do servidor (<:nome:id> ou <a:nome:id>) vira a imagem dele; o resto segue pelas regras abaixo.
  return texto.split(/(<a?:\w+:\d+>)/g).flatMap((pedaco, k) => {
    const emoji = /^<(a?):(\w+):(\d+)>$/.exec(pedaco);
    if (emoji) {
      return [
        <img
          key={`e${k}`}
          src={`https://cdn.discordapp.com/emojis/${emoji[3]}.${emoji[1] ? "gif" : "png"}?size=48`}
          alt={`:${emoji[2]}:`}
          title={`:${emoji[2]}:`}
          loading="lazy"
          className="mx-0.5 inline-block size-5 align-text-bottom"
        />,
      ];
    }
    return comLinksSemEmoji(pedaco, k);
  });
}

function comLinksSemEmoji(texto: string, grupo: number) {
  return texto.split(/(https?:\/\/[^\s]+)/g).map((parte, i) =>
    /^https?:\/\//.test(parte) ? (
      <a key={`${grupo}-${i}`} href={parte} target="_blank" rel="noopener noreferrer" className="text-[var(--brand)] underline">
        {parte}
      </a>
    ) : (
      <span key={`${grupo}-${i}`}>
        {parte.split(/(\*\*[^*\n]+\*\*)/g).map((trecho, j) =>
          /^\*\*[^*\n]+\*\*$/.test(trecho) ? <strong key={j}>{trecho.slice(2, -2)}</strong> : trecho
        )}
      </span>
    )
  );
}

/** Som e notificação do navegador para mensagem nova (preferências deste navegador). */
function PreferenciasDeAviso() {
  const { notificar } = useToast();
  const [som, setSom] = useState(somLigado);
  const [notificacao, setNotificacao] = useState(notificacaoLigada);

  async function alternarNotificacao(ligar: boolean) {
    if (!ligar) {
      desligarNotificacao();
      setNotificacao(false);
      return;
    }
    const ok = await ligarNotificacao();
    setNotificacao(ok);
    if (!ok) notificar("error", "O navegador não permitiu as notificações. Libere nas configurações do site e tente de novo.");
  }

  return (
    <div className="mt-2 flex flex-wrap gap-x-5 gap-y-1">
      <Checkbox
        label="Som de mensagem nova"
        checked={som}
        onChange={(e) => {
          definirSom(e.target.checked);
          setSom(e.target.checked);
        }}
      />
      {notificacaoSuportada() && (
        <Checkbox label="Avisar pelo navegador (com a aba em segundo plano)" checked={notificacao} onChange={(e) => void alternarNotificacao(e.target.checked)} />
      )}
    </div>
  );
}
