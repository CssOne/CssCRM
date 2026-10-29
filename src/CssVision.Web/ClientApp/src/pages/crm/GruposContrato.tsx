import { ArrowLeft, Copy, Search, Users } from "lucide-react";
import { useCallback, useEffect, useMemo, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { api, isAbortError } from "../../lib/api";
import { formatarMoeda } from "../../lib/format";
import { MesInput } from "../../components/PeriodoInput";
import { Badge, Button, Card, ConfirmDialog, EmptyState, ErrorState, Input, Label, MoneyInput, Select, Skeleton, useToast } from "../../components/ui";

// ---------- Tipos (espelham PortalFinanceiroGruposController) ----------

interface Resumo {
  grupoId: string;
  grupo: string;
  regionalId: string;
  regional: string;
  ativo: boolean;
  consultores: number;
  tipo: "Pj" | "Clt" | null;
  modeloPj: "BonusPorPlaca" | "Recorrencia" | null;
}

interface PerfilDoGrupo {
  vigenteDesde: string;
  tipo: "Pj" | "Clt" | null;
  modeloPj: "BonusPorPlaca" | "Recorrencia" | null;
  transporte: "Nenhum" | "Combustivel" | "Uber" | "Onibus";
  definidoPor: string | null;
}

interface ParametroAtual {
  parametro: string;
  valor: number | null;
  desde: string | null;
  padrao: boolean;
  carenciaMeses?: number | null;
  carenciaPropria?: boolean;
  carenciaPadrao?: number | null;
}

interface ValorDoGrupo {
  id: string;
  parametro: string;
  vigenteDesde: string;
  valor: number;
  observacao: string | null;
  definidoPor: string | null;
}

interface Grupo {
  grupoId: string;
  grupo: string;
  regionalId: string;
  regional: string;
  podeEditar: boolean;
  perfil: PerfilDoGrupo | null;
  historicoPerfil: PerfilDoGrupo[];
  atuais: ParametroAtual[];
  historico: ValorDoGrupo[];
  prazos: { parametro: string; carenciaMeses: number | null; carenciaPadrao: number }[];
  consultores: { consultorId: string; nome: string; desde: string; tipo: "Pj" | "Clt" | null }[];
}

const MESES = ["Jan", "Fev", "Mar", "Abr", "Mai", "Jun", "Jul", "Ago", "Set", "Out", "Nov", "Dez"];
const mesAno = (iso: string) => `${MESES[Number(iso.slice(5, 7)) - 1]}/${iso.slice(0, 4)}`;

const ROTULOS: Record<string, { titulo: string; ajuda: string }> = {
  BonusPorPlaca: { titulo: "Bônus por placa", ajuda: "Valor pago por venda (placa) a quem está no grupo. Cada consultor pode ter o dele no contrato individual." },
  Alimentacao: { titulo: "Alimentação", ajuda: "Valor da alimentação pago por mês a todo CLT do grupo." },
  Passagem: { titulo: "Passagem", ajuda: "Valor da passagem (transporte) pago por mês a todo CLT do grupo." },
  Coworking: { titulo: "Coworking", ajuda: "Custo fixo mensal cobrado de todo PJ do grupo (0 = grupo sem coworking)." },
  TrafegoPago: { titulo: "Tráfego pago", ajuda: "Custo fixo mensal. Só começa a ser cobrado depois do prazo abaixo." },
  CustoLead: { titulo: "Custo por lead", ajuda: "Valor cobrado por lead de tráfego pago (e do Notion) que o consultor pegou no mês. Só começa depois do prazo abaixo." },
  Transporte: { titulo: "Transporte", ajuda: "Combustível: valor por semana. Uber ou ônibus: valor por dia útil (seg–sex)." },
};

const TRANSPORTES: [string, string][] = [["Nenhum", "Sem transporte"], ["Combustivel", "Combustível (por semana)"], ["Uber", "Uber (por dia útil)"], ["Onibus", "Ônibus (por dia útil)"]];

const rotuloDoTipo = (tipo: string | null, modelo?: string | null) =>
  tipo === "Clt" ? "CLT" : tipo === "Pj" ? `PJ · ${modelo === "Recorrencia" ? "recorrência" : "bônus por placa"}` : "Sem contrato";

/**
 * Contrato dos grupos: o que vale para todo consultor de um grupo (tipo, custos fixos, alimentação, passagem...), com vigência mensal. Quem não tem
 * contrato próprio herda o do grupo em que está; cada mês é cobrado pelo grupo em que o consultor estava. Salário é sempre individual.
 */
export function GruposContratoPage() {
  const [params, setParams] = useSearchParams();
  const [lista, setLista] = useState<Resumo[] | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [recarregar, setRecarregar] = useState(0);
  const selecionado = params.get("grupo");
  const [busca, setBusca] = useState("");

  useEffect(() => {
    const controller = new AbortController();
    api
      .get<Resumo[]>("/portal/financeiro/grupos", controller.signal)
      .then((r) => { setLista(r); setErro(null); })
      .catch((e) => { if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar os grupos."); });
    return () => controller.abort();
  }, [recarregar]);

  // Sem grupo escolhido na URL, abre o primeiro: um clique a menos para quem só quer conferir ou ajustar.
  useEffect(() => {
    if (lista && lista.length > 0 && !selecionado) setParams({ grupo: lista[0].grupoId }, { replace: true });
  }, [lista, selecionado, setParams]);

  const porRegional = useMemo(() => {
    const termo = busca.trim().toLowerCase();
    const mapa = new Map<string, Resumo[]>();
    for (const g of lista ?? []) {
      if (termo && !g.grupo.toLowerCase().includes(termo) && !g.regional.toLowerCase().includes(termo)) continue;
      mapa.set(g.regional, [...(mapa.get(g.regional) ?? []), g]);
    }
    return [...mapa.entries()];
  }, [lista, busca]);

  return (
    <div className="space-y-4">
      <div>
        <Link to="/app/crm/setor-financeiro" className="focus-ring inline-flex items-center gap-1 rounded text-xs font-medium text-[var(--brand)]">
          <ArrowLeft className="size-3.5" aria-hidden /> Setor Financeiro
        </Link>
        <h1 className="mt-1 text-xl font-bold text-[var(--fg)]">Contrato dos grupos</h1>
        <p className="text-sm text-[var(--fg-muted)]">
          O que vale para todos de um grupo: tipo de contrato, custos fixos, alimentação e passagem. Quem está no grupo herda sozinho; o salário e qualquer exceção são definidos no contrato de cada consultor.
        </p>
      </div>

      {erro ? (
        <ErrorState message={erro} onRetry={() => setRecarregar((n) => n + 1)} />
      ) : !lista ? (
        <Skeleton className="h-64 rounded-2xl" />
      ) : lista.length === 0 ? (
        <EmptyState title="Nenhum grupo" description="Crie grupos dentro de uma regional para definir o contrato de cada um." />
      ) : (
        <div className="grid gap-4 lg:grid-cols-[18rem_minmax(0,1fr)] lg:items-start">
          <Card className="space-y-3 p-3">
            <div className="relative">
              <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-[var(--fg-muted)]" aria-hidden />
              <Input className="!pl-9" placeholder="Buscar grupo ou regional…" aria-label="Buscar grupo ou regional" value={busca} onChange={(e) => setBusca(e.target.value)} />
            </div>
            {porRegional.length === 0 && <p className="px-2 py-3 text-sm text-[var(--fg-muted)]">Nenhum grupo com essa busca.</p>}
            {porRegional.map(([regional, grupos]) => (
              <div key={regional}>
                <p className="px-2 pb-1 text-[11px] font-semibold uppercase tracking-wide text-[var(--fg-muted)]">{regional}</p>
                <ul className="space-y-1">
                  {grupos.map((g) => (
                    <li key={g.grupoId}>
                      <button
                        type="button"
                        onClick={() => setParams({ grupo: g.grupoId }, { replace: true })}
                        aria-current={selecionado === g.grupoId}
                        className={`focus-ring w-full cursor-pointer rounded-lg px-2 py-2 text-left transition-colors ${selecionado === g.grupoId ? "bg-[var(--brand-soft)]" : "hover:bg-[var(--surface-hover)]"}`}
                      >
                        <span className="flex items-center justify-between gap-2 text-sm font-semibold text-[var(--fg)]">
                          {g.grupo}
                          {!g.ativo && <Badge variant="neutral">inativo</Badge>}
                        </span>
                        <span className="flex items-center justify-between text-xs text-[var(--fg-muted)]">
                          <span>{rotuloDoTipo(g.tipo, g.modeloPj)}</span>
                          <span className="inline-flex items-center gap-1"><Users className="size-3" aria-hidden />{g.consultores}</span>
                        </span>
                      </button>
                    </li>
                  ))}
                </ul>
              </div>
            ))}
          </Card>

          {selecionado ? <ContratoDoGrupo key={selecionado} grupoId={selecionado} outros={(lista ?? []).filter((g) => g.grupoId !== selecionado)} aoMudar={() => setRecarregar((n) => n + 1)} /> : (
            <Card className="p-8"><p className="text-sm text-[var(--fg-muted)]">Escolha um grupo ao lado para ver e definir o contrato dele.</p></Card>
          )}
        </div>
      )}
    </div>
  );
}

function ContratoDoGrupo({ grupoId, outros, aoMudar }: { grupoId: string; outros: Resumo[]; aoMudar: () => void }) {
  const { notificar } = useToast();
  const [dados, setDados] = useState<Grupo | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [excluir, setExcluir] = useState<ValorDoGrupo | null>(null);
  // Cada carga nova reinicia os rascunhos do editor: o que foi salvo vira o ponto de partida.
  const [versao, setVersao] = useState(0);

  const carregar = useCallback((signal?: AbortSignal) => {
    api
      .get<Grupo>(`/portal/financeiro/grupos/${grupoId}`, signal)
      .then((r) => { setDados(r); setVersao((n) => n + 1); setErro(null); })
      .catch((e) => { if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar o grupo."); });
  }, [grupoId]);

  useEffect(() => {
    const controller = new AbortController();
    carregar(controller.signal);
    return () => controller.abort();
  }, [carregar]);

  const falha = (e: unknown) => notificar("error", e instanceof Error ? e.message : "Não foi possível salvar.");
  const aposSalvar = () => { carregar(); aoMudar(); };

  if (erro) return <ErrorState message={erro} onRetry={() => carregar()} />;
  if (!dados) return <Skeleton className="h-64 rounded-2xl" />;

  return (
    <div className="space-y-4">
      <Card className="space-y-3 p-5">
        <div className="flex flex-wrap items-start justify-between gap-2">
          <div>
            <p className="text-xs text-[var(--fg-muted)]">{dados.regional}</p>
            <p className="text-lg font-bold text-[var(--fg)]">{dados.grupo}</p>
          </div>
          <div className="flex flex-wrap items-center gap-1.5">
            {dados.perfil?.tipo ? <Badge variant="brand">{rotuloDoTipo(dados.perfil.tipo, dados.perfil.modeloPj)}</Badge> : <Badge variant="warning">Sem contrato</Badge>}
            <Badge variant="neutral"><Users className="mr-1 inline size-3" aria-hidden />{dados.consultores.length} consultor(es)</Badge>
          </div>
        </div>
        {/* O contrato de relance: o que vale hoje para quem está no grupo (só o que o grupo definiu). */}
        {dados.atuais.some((a) => a.valor !== null && !a.padrao) && (
          <ul className="flex flex-wrap gap-1.5" aria-label="Valores vigentes do grupo">
            {dados.atuais.filter((a) => a.valor !== null && !a.padrao).map((a) => (
              <li key={a.parametro} className="rounded-md bg-[var(--surface-hover)] px-2 py-1 text-xs text-[var(--fg-muted)]">
                {ROTULOS[a.parametro]?.titulo ?? a.parametro} <strong className="text-[var(--fg)] [font-variant-numeric:tabular-nums]">{formatarMoeda(a.valor ?? 0)}</strong>
              </li>
            ))}
          </ul>
        )}
        {!dados.podeEditar && <p className="text-xs text-[var(--fg-muted)]">Só administrador, gestor master e supervisor definem o contrato de um grupo — você só consulta.</p>}
        {dados.podeEditar && outros.length > 0 && <CopiarDeOutroGrupo dados={dados} outros={outros} onSalvo={aposSalvar} onErro={falha} />}
      </Card>

      <EditorDoGrupo key={versao} dados={dados} onSalvo={aposSalvar} onErro={falha} />
      <ConsultoresDoGrupo dados={dados} />
      {dados.historico.length > 0 && (
        <Card className="p-5">
          <p className="mb-2 text-base font-semibold text-[var(--fg)]">Histórico de valores do grupo</p>
          <ul className="divide-y divide-[var(--border)] text-sm">
            {dados.historico.map((h) => (
              <li key={h.id} className="flex flex-wrap items-center justify-between gap-2 py-2">
                <span className="text-[var(--fg)]">
                  <strong>{ROTULOS[h.parametro]?.titulo ?? h.parametro}</strong> · {formatarMoeda(h.valor)} <span className="text-[var(--fg-muted)]">desde {mesAno(h.vigenteDesde)}{h.definidoPor ? ` · ${h.definidoPor}` : ""}</span>
                </span>
                {dados.podeEditar && <Button size="sm" variant="secondary" onClick={() => setExcluir(h)}>Remover</Button>}
              </li>
            ))}
          </ul>
        </Card>
      )}

      <ConfirmDialog
        open={!!excluir}
        danger
        title="Remover lançamento do grupo"
        confirmLabel="Remover"
        message={excluir ? `Remover ${ROTULOS[excluir.parametro]?.titulo ?? excluir.parametro} de ${mesAno(excluir.vigenteDesde)} (${formatarMoeda(excluir.valor)})? O valor anterior do grupo volta a valer para todos.` : ""}
        onConfirm={async () => {
          if (!excluir) return;
          try {
            await api.del(`/portal/financeiro/grupos/${grupoId}/valores/${excluir.id}`);
            setExcluir(null);
            aposSalvar();
          } catch (e) { falha(e); }
        }}
        onCancel={() => setExcluir(null)}
      />
    </div>
  );
}

// ---------- Copiar o contrato de outro grupo ----------

/**
 * Atalho para montar um grupo novo: leva tipo, valores e prazos de outro grupo para este, valendo a partir do mês atual. Nada é apagado: os lançamentos
 * entram por cima, no histórico, e cada um pode ser ajustado ou removido depois.
 */
function CopiarDeOutroGrupo({ dados, outros, onSalvo, onErro }: { dados: Grupo; outros: Resumo[]; onSalvo: () => void; onErro: (e: unknown) => void }) {
  const { notificar } = useToast();
  const [origem, setOrigem] = useState("");
  const [confirmar, setConfirmar] = useState(false);
  const [copiando, setCopiando] = useState(false);
  const escolhido = outros.find((g) => g.grupoId === origem);

  async function copiar() {
    setCopiando(true);
    try {
      const o = await api.get<Grupo>(`/portal/financeiro/grupos/${origem}`);
      const vigenteDesde = `${new Date().toISOString().slice(0, 7)}-01`;
      await api.put(`/portal/financeiro/grupos/${dados.grupoId}/perfil`, {
        vigenteDesde, tipo: o.perfil?.tipo ?? null, modeloPj: o.perfil?.modeloPj ?? null, transporte: o.perfil?.transporte ?? "Nenhum",
      });
      if (o.perfil?.tipo) {
        for (const a of o.atuais.filter((x) => x.valor !== null && !x.padrao)) {
          await api.put(`/portal/financeiro/grupos/${dados.grupoId}/valores`, { parametro: a.parametro, vigenteDesde, valor: a.valor, observacao: `Copiado de ${o.grupo}` });
        }
        for (const pz of o.prazos.filter((x) => x.carenciaMeses !== null)) {
          await api.put(`/portal/financeiro/grupos/${dados.grupoId}/prazos`, { parametro: pz.parametro, carenciaMeses: pz.carenciaMeses });
        }
      }
      notificar("success", `Contrato de ${o.grupo} copiado para ${dados.grupo}.`);
      setConfirmar(false);
      setOrigem("");
      onSalvo();
    } catch (e) { onErro(e); } finally { setCopiando(false); }
  }

  return (
    <div className="filtros-painel flex flex-wrap items-end gap-2 border-t border-[var(--border)] pt-3">
      <div className="w-full sm:w-72">
        <Label htmlFor="gp-copiar">Copiar o contrato de outro grupo</Label>
        <Select id="gp-copiar" value={origem} onChange={(e) => setOrigem(e.target.value)}>
          <option value="">Escolha o grupo de origem…</option>
          {outros.map((g) => <option key={g.grupoId} value={g.grupoId}>{g.regional} · {g.grupo} ({rotuloDoTipo(g.tipo, g.modeloPj)})</option>)}
        </Select>
      </div>
      <Button size="sm" variant="secondary" disabled={!origem} onClick={() => setConfirmar(true)}><Copy className="size-4" /> Copiar</Button>
      <ConfirmDialog
        open={confirmar}
        title="Copiar contrato do grupo"
        confirmLabel="Copiar"
        loading={copiando}
        message={escolhido ? `Copiar tipo, valores e prazos de ${escolhido.grupo} para ${dados.grupo}, valendo a partir do mês atual? Os valores anteriores ficam no histórico.` : ""}
        onConfirm={copiar}
        onCancel={() => setConfirmar(false)}
      />
    </div>
  );
}

// ---------- Editor do contrato do grupo: tipo, valores e prazos, com um único Salvar ----------

interface PrazoRascunho { meses: string; padrao: boolean }

/**
 * Tipo, valores e prazos do grupo num formulário só: tudo se altera nos campos e um botão no fim salva o que mudou (primeiro o tipo, depois valores e
 * prazos). Mudar o tipo salva só o tipo: os valores do novo tipo aparecem em seguida. Tudo vale a partir do mês escolhido.
 */
function EditorDoGrupo({ dados, onSalvo, onErro }: { dados: Grupo; onSalvo: () => void; onErro: (e: unknown) => void }) {
  const { notificar } = useToast();
  const perfil = dados.perfil;
  const hoje = new Date().toISOString().slice(0, 7);
  const [mes, setMes] = useState(hoje);
  const [tipo, setTipo] = useState<string>(perfil?.tipo ?? "");
  const [modelo, setModelo] = useState<string>(perfil?.modeloPj ?? "BonusPorPlaca");
  const [transporte, setTransporte] = useState<string>(perfil?.transporte ?? "Nenhum");
  const [obs, setObs] = useState("");
  const [valores, setValores] = useState<Record<string, number | null>>(() => Object.fromEntries(dados.atuais.map((a) => [a.parametro, a.valor])));
  const [prazos, setPrazos] = useState<Record<string, PrazoRascunho>>(() =>
    Object.fromEntries(dados.atuais.filter((a) => a.carenciaMeses != null).map((a) => [a.parametro, { meses: String(a.carenciaMeses), padrao: false }])));
  const [salvando, setSalvando] = useState(false);

  const tipoMudou = tipo !== (perfil?.tipo ?? "");
  const perfilMudou = tipoMudou || (tipo === "Pj" && (modelo !== (perfil?.modeloPj ?? "BonusPorPlaca") || transporte !== (perfil?.transporte ?? "Nenhum")));
  const valoresMudados = tipoMudou ? [] : dados.atuais.filter((a) => (valores[a.parametro] ?? null) !== a.valor);
  const prazosMudados = tipoMudou ? [] : dados.atuais.filter((a) => {
    const r = prazos[a.parametro];
    return a.carenciaMeses != null && !!r && (r.padrao || r.meses !== String(a.carenciaMeses));
  });
  const total = (perfilMudou ? 1 : 0) + valoresMudados.length + prazosMudados.length;

  function descartar() {
    setTipo(perfil?.tipo ?? "");
    setModelo(perfil?.modeloPj ?? "BonusPorPlaca");
    setTransporte(perfil?.transporte ?? "Nenhum");
    setObs("");
    setValores(Object.fromEntries(dados.atuais.map((a) => [a.parametro, a.valor])));
    setPrazos(Object.fromEntries(dados.atuais.filter((a) => a.carenciaMeses != null).map((a) => [a.parametro, { meses: String(a.carenciaMeses), padrao: false }])));
  }

  async function salvar() {
    for (const a of valoresMudados) {
      if (valores[a.parametro] === null || valores[a.parametro] === undefined) return onErro(new Error(`Informe o valor de ${ROTULOS[a.parametro]?.titulo ?? a.parametro}.`));
    }
    for (const a of prazosMudados) {
      const r = prazos[a.parametro];
      const n = Number(r.meses);
      if (!r.padrao && (r.meses === "" || !Number.isInteger(n) || n < 0 || n > 60)) return onErro(new Error("O prazo deve ficar entre 0 e 60 meses."));
    }
    setSalvando(true);
    try {
      const base = `/portal/financeiro/grupos/${dados.grupoId}`;
      if (perfilMudou) {
        await api.put(`${base}/perfil`, { vigenteDesde: `${mes}-01`, tipo: tipo || null, modeloPj: tipo === "Pj" ? modelo : null, transporte: tipo === "Pj" ? transporte : "Nenhum" });
      }
      for (const a of valoresMudados) {
        await api.put(`${base}/valores`, { parametro: a.parametro, vigenteDesde: `${mes}-01`, valor: valores[a.parametro], observacao: obs || null });
      }
      for (const a of prazosMudados) {
        const r = prazos[a.parametro];
        await api.put(`${base}/prazos`, { parametro: a.parametro, carenciaMeses: r.padrao ? null : Number(r.meses) });
      }
      notificar("success", tipoMudou ? "Tipo de contrato salvo. Agora defina os valores dele." : "Contrato do grupo salvo.");
      onSalvo();
    } catch (e) { onErro(e); } finally { setSalvando(false); }
  }

  return (
    <>
      <Card className="filtros-painel space-y-4 p-5">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <div>
            <p className="text-base font-semibold text-[var(--fg)]">Tipo de contrato do grupo</p>
            <p className="text-xs text-[var(--fg-muted)]">Vale para quem está no grupo e não tem contrato próprio. Tudo o que for alterado vale a partir do mês escolhido.</p>
          </div>
          {perfil?.tipo ? <Badge variant="brand">{rotuloDoTipo(perfil.tipo, perfil.modeloPj)}</Badge> : <Badge variant="warning">Sem contrato</Badge>}
        </div>

        {dados.podeEditar && (
          <div className="grid items-end gap-3 sm:grid-cols-4">
            <div>
              <Label htmlFor="gp-mes">A partir de</Label>
              <MesInput id="gp-mes" value={mes} onChange={setMes} />
            </div>
            <div>
              <Label htmlFor="gp-tipo">Tipo</Label>
              <Select id="gp-tipo" value={tipo} onChange={(e) => setTipo(e.target.value)}>
                <option value="">Sem contrato</option>
                <option value="Pj">PJ</option>
                <option value="Clt">CLT</option>
              </Select>
            </div>
            {tipo === "Pj" && (
              <>
                <div>
                  <Label htmlFor="gp-modelo">Remuneração</Label>
                  <Select id="gp-modelo" value={modelo} onChange={(e) => setModelo(e.target.value)}>
                    <option value="BonusPorPlaca">Bônus por placa</option>
                    <option value="Recorrencia">Recorrência (individual)</option>
                  </Select>
                </div>
                <div>
                  <Label htmlFor="gp-transp">Transporte fixo</Label>
                  <Select id="gp-transp" value={transporte} onChange={(e) => setTransporte(e.target.value)}>
                    {TRANSPORTES.map(([v, r]) => <option key={v} value={v}>{r}</option>)}
                  </Select>
                </div>
              </>
            )}
          </div>
        )}

        {dados.historicoPerfil.length > 1 && (
          <ul className="divide-y divide-[var(--border)] text-sm">
            {dados.historicoPerfil.map((p) => (
              <li key={p.vigenteDesde} className="flex flex-wrap justify-between gap-2 py-1.5">
                <span className="text-[var(--fg)]">{rotuloDoTipo(p.tipo, p.modeloPj)}</span>
                <span className="text-xs text-[var(--fg-muted)]">desde {mesAno(p.vigenteDesde)}{p.definidoPor ? ` · ${p.definidoPor}` : ""}</span>
              </li>
            ))}
          </ul>
        )}
      </Card>

      {tipoMudou ? (
        dados.podeEditar && <p className="rounded-xl border border-dashed border-[var(--border)] px-4 py-3 text-sm text-[var(--fg-muted)]">Salve o novo tipo de contrato para definir os valores dele.</p>
      ) : (
        perfil?.tipo && (
          <Card className="p-5">
            <div className="flex flex-wrap items-end justify-between gap-3">
              <div>
                <p className="text-base font-semibold text-[var(--fg)]">Valores do grupo</p>
                <p className="text-xs text-[var(--fg-muted)]">
                  Valem para todo consultor do grupo a partir do mês escolhido, sem mexer nos meses anteriores. Salário e recorrência não aparecem aqui porque são sempre individuais.
                </p>
              </div>
              {dados.podeEditar && (
                <div className="w-56">
                  <Label htmlFor="gv-obs">Observação (opcional)</Label>
                  <Input id="gv-obs" maxLength={300} placeholder="Vale para os valores alterados" value={obs} onChange={(e) => setObs(e.target.value)} />
                </div>
              )}
            </div>
            <div className="mt-2 divide-y divide-[var(--border)]">
              {dados.atuais.map((a) => (
                <LinhaDoGrupo
                  key={a.parametro} dados={dados} atual={a}
                  valor={valores[a.parametro] ?? null} onValor={(v) => setValores((r) => ({ ...r, [a.parametro]: v }))}
                  prazo={prazos[a.parametro]} onPrazo={(p) => setPrazos((r) => ({ ...r, [a.parametro]: p }))}
                />
              ))}
            </div>
          </Card>
        )
      )}

      {dados.podeEditar && (
        <div className="sticky bottom-3 z-10 flex flex-wrap items-center justify-between gap-2 rounded-2xl border border-[var(--border)] bg-[var(--surface)] px-4 py-3 shadow-[var(--shadow-card)]">
          <p className="text-sm text-[var(--fg-muted)]">
            {total === 0 ? "Nenhuma alteração." : <><strong className="text-[var(--fg)]">{total}</strong> alteração(ões) para salvar, valendo a partir de {mes.slice(5, 7)}/{mes.slice(0, 4)}.</>}
          </p>
          <div className="flex gap-2">
            <Button variant="ghost" onClick={descartar} disabled={total === 0 || salvando}>Descartar</Button>
            <Button onClick={salvar} loading={salvando} disabled={total === 0}>Salvar alterações</Button>
          </div>
        </div>
      )}
    </>
  );
}

function LinhaDoGrupo({ dados, atual, valor, onValor, prazo, onPrazo }: {
  dados: Grupo; atual: ParametroAtual;
  valor: number | null; onValor: (v: number | null) => void;
  prazo?: PrazoRascunho; onPrazo: (p: PrazoRascunho) => void;
}) {
  const info = ROTULOS[atual.parametro] ?? { titulo: atual.parametro, ajuda: "" };
  const alterado = valor !== atual.valor;
  const n = atual.carenciaMeses ?? 0;

  return (
    <div className="grid gap-3 py-4 lg:grid-cols-[minmax(0,1fr)_minmax(0,1.2fr)] lg:items-start">
      <div className="min-w-0">
        <div className="flex flex-wrap items-baseline gap-2">
          <p className="text-sm font-semibold text-[var(--fg)]">{info.titulo}</p>
          <span className="text-base font-bold text-[var(--brand)] [font-variant-numeric:tabular-nums]">{atual.valor === null ? "—" : formatarMoeda(atual.valor)}{atual.parametro === "CustoLead" ? " / lead" : ""}</span>
          {atual.padrao && <Badge variant="neutral">padrão geral</Badge>}
        </div>
        <p className="text-xs text-[var(--fg-muted)]">
          {atual.desde ? `Vigente desde ${mesAno(atual.desde)}. ` : atual.padrao ? "Ainda não definido para este grupo: vale o padrão geral. " : "Ainda não definido para este grupo. "}
          {info.ajuda}
        </p>
        {atual.carenciaMeses != null && (
          <p className="mt-1 text-xs text-[var(--fg-muted)]">
            {n === 0 ? "Cobrado desde a entrada do consultor no grupo." : `Começa ${n} ${n === 1 ? "mês" : "meses"} depois da entrada do consultor no grupo.`}{" "}
            {atual.carenciaPropria ? <strong className="text-[var(--fg)]">Prazo definido para este grupo.</strong> : `Segue o padrão geral (${atual.carenciaPadrao ?? 0} ${atual.carenciaPadrao === 1 ? "mês" : "meses"}).`}
          </p>
        )}
      </div>
      {dados.podeEditar && (
        <div className="filtros-painel flex flex-wrap items-end gap-3">
          <div className="w-40">
            <Label htmlFor={`gv-${atual.parametro}`}>{atual.parametro === "CustoLead" ? "Valor por lead" : "Valor"}</Label>
            <MoneyInput id={`gv-${atual.parametro}`} value={valor} onChange={onValor} />
          </div>
          {prazo && (
            <div className="w-44">
              <Label htmlFor={`gp-${atual.parametro}`}>Começa a cobrar após (meses)</Label>
              <Input
                id={`gp-${atual.parametro}`} type="number" min={0} max={60} step={1}
                value={prazo.padrao ? String(atual.carenciaPadrao ?? 0) : prazo.meses}
                onChange={(e) => onPrazo({ meses: e.target.value, padrao: false })}
              />
              {atual.carenciaPropria && (
                <button type="button" onClick={() => onPrazo({ meses: prazo.meses, padrao: !prazo.padrao })} className="focus-ring mt-1 cursor-pointer rounded text-xs font-semibold text-[var(--brand)] underline">
                  {prazo.padrao ? "Manter o prazo próprio" : "Voltar ao padrão"}
                </button>
              )}
            </div>
          )}
          {alterado && <Badge variant="info">alterado</Badge>}
        </div>
      )}
    </div>
  );
}

// ---------- Quem está no grupo ----------

function ConsultoresDoGrupo({ dados }: { dados: Grupo }) {
  return (
    <Card className="p-5">
      <p className="text-base font-semibold text-[var(--fg)]">Consultores do grupo</p>
      <p className="mb-3 text-xs text-[var(--fg-muted)]">Para trocar alguém de grupo, edite o usuário em Usuários — a troca vale a partir do mês atual e fica no histórico dele.</p>
      {dados.consultores.length === 0 ? (
        <p className="text-sm text-[var(--fg-muted)]">Ninguém neste grupo ainda.</p>
      ) : (
        <ul className="divide-y divide-[var(--border)] text-sm">
          {dados.consultores.map((c) => (
            <li key={c.consultorId} className="flex flex-wrap items-center justify-between gap-2 py-2">
              <Link to={`/app/crm/setor-financeiro/${c.consultorId}?aba=contrato`} className="focus-ring rounded font-medium text-[var(--brand)] hover:underline">{c.nome}</Link>
              <span className="text-xs text-[var(--fg-muted)]">{rotuloDoTipo(c.tipo)} · no grupo desde {c.desde ? mesAno(c.desde) : "—"}</span>
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
}
