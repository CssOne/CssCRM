import { FileText, LifeBuoy, Paperclip, Plus, Search, Send, X } from "lucide-react";
import { useCallback, useEffect, useRef, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { api, isAbortError, uploadFile } from "../lib/api";
import { avisoDeEspera, ehImagem, erroDoAnexo } from "../lib/suporte";
import { formatarDataHora } from "../lib/format";
import { Badge, Button, Card, EmptyState, ErrorState, Input, Label, Modal, Skeleton, Textarea, useToast } from "../components/ui";

type StatusChamado = "Aberto" | "EmAtendimento" | "Resolvido";

interface ChamadoResumo {
  id: string;
  assunto: string;
  categoria: string;
  status: StatusChamado;
  solicitanteId: string;
  solicitanteNome: string;
  solicitanteEmail?: string | null;
  criadoEm: string;
  ultimaMensagemEm: string;
  mensagens: number;
  /** Em aberto e a última mensagem é do solicitante: espera resposta do suporte. */
  aguardandoSuporte?: boolean;
  aguardandoDesde?: string | null;
}

interface MensagemChamado {
  id: string;
  autorNome: string;
  doSuporte: boolean;
  texto: string;
  criadoEm: string;
  anexoUrl?: string | null;
  anexoNome?: string | null;
}

interface ChamadoDetalhe {
  resumo: ChamadoResumo;
  mensagens: MensagemChamado[];
}

interface ListaSuporte {
  atende: boolean;
  abertos: number;
  chamados: ChamadoResumo[];
}

const CATEGORIAS = ["Dúvida", "Problema", "Sugestão", "Acesso", "Outro"];

type FiltroChamados = "abertos" | "aguardando" | "atendimento" | "resolvidos" | "todos";

const iniciais = (nome: string) =>
  nome
    .split(" ")
    .filter(Boolean)
    .slice(0, 2)
    .map((p) => p[0]?.toUpperCase())
    .join("");

const STATUS_ROTULO: Record<StatusChamado, string> = { Aberto: "Aberto", EmAtendimento: "Em atendimento", Resolvido: "Resolvido" };
const STATUS_COR: Record<StatusChamado, "warning" | "info" | "success"> = { Aberto: "warning", EmAtendimento: "info", Resolvido: "success" };

/** "Sem resposta há 3 h": só aparece quando o chamado espera o suporte; vira vermelho depois de um dia. */
function AvisoDeEspera({ aguardando, desde, atende }: { aguardando: boolean; desde?: string | null; atende: boolean }) {
  const aviso = avisoDeEspera(aguardando, desde, atende);
  if (!aviso) return null;
  return (
    <p className={`mt-1 text-xs font-medium ${aviso.urgente ? "text-[var(--danger)]" : "text-[var(--warning)]"}`}>
      {aviso.urgente ? "⚠ " : ""}
      {aviso.texto}
    </p>
  );
}

/**
 * Suporte: todo usuário abre chamados e acompanha as respostas. Quem atende (o administrador do sistema)
 * vê os chamados de todos, responde e marca como resolvido — o back-end decide quem é quem.
 */
export function SuportePage() {
  const { notificar } = useToast();
  const [params, setParams] = useSearchParams();
  const [lista, setLista] = useState<ListaSuporte | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [recarregar, setRecarregar] = useState(0);
  const [filtro, setFiltro] = useState<FiltroChamados>("abertos");
  const [busca, setBusca] = useState("");
  const fimDaConversa = useRef<HTMLDivElement>(null);
  const painelDoChamado = useRef<HTMLDivElement>(null);

  const [novoAberto, setNovoAberto] = useState(false);
  const [assunto, setAssunto] = useState("");
  const [categoria, setCategoria] = useState(CATEGORIAS[0]);
  const [mensagem, setMensagem] = useState("");
  const [enviando, setEnviando] = useState(false);

  const chamadoId = params.get("chamado");
  const [detalhe, setDetalhe] = useState<ChamadoDetalhe | null>(null);
  const [resposta, setResposta] = useState("");
  const [respondendo, setRespondendo] = useState(false);
  const [anexo, setAnexo] = useState<File | null>(null);
  const inputAnexo = useRef<HTMLInputElement>(null);

  useEffect(() => {
    const controller = new AbortController();
    api
      .get<ListaSuporte>("/crm/suporte", controller.signal)
      .then((l) => {
        setLista(l);
        setErro(null);
      })
      .catch((e) => {
        if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar o suporte.");
      });
    return () => controller.abort();
  }, [recarregar]);

  useEffect(() => {
    if (!chamadoId) {
      setDetalhe(null);
      return;
    }
    const controller = new AbortController();
    api
      .get<ChamadoDetalhe>(`/crm/suporte/${chamadoId}`, controller.signal)
      .then(setDetalhe)
      .catch((e) => {
        if (isAbortError(e)) return;
        notificar("error", e instanceof Error ? e.message : "Não foi possível abrir o chamado.");
        setParams({}, { replace: true });
      });
    return () => controller.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [chamadoId]);

  // Conversa sempre na última mensagem; no celular, escolher um chamado leva até o painel dele (a lista fica acima).
  useEffect(() => {
    if (!detalhe) return;
    fimDaConversa.current?.scrollIntoView({ block: "nearest" });
  }, [detalhe?.resumo.id, detalhe?.mensagens.length]);

  useEffect(() => {
    if (chamadoId && window.matchMedia("(max-width: 1023px)").matches) painelDoChamado.current?.scrollIntoView({ behavior: "smooth", block: "start" });
  }, [chamadoId]);

  const abrirChamado = useCallback(async () => {
    setEnviando(true);
    try {
      const criado = await api.post<ChamadoDetalhe>("/crm/suporte", { assunto: assunto.trim(), categoria, mensagem: mensagem.trim() });
      notificar("success", "Chamado enviado ao suporte.");
      setNovoAberto(false);
      setAssunto("");
      setMensagem("");
      setCategoria(CATEGORIAS[0]);
      setRecarregar((n) => n + 1);
      setParams({ chamado: criado.resumo.id });
    } catch (e) {
      notificar("error", e instanceof Error ? e.message : "Não foi possível enviar o chamado.");
    } finally {
      setEnviando(false);
    }
  }, [assunto, categoria, mensagem, notificar, setParams]);

  function escolherAnexo(arquivo: File | null | undefined) {
    if (!arquivo) return;
    const erro = erroDoAnexo(arquivo);
    if (erro) {
      notificar("error", erro);
      return;
    }
    setAnexo(arquivo);
  }

  async function responder() {
    if (!detalhe || (!resposta.trim() && !anexo)) return;
    setRespondendo(true);
    try {
      const id = detalhe.resumo.id;
      setDetalhe(
        anexo
          ? await uploadFile<ChamadoDetalhe>(`/crm/suporte/${id}/anexo`, anexo, { texto: resposta.trim() })
          : await api.post<ChamadoDetalhe>(`/crm/suporte/${id}/mensagens`, { texto: resposta.trim() })
      );
      setResposta("");
      setAnexo(null);
      setRecarregar((n) => n + 1);
    } catch (e) {
      notificar("error", e instanceof Error ? e.message : "Não foi possível enviar a mensagem.");
    } finally {
      setRespondendo(false);
    }
  }

  async function mudarStatus(status: StatusChamado) {
    if (!detalhe) return;
    try {
      setDetalhe(await api.put<ChamadoDetalhe>(`/crm/suporte/${detalhe.resumo.id}/status`, { status }));
      setRecarregar((n) => n + 1);
    } catch (e) {
      notificar("error", e instanceof Error ? e.message : "Não foi possível mudar o status.");
    }
  }

  const atende = lista?.atende ?? false;
  const todos = lista?.chamados ?? [];
  const contagem = {
    abertos: todos.filter((c) => c.status !== "Resolvido").length,
    atendimento: todos.filter((c) => c.status === "EmAtendimento").length,
    resolvidos: todos.filter((c) => c.status === "Resolvido").length,
    aguardando: todos.filter((c) => c.aguardandoSuporte).length,
    todos: todos.length,
  };
  const termo = busca.trim().toLowerCase();
  const chamados = todos
    .filter((c) =>
      filtro === "todos" ? true : filtro === "abertos" ? c.status !== "Resolvido" : filtro === "aguardando" ? !!c.aguardandoSuporte : filtro === "atendimento" ? c.status === "EmAtendimento" : c.status === "Resolvido"
    )
    .filter((c) => !termo || `${c.assunto} ${c.solicitanteNome} ${c.categoria}`.toLowerCase().includes(termo));
  const FILTROS: { valor: FiltroChamados; rotulo: string }[] = [
    { valor: "abertos", rotulo: "Em aberto" },
    ...(atende ? [{ valor: "aguardando" as const, rotulo: "Sem resposta" }] : []),
    ...(atende ? [{ valor: "atendimento" as const, rotulo: "Em atendimento" }] : []),
    { valor: "resolvidos", rotulo: "Resolvidos" },
    { valor: "todos", rotulo: "Todos" },
  ];

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="flex items-center gap-2 text-xl font-bold text-[var(--fg)]">
            <LifeBuoy className="size-5 text-[var(--brand)]" aria-hidden /> Suporte
          </h1>
          <p className="text-sm text-[var(--fg-muted)]">
            {atende ? "Chamados abertos pelos usuários do sistema." : "Precisa de ajuda? Abra um chamado e acompanhe a resposta aqui."}
          </p>
        </div>
        <Button onClick={() => setNovoAberto(true)}>
          <Plus className="size-4" /> Novo chamado
        </Button>
      </div>

      {erro ? (
        <ErrorState message={erro} onRetry={() => setRecarregar((n) => n + 1)} />
      ) : !lista ? (
        <div className="space-y-2">
          {Array.from({ length: 3 }).map((_, i) => (
            <Skeleton key={i} className="h-16" />
          ))}
        </div>
      ) : (
        <div className="grid gap-4 lg:grid-cols-[minmax(0,2fr)_minmax(0,3fr)]">
          <Card className="space-y-3 p-4">
            <h2 className="text-sm font-semibold text-[var(--fg)]">
              {atende ? "Todos os chamados" : "Meus chamados"}
              {lista.abertos > 0 && <span className="ml-2 text-xs font-normal text-[var(--fg-muted)]">{lista.abertos} em aberto</span>}
            </h2>
            <div className="relative">
              <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-[var(--fg-muted)]" aria-hidden />
              <Input aria-label="Buscar chamados" className="pl-9" placeholder="Buscar por assunto, tipo ou pessoa" value={busca} onChange={(e) => setBusca(e.target.value)} />
            </div>
            <div className="flex flex-wrap gap-1.5" role="group" aria-label="Filtrar chamados">
              {FILTROS.map((f) => (
                <button
                  key={f.valor}
                  type="button"
                  aria-pressed={filtro === f.valor}
                  onClick={() => setFiltro(f.valor)}
                  className={`focus-ring cursor-pointer rounded-full border px-3 py-1 text-xs font-medium transition-colors ${
                    filtro === f.valor
                      ? "border-[var(--brand)] bg-[var(--brand-soft)] text-[var(--brand)]"
                      : "border-[var(--border)] text-[var(--fg-muted)] hover:bg-[var(--surface-hover)]"
                  }`}
                >
                  {f.rotulo} <span className="opacity-70">{contagem[f.valor]}</span>
                </button>
              ))}
            </div>

            {chamados.length === 0 ? (
              <EmptyState
                title="Nenhum chamado"
                description={termo ? "Nada encontrado para essa busca." : filtro === "abertos" ? "Não há chamados em aberto." : "Nenhum chamado nesse filtro."}
              />
            ) : (
              <ul className="space-y-2">
                {chamados.map((c) => (
                  <li key={c.id}>
                    <button
                      type="button"
                      onClick={() => setParams({ chamado: c.id })}
                      className={`focus-ring w-full cursor-pointer rounded-xl border p-3 text-left transition-colors hover:bg-[var(--surface-hover)] ${
                        c.id === chamadoId ? "border-[var(--brand)] bg-[var(--surface-hover)]" : "border-[var(--border)]"
                      }`}
                    >
                      <div className="flex items-start justify-between gap-2">
                        <p className="min-w-0 truncate text-sm font-medium text-[var(--fg)]">{c.assunto}</p>
                        <Badge variant={STATUS_COR[c.status]}>{STATUS_ROTULO[c.status]}</Badge>
                      </div>
                      <AvisoDeEspera aguardando={!!c.aguardandoSuporte} desde={c.aguardandoDesde} atende={atende} />
                      <p className="mt-1 text-xs text-[var(--fg-muted)]">
                        {atende ? `${c.solicitanteNome} · ` : ""}
                        {c.categoria} · {formatarDataHora(c.ultimaMensagemEm)} · {c.mensagens} mensagem(ns)
                      </p>
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </Card>

          <Card className="scroll-mt-4 p-4 lg:sticky lg:top-4 lg:self-start">
            <div ref={painelDoChamado} className="-mt-4 pt-4" aria-hidden />
            {!detalhe ? (
              <EmptyState title="Selecione um chamado" description="Escolha um chamado da lista ou abra um novo." />
            ) : (
              <div className="space-y-4">
                <div className="flex flex-wrap items-start justify-between gap-2">
                  <div className="min-w-0">
                    <h2 className="break-words text-base font-semibold text-[var(--fg)]">{detalhe.resumo.assunto}</h2>
                    <p className="text-xs text-[var(--fg-muted)]">
                      {detalhe.resumo.solicitanteNome}
                      {detalhe.resumo.solicitanteEmail ? ` (${detalhe.resumo.solicitanteEmail})` : ""} · {detalhe.resumo.categoria} · aberto em{" "}
                      {formatarDataHora(detalhe.resumo.criadoEm)}
                    </p>
                    <AvisoDeEspera aguardando={!!detalhe.resumo.aguardandoSuporte} desde={detalhe.resumo.aguardandoDesde} atende={atende} />
                  </div>
                  <Badge variant={STATUS_COR[detalhe.resumo.status]}>{STATUS_ROTULO[detalhe.resumo.status]}</Badge>
                </div>

                <div className="max-h-[50vh] space-y-3 overflow-y-auto pr-1" role="log" aria-label="Mensagens do chamado">
                  {detalhe.mensagens.map((m) => (
                    <div key={m.id} className={`flex items-end gap-2 ${m.doSuporte ? "flex-row-reverse" : ""}`}>
                      <span
                        aria-hidden
                        className={`flex size-8 shrink-0 items-center justify-center rounded-full text-xs font-semibold ${
                          m.doSuporte ? "bg-[var(--brand)] text-[var(--brand-fg)]" : "bg-[var(--surface-hover)] text-[var(--fg-muted)]"
                        }`}
                      >
                        {iniciais(m.autorNome)}
                      </span>
                      <div className={`max-w-[85%] rounded-2xl p-3 text-sm ${m.doSuporte ? "rounded-br-sm bg-[var(--brand-soft)]" : "rounded-bl-sm bg-[var(--surface-hover)]"}`}>
                        <p className="mb-1 text-xs font-medium text-[var(--fg-muted)]">
                          {m.autorNome}
                          {m.doSuporte && !m.autorNome.toLowerCase().includes("suporte") ? " · Suporte" : ""} · {formatarDataHora(m.criadoEm)}
                        </p>
                        {m.texto && <p className="whitespace-pre-wrap break-words text-[var(--fg)]">{m.texto}</p>}
                        {m.anexoUrl &&
                          (ehImagem(m.anexoUrl) ? (
                            <a href={m.anexoUrl} target="_blank" rel="noreferrer" className="focus-ring mt-2 block w-fit">
                              <img src={m.anexoUrl} alt={m.anexoNome ?? "Anexo"} loading="lazy" className="max-h-48 rounded-lg border border-[var(--border)]" />
                            </a>
                          ) : (
                            <a href={m.anexoUrl} target="_blank" rel="noreferrer" className="focus-ring mt-2 flex w-fit items-center gap-1.5 rounded-lg border border-[var(--border)] px-2 py-1 text-xs text-[var(--brand)] hover:underline">
                              <FileText className="size-3.5" aria-hidden /> {m.anexoNome ?? "Anexo"}
                            </a>
                          ))}
                      </div>
                    </div>
                  ))}
                  <div ref={fimDaConversa} />
                </div>

                <div className="space-y-2">
                  <Textarea
                    rows={3}
                    maxLength={4000}
                    placeholder={detalhe.resumo.status === "Resolvido" ? "Responder reabre o chamado…" : "Escreva sua mensagem…"}
                    value={resposta}
                    onChange={(e) => setResposta(e.target.value)}
                    onPaste={(e) => {
                      // Print colado (Ctrl+V) vira anexo.
                      const imagem = Array.from(e.clipboardData.files).find((f) => f.type.startsWith("image/"));
                      if (imagem) {
                        e.preventDefault();
                        escolherAnexo(imagem);
                      }
                    }}
                    onKeyDown={(e) => {
                      if (e.key === "Enter" && (e.ctrlKey || e.metaKey) && !respondendo) {
                        e.preventDefault();
                        void responder();
                      }
                    }}
                  />
                  <input
                    ref={inputAnexo}
                    type="file"
                    accept=".png,.jpg,.jpeg,.webp,.pdf"
                    className="hidden"
                    aria-label="Anexar arquivo"
                    onChange={(e) => {
                      escolherAnexo(e.target.files?.[0]);
                      e.target.value = "";
                    }}
                  />
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <p className="text-xs text-[var(--fg-muted)]">Ctrl + Enter para enviar · cole um print com Ctrl + V.</p>
                    {anexo ? (
                      <span className="flex max-w-full items-center gap-1.5 rounded-lg bg-[var(--surface-hover)] px-2 py-1 text-xs text-[var(--fg)]">
                        <Paperclip className="size-3.5 shrink-0" aria-hidden />
                        <span className="truncate">{anexo.name}</span>
                        <button type="button" aria-label="Remover anexo" onClick={() => setAnexo(null)} className="focus-ring cursor-pointer rounded text-[var(--fg-muted)] hover:text-[var(--fg)]">
                          <X className="size-3.5" />
                        </button>
                      </span>
                    ) : (
                      <Button variant="secondary" size="sm" type="button" onClick={() => inputAnexo.current?.click()}>
                        <Paperclip className="size-4" /> Anexar print ou PDF
                      </Button>
                    )}
                  </div>
                  <div className="flex flex-wrap justify-end gap-2">
                    {atende && detalhe.resumo.status === "Aberto" && (
                      <Button variant="secondary" onClick={() => mudarStatus("EmAtendimento")}>
                        Em atendimento
                      </Button>
                    )}
                    {detalhe.resumo.status !== "Resolvido" ? (
                      <Button variant="secondary" onClick={() => mudarStatus("Resolvido")}>
                        Marcar como resolvido
                      </Button>
                    ) : (
                      <Button variant="secondary" onClick={() => mudarStatus("Aberto")}>
                        Reabrir
                      </Button>
                    )}
                    <Button onClick={responder} loading={respondendo} disabled={!resposta.trim() && !anexo}>
                      <Send className="size-4" /> Enviar
                    </Button>
                  </div>
                </div>
              </div>
            )}
          </Card>
        </div>
      )}

      <Modal open={novoAberto} onClose={() => setNovoAberto(false)} title="Novo chamado de suporte" size="md">
        <div className="space-y-4">
          <div>
            <Label htmlFor="suporte-assunto" required>Assunto</Label>
            <Input id="suporte-assunto" maxLength={200} value={assunto} onChange={(e) => setAssunto(e.target.value)} placeholder="Resumo do que você precisa" />
          </div>
          <div>
            <Label htmlFor="suporte-categoria">Tipo</Label>
            <div id="suporte-categoria" className="flex flex-wrap gap-1.5" role="radiogroup" aria-label="Tipo do chamado">
              {CATEGORIAS.map((c) => (
                <button
                  key={c}
                  type="button"
                  role="radio"
                  aria-checked={categoria === c}
                  onClick={() => setCategoria(c)}
                  className={`focus-ring cursor-pointer rounded-full border px-3 py-1.5 text-sm transition-colors ${
                    categoria === c
                      ? "border-[var(--brand)] bg-[var(--brand-soft)] font-medium text-[var(--brand)]"
                      : "border-[var(--border)] text-[var(--fg-muted)] hover:bg-[var(--surface-hover)]"
                  }`}
                >
                  {c}
                </button>
              ))}
            </div>
          </div>
          <div>
            <Label htmlFor="suporte-mensagem" required>Mensagem</Label>
            <Textarea
              id="suporte-mensagem"
              rows={5}
              maxLength={4000}
              value={mensagem}
              onChange={(e) => setMensagem(e.target.value)}
              placeholder="Descreva o problema ou a dúvida, com o máximo de detalhes (tela, cliente, o que aconteceu)."
            />
          </div>
          <div className="flex justify-end gap-2">
            <Button variant="secondary" onClick={() => setNovoAberto(false)} disabled={enviando}>
              Cancelar
            </Button>
            <Button onClick={abrirChamado} loading={enviando} disabled={assunto.trim().length < 3 || mensagem.trim().length < 3}>
              Enviar chamado
            </Button>
          </div>
        </div>
      </Modal>
    </div>
  );
}
