import { ArrowLeft, BellRing, Check, Send } from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router-dom";
import { api, isAbortError } from "../../lib/api";
import { formatarDataHora, formatarMoeda } from "../../lib/format";
import { useAtualizarAoVivo } from "../../lib/useAoVivo";
import type { AvisoPagamento } from "../../lib/types";
import { Avatar, Badge, Button, Card, EmptyState, ErrorState, Label, MoneyInput, Select, Skeleton, Tabs, Textarea, useToast } from "../../components/ui";
import { CobrancasDoConsultor, type Cobranca } from "../../components/portal/CobrancasDoConsultor";
import { ContratoFinanceiro } from "../../components/portal/ContratoFinanceiro";
import { CustosDoConsultor } from "../../components/portal/CustosDoConsultor";

type Aba = "cobrancas" | "contrato" | "avisos";

interface RascunhoDeAviso {
  mensagem: string;
  valor: number;
  referencia: string;
}

const MESES_EXTENSO = ["janeiro", "fevereiro", "março", "abril", "maio", "junho", "julho", "agosto", "setembro", "outubro", "novembro", "dezembro"];
const dataBr = (iso: string) => `${iso.slice(8, 10)}/${iso.slice(5, 7)}/${iso.slice(0, 4)}`;
const moeda = (v: number) => v.toLocaleString("pt-BR", { style: "currency", currency: "BRL" });

/** Mensagem e valor sugeridos para cobrar uma parcela em aberto, de acordo com o tipo da cobrança. */
function rascunhoDaCobranca(c: Cobranca, nome: string): RascunhoDeAviso {
  const mes = `${MESES_EXTENSO[Number(c.competencia.slice(5, 7)) - 1]} de ${c.competencia.slice(0, 4)}`;
  const primeiroNome = nome.split(" ")[0];
  const saudacao = primeiroNome ? `Olá, ${primeiroNome}! ` : "Olá! ";
  const fim = ` Faça o pagamento o quanto antes e avise o financeiro depois de pagar.`;
  let referencia = "Outro";
  let assunto: string;

  if (c.item === "Coworking") {
    referencia = "Coworking";
    assunto = `O coworking de ${mes} está ${c.atrasada ? "atrasado" : "em aberto"}`;
  } else if (c.item === "CustoLead") {
    referencia = "Custo de lead";
    const detalhe = c.descricao.replace("Custo de leads — ", "").replace(" (fixo)", "");
    assunto = `O custo de leads de ${mes} (${detalhe}) está ${c.atrasada ? "atrasado" : "em aberto"}`;
  } else if (c.item === "TrafegoPago") {
    referencia = "Tráfego pago";
    assunto = `A gestão de tráfego pago de ${mes} está ${c.atrasada ? "atrasada" : "em aberto"}`;
  } else {
    const [nomeDoCusto, parcela] = c.descricao.split(" — ");
    referencia = c.rotulo === "Notebook" ? "Notebook" : "Outro";
    const vence = c.vencimento ? (c.atrasada ? `, que venceu em ${dataBr(c.vencimento)}` : `, com vencimento em ${dataBr(c.vencimento)}`) : "";
    assunto = `A ${parcela ?? "parcela"} de ${nomeDoCusto}${vence}, está ${c.atrasada ? "atrasada" : "em aberto"}`;
  }
  return { mensagem: `${saudacao}${assunto}, no valor de ${moeda(c.valor)}.${fim}`, valor: c.valor, referencia };
}

/** Tipos de custo que a CSS cobra do consultor: vai no aviso no lugar da referência livre. */
const TIPOS_DE_CUSTO = ["Coworking", "Tráfego pago", "Custo de lead", "Notebook", "Outro"];

/**
 * Setor Financeiro de UM consultor: as cobranças da CSS (notebook em parcelas, custo de lead, tráfego pago) e se foram pagas, o contrato e os
 * custos com os prazos, e os avisos de pagamento. Tudo que a CSS cobra do consultor é registrado e editado aqui.
 */
export function SetorFinanceiroConsultorPage() {
  const { consultorId = "" } = useParams();
  const [params, setParams] = useSearchParams();
  const aba = (["cobrancas", "contrato", "avisos"].includes(params.get("aba") ?? "") ? params.get("aba") : "cobrancas") as Aba;
  const [consultor, setConsultor] = useState<{ nome: string; tipo: string | null } | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [versao, setVersao] = useState(0);
  // "Cobrar" numa cobrança em aberto leva ao aviso de pagamento já com a mensagem e o valor sugeridos.
  const [rascunho, setRascunho] = useState<RascunhoDeAviso | null>(null);
  function cobrar(c: Cobranca) {
    setRascunho(rascunhoDaCobranca(c, consultor?.nome ?? ""));
    setParams({ aba: "avisos" }, { replace: true });
  }

  useEffect(() => {
    const controller = new AbortController();
    api
      .get<{ consultorNome: string; tipoContrato: string | null }>(`/crm/setor-financeiro/cobrancas?consultorId=${consultorId}`, controller.signal)
      .then((d) => {
        setConsultor({ nome: d.consultorNome, tipo: d.tipoContrato });
        setErro(null);
      })
      .catch((e) => {
        if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar o consultor.");
      });
    return () => controller.abort();
  }, [consultorId, versao]);

  const ano = new Date().getFullYear();
  return (
    <div className="space-y-4">
      <Link to="/app/crm/setor-financeiro" className="focus-ring inline-flex items-center gap-1 rounded text-sm font-medium text-[var(--brand)] hover:underline">
        <ArrowLeft className="size-4" aria-hidden /> Setor Financeiro
      </Link>

      {erro ? (
        <ErrorState message={erro} onRetry={() => setVersao((n) => n + 1)} />
      ) : !consultor ? (
        <Skeleton className="h-16" />
      ) : (
        <div className="flex items-center gap-3">
          <Avatar nome={consultor.nome} usuarioId={consultorId} className="size-12 text-base" />
          <div>
            <h1 className="text-xl font-bold text-[var(--fg)]">{consultor.nome}</h1>
            <p className="text-sm text-[var(--fg-muted)]">
              {consultor.tipo === "Pj" ? "Consultor PJ" : consultor.tipo === "Clt" ? "Consultor CLT" : "Contrato não definido"} · Setor Financeiro
            </p>
          </div>
        </div>
      )}

      <Tabs
        tabs={[
          { chave: "cobrancas", rotulo: "Cobranças" },
          { chave: "contrato", rotulo: "Contrato e custos" },
          { chave: "avisos", rotulo: "Avisos de pagamento" },
        ]}
        ativa={aba}
        onChange={(k) => setParams(k === "cobrancas" ? {} : { aba: k }, { replace: true })}
      />

      {aba === "cobrancas" && <CobrancasDoConsultor key={`${consultorId}-${versao}`} consultorId={consultorId} onMudou={() => setVersao((n) => n + 1)} onCobrar={cobrar} />}
      {aba === "contrato" && (
        <div className="space-y-4">
          <CustosDoConsultor consultorId={consultorId} onMudou={() => setVersao((n) => n + 1)} />
          <ContratoFinanceiro consultorId={consultorId} ano={ano} mostrarCobrancas={false} onMudou={() => setVersao((n) => n + 1)} />
        </div>
      )}
      {aba === "avisos" && <AvisosDoConsultor consultorId={consultorId} nome={consultor?.nome ?? ""} rascunho={rascunho} onRascunhoUsado={() => setRascunho(null)} />}
    </div>
  );
}

/** Avisos de pagamento do consultor: o consultor recebe notificação (push e dentro do CRM) e vê no card "Avisos importantes" até ser resolvido. */
function AvisosDoConsultor({ consultorId, nome, rascunho, onRascunhoUsado }: { consultorId: string; nome: string; rascunho: RascunhoDeAviso | null; onRascunhoUsado: () => void }) {
  const { notificar } = useToast();
  const [avisos, setAvisos] = useState<AvisoPagamento[] | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [recarregar, setRecarregar] = useState(0);
  const [filtro, setFiltro] = useState<"abertos" | "resolvidos" | "todos">("abertos");
  const [mensagem, setMensagem] = useState("");
  const [valor, setValor] = useState<number | null>(null);
  const [referencia, setReferencia] = useState("");
  const [enviando, setEnviando] = useState(false);
  const [sugerida, setSugerida] = useState(false);

  // Vindo do "Cobrar": preenche a mensagem, o valor e o tipo do custo; o gestor só confere (e edita, se quiser) antes de enviar.
  useEffect(() => {
    if (!rascunho) return;
    setMensagem(rascunho.mensagem);
    setValor(rascunho.valor);
    setReferencia(rascunho.referencia);
    setSugerida(true);
    onRascunhoUsado();
  }, [rascunho, onRascunhoUsado]);

  useEffect(() => {
    const controller = new AbortController();
    api
      .get<AvisoPagamento[]>(`/crm/avisos-pagamento?consultorId=${consultorId}`, controller.signal)
      .then((a) => {
        setAvisos(a);
        setErro(null);
      })
      .catch((e) => {
        if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar os avisos.");
      });
    return () => controller.abort();
  }, [consultorId, recarregar]);
  useAtualizarAoVivo(useCallback(() => setRecarregar((n) => n + 1), []));

  async function enviar() {
    setEnviando(true);
    try {
      await api.post<AvisoPagamento[]>("/crm/avisos-pagamento", { consultorIds: [consultorId], mensagem: mensagem.trim(), valor, referencia: referencia.trim() || null });
      notificar("success", `Aviso enviado para ${nome || "o consultor"}.`);
      setMensagem("");
      setValor(null);
      setReferencia("");
      setSugerida(false);
      setRecarregar((n) => n + 1);
    } catch (e) {
      notificar("error", e instanceof Error ? e.message : "Não foi possível enviar o aviso.");
    } finally {
      setEnviando(false);
    }
  }

  async function agir(caminho: string, ok: string) {
    try {
      await api.put(caminho, {});
      notificar("success", ok);
      setRecarregar((n) => n + 1);
    } catch (e) {
      notificar("error", e instanceof Error ? e.message : "Não foi possível concluir.");
    }
  }

  const lista = (avisos ?? []).filter((a) => filtro === "todos" || (filtro === "abertos" ? a.status === "Aberto" : a.status === "Resolvido"));

  return (
    <div className="grid gap-4 lg:grid-cols-[minmax(0,2fr)_minmax(0,3fr)]">
      <Card className="space-y-3 p-4">
        <h2 className="text-sm font-semibold text-[var(--fg)]">Novo aviso</h2>
        {sugerida && <p className="rounded-lg bg-[var(--brand-soft)] px-3 py-2 text-xs text-[var(--fg)]">Mensagem e valor sugeridos a partir da cobrança em aberto. Confira, edite se quiser e envie.</p>}
        <div>
          <Label htmlFor="aviso-mensagem" required>Mensagem</Label>
          <Textarea id="aviso-mensagem" rows={4} maxLength={1000} value={mensagem} onChange={(e) => setMensagem(e.target.value)} placeholder="Ex.: O coworking de outubro está em aberto. Faça o pagamento até sexta." />
        </div>
        <div className="grid gap-3 sm:grid-cols-2">
          <div>
            <Label htmlFor="aviso-valor">Valor em aberto (opcional)</Label>
            <MoneyInput id="aviso-valor" value={valor} onChange={setValor} />
          </div>
          <div>
            <Label htmlFor="aviso-ref">Tipo do custo cobrado</Label>
            <Select id="aviso-ref" value={referencia} onChange={(e) => setReferencia(e.target.value)}>
              <option value="">Não informar</option>
              {TIPOS_DE_CUSTO.map((t) => <option key={t} value={t}>{t}</option>)}
            </Select>
          </div>
        </div>
        <div className="flex justify-end">
          <Button onClick={enviar} loading={enviando} disabled={mensagem.trim().length < 3}>
            <Send className="size-4" /> Enviar aviso
          </Button>
        </div>
      </Card>

      <Card className="space-y-3 p-4">
        <div className="flex items-center justify-between gap-2">
          <h2 className="text-sm font-semibold text-[var(--fg)]">Avisos enviados</h2>
          <Select aria-label="Filtrar avisos" value={filtro} onChange={(e) => setFiltro(e.target.value as typeof filtro)} className="!w-auto">
            <option value="abertos">Em aberto</option>
            <option value="resolvidos">Resolvidos</option>
            <option value="todos">Todos</option>
          </Select>
        </div>
        {erro ? (
          <ErrorState message={erro} onRetry={() => setRecarregar((n) => n + 1)} />
        ) : !avisos ? (
          <Skeleton className="h-24" />
        ) : lista.length === 0 ? (
          <EmptyState title="Nenhum aviso" description={filtro === "abertos" ? "Não há avisos em aberto." : "Nada por aqui ainda."} />
        ) : (
          <ul className="space-y-2">
            {lista.map((a) => (
              <li key={a.id} className="rounded-xl border border-[var(--border)] p-3 text-sm">
                <div className="flex items-start justify-between gap-2">
                  <div className="min-w-0">
                    <p className="whitespace-pre-wrap break-words text-[var(--fg)]">{a.mensagem}</p>
                    {(a.valor != null || a.referencia) && (
                      <p className="mt-1 text-xs text-[var(--fg-muted)]">
                        {a.valor != null && <span className="font-medium text-[var(--fg)]">{formatarMoeda(a.valor)}</span>}
                        {a.valor != null && a.referencia && " · "}
                        {a.referencia}
                      </p>
                    )}
                    <p className="mt-1 text-xs text-[var(--fg-muted)]">
                      Enviado por {a.enviadoPorNome} · {formatarDataHora(a.criadoEm)}
                      {a.lidoEm && ` · ciente em ${formatarDataHora(a.lidoEm)}`}
                    </p>
                  </div>
                  <div className="flex shrink-0 flex-col items-end gap-2">
                    <Badge variant={a.status === "Aberto" ? "warning" : "success"}>{a.status === "Aberto" ? "Em aberto" : "Resolvido"}</Badge>
                    {a.status === "Aberto" && (
                      <div className="flex gap-2">
                        <Button size="sm" variant="ghost" onClick={() => agir(`/crm/avisos-pagamento/${a.id}/reenviar`, "Notificação enviada de novo ao consultor.")} title="Manda a notificação de novo ao consultor">
                          <BellRing className="size-4" /> Reenviar
                        </Button>
                        <Button size="sm" variant="secondary" onClick={() => agir(`/crm/avisos-pagamento/${a.id}/resolver`, "Aviso marcado como resolvido.")}>
                          <Check className="size-4" /> Resolver
                        </Button>
                      </div>
                    )}
                  </div>
                </div>
              </li>
            ))}
          </ul>
        )}
      </Card>
    </div>
  );
}

/** O endereço antigo (?consultor=<id>) leva à página do consultor, na aba de avisos. */
export function RedirecionarAvisosPagamento() {
  const navigate = useNavigate();
  const [params] = useSearchParams();
  useEffect(() => {
    const id = params.get("consultor");
    navigate(id ? `/app/crm/setor-financeiro/${id}?aba=avisos` : "/app/crm/setor-financeiro", { replace: true });
  }, [navigate, params]);
  return null;
}
