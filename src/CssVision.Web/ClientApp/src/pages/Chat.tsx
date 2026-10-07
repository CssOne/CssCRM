import { ChevronUp, Hash, MessagesSquare, Send } from "lucide-react";
import { useCallback, useEffect, useLayoutEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { useAuth } from "../context/AuthContext";
import { api, ApiRequestError, isAbortError } from "../lib/api";
import { formatarDataHora } from "../lib/format";
import type { DiscordChatCanal, DiscordChatMensagem, DiscordChatMensagens } from "../lib/types";
import { Avatar, Badge, Button, Card, EmptyState, ErrorState, Skeleton, useToast } from "../components/ui";

const LIMITE_TEXTO = 2000;
const INTERVALO_MS = 4000;

/**
 * Chat de texto dos grupos da empresa. As conversas ficam no Discord (um canal por grupo) e aparecem aqui, com o nome e a foto de cada pessoa;
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
  const rolagem = useRef<HTMLDivElement>(null);
  const colarNoFim = useRef(true);
  const chaveAtual = useRef<string | null>(null);

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

  const nomeDoGrupo = canais?.find((c) => c.chave === chave)?.nome;

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
            {canais.map((c) => (
              <button
                key={c.chave}
                type="button"
                onClick={() => setChave(c.chave)}
                aria-current={c.chave === chave ? "true" : undefined}
                className={`flex shrink-0 items-center gap-2 rounded-lg border px-3 py-2 text-left text-sm transition-colors ${
                  c.chave === chave
                    ? "border-[var(--brand)] bg-[var(--brand)]/10 font-semibold text-[var(--fg)]"
                    : "border-[var(--border)] text-[var(--fg-muted)] hover:bg-[var(--bg-muted)]"
                }`}
              >
                <Hash className="size-4 shrink-0" aria-hidden /> {c.nome}
              </button>
            ))}
          </nav>

          <Card className="flex h-[calc(100dvh-15rem)] min-h-[22rem] flex-col p-0">
            <div className="flex items-center justify-between border-b border-[var(--border)] px-4 py-2">
              <p className="flex items-center gap-1.5 text-sm font-semibold text-[var(--fg)]">
                <Hash className="size-4 text-[var(--fg-muted)]" aria-hidden /> {nomeDoGrupo}
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
    </div>
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
