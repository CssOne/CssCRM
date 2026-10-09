import { ChevronUp, Hash, MessagesSquare, Plus, Search, Send } from "lucide-react";
import { useCallback, useEffect, useLayoutEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { useAuth } from "../context/AuthContext";
import { api, ApiRequestError, isAbortError } from "../lib/api";
import { formatarDataHora } from "../lib/format";
import { rotuloNaoLidas, useChatNaoLidas } from "../lib/useChatNaoLidas";
import type { DiscordChatCanal, DiscordChatContato, DiscordChatMensagem, DiscordChatMensagens } from "../lib/types";
import { Avatar, Badge, Button, Card, EmptyState, ErrorState, Input, Modal, Skeleton, useToast } from "../components/ui";

const LIMITE_TEXTO = 2000;
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
    api
      .get<DiscordChatCanal[]>("/crm/discord/chat/canais", controller.signal)
      .then((lista) => {
        setCanais(lista);
        setChave((atual) => atual ?? lista[0]?.chave ?? null);
      })
      .catch((e) => {
        if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar as conversas.");
      });
    return () => controller.abort();
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

  async function enviar() {
    const conteudo = texto.trim();
    if (!chave || !conteudo || enviando) return;
    setEnviando(true);
    try {
      const nova = await api.post<DiscordChatMensagem>(`/crm/discord/chat/canais/${encodeURIComponent(chave)}/mensagens`, { texto: conteudo });
      setTexto("");
      colarNoFim.current = true;
      setDados((atual) => ({ mensagens: [...(atual?.mensagens ?? []), nova], temMais: atual?.temMais ?? false, conteudoOculto: atual?.conteudoOculto ?? false }));
    } catch (e) {
      notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível enviar a mensagem.");
    } finally {
      setEnviando(false);
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
                {nomeDoGrupo}
              </p>
            </div>

            {dados?.conteudoOculto && (
              <p className="border-b border-[var(--border)] bg-[var(--warning)]/10 px-4 py-2 text-xs text-[var(--fg)]">
                O Discord não está entregando o texto das mensagens. Um administrador precisa ligar <strong>Message Content Intent</strong> no portal do desenvolvedor do
                Discord (aplicação → Bot → Privileged Gateway Intents).
              </p>
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
                    <Balao key={m.id} mensagem={m} minha={m.doCrm && m.autorNome === sessao?.nomeCompleto} />
                  ))}
                </>
              )}
            </div>

            <form
              className="flex items-end gap-2 border-t border-[var(--border)] p-3"
              onSubmit={(e) => {
                e.preventDefault();
                void enviar();
              }}
            >
              <textarea
                value={texto}
                onChange={(e) => setTexto(e.target.value)}
                onKeyDown={(e) => {
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
                className="min-h-10 flex-1 resize-none rounded-lg border border-[var(--border)] bg-[var(--bg)] px-3 py-2 text-sm text-[var(--fg)] outline-none focus:border-[var(--brand)]"
              />
              <Button type="submit" loading={enviando} disabled={!texto.trim()}>
                <Send className="size-4" /> Enviar
              </Button>
            </form>
          </Card>
        </div>
      )}

      <NovaConversa aberta={novaConversa} aoFechar={() => setNovaConversa(false)} aoAbrir={aoAbrirConversa} />
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
      {conversa.tipo === "direta" ? <Avatar nome={conversa.nome} fotoUrl={conversa.fotoUrl} className="size-5 text-[9px]" /> : <Hash className="size-4 shrink-0" aria-hidden />}
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
                  <Avatar nome={c.nome} fotoUrl={c.fotoUrl} className="size-8 text-xs" />
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

function Balao({ mensagem, minha }: { mensagem: DiscordChatMensagem; minha: boolean }) {
  return (
    <div className={`flex items-start gap-2 ${minha ? "flex-row-reverse" : ""}`}>
      <Avatar nome={mensagem.autorNome} fotoUrl={mensagem.autorFotoUrl} className="size-8 text-xs" />
      <div className={`max-w-[80%] rounded-xl px-3 py-2 ${minha ? "bg-[var(--brand)]/15" : "bg-[var(--bg-muted)]"}`}>
        <p className="flex flex-wrap items-center gap-x-2 text-xs text-[var(--fg-muted)]">
          <span className="font-semibold text-[var(--fg)]">{mensagem.autorNome}</span>
          {!mensagem.doCrm && <Badge variant="neutral">Discord</Badge>}
          <span>{formatarDataHora(mensagem.criadaEm)}</span>
        </p>
        {mensagem.conteudo && <p className="whitespace-pre-wrap break-words text-sm text-[var(--fg)]">{mensagem.conteudo}</p>}
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
      </div>
    </div>
  );
}
