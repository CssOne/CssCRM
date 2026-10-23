import { History, Trash2 } from "lucide-react";
import { useCallback, useEffect, useMemo, useState } from "react";
import { api, isAbortError } from "../../lib/api";
import { formatarMoeda } from "../../lib/format";
import { Badge, Button, Card, ConfirmDialog, ErrorState, Input, Label, MoneyInput, Select, Skeleton, useToast } from "../ui";
import { CobrancasDoConsultor } from "./CobrancasDoConsultor";
import { MesInput } from "../PeriodoInput";

// ---------- Tipos (espelham PortalFinanceiroContratoController) ----------

interface Perfil {
  tipo: "Pj" | "Clt" | null;
  /** PJ: ganha bônus por placa OU recorrência (um dos dois). */
  modeloPj?: "BonusPorPlaca" | "Recorrencia" | null;
  transporte: "Nenhum" | "Combustivel" | "Uber" | "Onibus";
  inicioContrato: string | null;
}

interface ParametroAtual {
  parametro: string;
  valor: number | null;
  desde: string | null;
  padrao: boolean;
  /** Custos fixos do PJ: meses depois da entrada até começar a cobrar, se a carência é só deste consultor e qual é o padrão. */
  carenciaMeses?: number | null;
  carenciaPropria?: boolean;
  carenciaPadrao?: number | null;
  inicioCobranca?: string | null;
  /** Custo de lead: leads de tráfego pago (e Notion) que o consultor pegou no mês e o custo real (valor por lead x leads). */
  leadsNoMes?: number | null;
  custoRealNoMes?: number | null;
}

interface ValorHistorico {
  id: string;
  parametro: string;
  vigenteDesde: string;
  valor: number;
  observacao: string | null;
  definidoPor: string | null;
  definidoEm: string;
}

interface MesContrato {
  mes: number;
  salario: number;
  recorrencia: number;
  bonusPorPlaca: number;
  percentualAdesao: number;
  /** CLT: adesão total vendida no mês, quantas vendas e a comissão (% × adesão de cada venda), calculada sozinha. */
  adesoesDoMes?: number;
  comissao?: number;
  vendasDoMes?: number;
  custos: { categoria: string; descricao: string; valor: number }[];
}

interface Contrato {
  consultorId: string;
  consultorNome: string | null;
  perfil: Perfil;
  podeEditar: boolean;
  atuais: ParametroAtual[];
  historico: ValorHistorico[];
  meses: MesContrato[];
}

const MESES = ["Jan", "Fev", "Mar", "Abr", "Mai", "Jun", "Jul", "Ago", "Set", "Out", "Nov", "Dez"];

const ROTULOS: Record<string, { titulo: string; ajuda: string; percentual?: boolean }> = {
  BonusPorPlaca: { titulo: "Bônus por placa", ajuda: "Valor pago por venda (placa). Pode mudar de um mês para outro — vale do mês escolhido até o próximo lançamento." },
  Recorrencia: { titulo: "Recorrência do mês", ajuda: "Varia todo mês: vale só no mês escolhido (não se repete sozinha)." },
  Salario: { titulo: "Salário", ajuda: "Salário mensal — vale do mês escolhido até o próximo lançamento." },
  PercentualAdesao: { titulo: "% sobre a adesão", ajuda: "O consultor CLT recebe só essa porcentagem de cada adesão, e não ela inteira.", percentual: true },
  Coworking: { titulo: "Coworking", ajuda: "Custo fixo mensal. Padrão R$ 100,00 até a gestão definir outro valor (0 = sem coworking)." },
  ImpostoMei: { titulo: "Imposto MEI", ajuda: "Custo fixo mensal. Padrão R$ 89,00 até a gestão definir outro valor." },
  TrafegoPago: { titulo: "Tráfego pago", ajuda: "Custo fixo mensal. Só passa a ser cobrado depois do prazo abaixo (padrão: 4 meses depois da entrada)." },
  CustoLead: { titulo: "Custo por lead", ajuda: "Valor cobrado por lead de tráfego pago (e do Notion) que o consultor pegou no mês, igual ao da Gestão comercial. Muda de mês para mês: lance o novo valor a partir do mês. O custo real é o valor × os leads do mês. Só começa depois do prazo abaixo." },
  Transporte: { titulo: "Transporte", ajuda: "Combustível: valor por semana. Uber ou ônibus: valor por dia útil (seg–sex)." },
};

const TRANSPORTES: [string, string][] = [["Nenhum", "Sem transporte fixo"], ["Combustivel", "Combustível (por semana)"], ["Uber", "Uber (por dia útil)"], ["Onibus", "Ônibus (por dia útil)"]];

const mesInput = (iso: string | null | undefined) => (iso ? iso.slice(0, 7) : "");
const mesAno = (iso: string) => `${MESES[Number(iso.slice(5, 7)) - 1]}/${iso.slice(0, 4)}`;
const pct = (v: number) => `${(v * 100).toLocaleString("pt-BR", { maximumFractionDigits: 2 })}%`;

function formatarValor(parametro: string, valor: number | null) {
  if (valor === null) return "—";
  return ROTULOS[parametro]?.percentual ? pct(valor) : formatarMoeda(valor);
}

/**
 * Contrato financeiro do consultor: PJ ou CLT, os valores definidos pela gestão (bônus por placa, recorrência, salário, % da adesão e custos fixos)
 * e o histórico mês a mês de quanto ele recebia e vai receber. Quem DEFINE é a gestão; o consultor só consulta.
 */
export function ContratoFinanceiro({ consultorId, ano, onMudou, mostrarCobrancas = false }: { consultorId: string; ano: number; onMudou?: () => void; mostrarCobrancas?: boolean }) {
  const { notificar } = useToast();
  const [dados, setDados] = useState<Contrato | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [excluir, setExcluir] = useState<ValorHistorico | null>(null);

  const carregar = useCallback(
    (signal?: AbortSignal) => {
      setErro(null);
      api
        .get<Contrato>(`/portal/financeiro/contrato?consultor=${consultorId}&ano=${ano}`, signal)
        .then(setDados)
        .catch((e) => {
          if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar o contrato.");
        });
    },
    [consultorId, ano]
  );

  useEffect(() => {
    setDados(null);
    const controller = new AbortController();
    carregar(controller.signal);
    return () => controller.abort();
  }, [carregar]);

  const falha = (e: unknown) => notificar("error", e instanceof Error ? e.message : "Não foi possível salvar.");
  const aposSalvar = () => {
    carregar();
    onMudou?.();
    window.dispatchEvent(new Event("contrato-financeiro-mudou")); // atualiza os avisos de início de cobrança
  };

  if (erro) return <ErrorState message={erro} onRetry={() => carregar()} />;
  if (!dados) return <Skeleton className="h-56 rounded-2xl" />;

  return (
    <div className="space-y-4">
      <PerfilContrato dados={dados} onSalvo={aposSalvar} onErro={falha} />
      {dados.perfil.tipo && <ValoresDoContrato dados={dados} onSalvo={aposSalvar} onErro={falha} />}
      {dados.podeEditar && <PadroesDoPj onSalvo={aposSalvar} onErro={falha} />}
      {dados.perfil.tipo && <PrevisaoDoAno dados={dados} ano={ano} />}
      {mostrarCobrancas && dados.perfil.tipo === "Pj" && <CobrancasDoConsultor consultorId={consultorId} onMudou={onMudou} />}
      <HistoricoDeValores dados={dados} onExcluir={setExcluir} />

      <ConfirmDialog
        open={!!excluir}
        danger
        title="Remover lançamento"
        confirmLabel="Remover"
        message={excluir ? `Remover ${ROTULOS[excluir.parametro]?.titulo ?? excluir.parametro} de ${mesAno(excluir.vigenteDesde)} (${formatarValor(excluir.parametro, excluir.valor)})? O valor anterior volta a valer.` : ""}
        onConfirm={async () => {
          if (!excluir) return;
          try {
            await api.del(`/portal/financeiro/contrato/${consultorId}/valores/${excluir.id}`);
            setExcluir(null);
            aposSalvar();
          } catch (e) { falha(e); }
        }}
        onCancel={() => setExcluir(null)}
      />
    </div>
  );
}

// ---------- Tipo de contrato ----------

function PerfilContrato({ dados, onSalvo, onErro }: { dados: Contrato; onSalvo: () => void; onErro: (e: unknown) => void }) {
  const { perfil, podeEditar } = dados;
  const hoje = new Date().toISOString().slice(0, 7);
  const [tipo, setTipo] = useState<string>(perfil.tipo ?? "");
  const [modelo, setModelo] = useState<string>(perfil.modeloPj ?? "");
  const [inicio, setInicio] = useState(mesInput(perfil.inicioContrato) || hoje);
  const [transporte, setTransporte] = useState<string>(perfil.transporte);
  const [salvando, setSalvando] = useState(false);

  async function salvar() {
    if (tipo && !inicio) return onErro(new Error("Informe o mês de início do contrato."));
    if (tipo === "Pj" && !modelo) return onErro(new Error("Escolha como o consultor PJ é remunerado: bônus por placa ou recorrência."));
    setSalvando(true);
    try {
      await api.put(`/portal/financeiro/contrato/${dados.consultorId}/perfil`, { tipo: tipo || null, modeloPj: tipo === "Pj" ? modelo : null, transporte: tipo === "Pj" ? transporte : "Nenhum", inicioContrato: tipo ? `${inicio}-01` : null });
      onSalvo();
    } catch (e) { onErro(e); } finally { setSalvando(false); }
  }

  const botao = (valor: string, rotulo: string, detalhe: string) => (
    <button
      key={valor}
      type="button"
      disabled={!podeEditar}
      aria-pressed={tipo === valor}
      onClick={() => setTipo(valor)}
      className={`focus-ring flex-1 rounded-xl border px-4 py-3 text-left transition-colors disabled:cursor-default ${podeEditar ? "cursor-pointer" : ""} ${
        tipo === valor ? "border-[var(--brand)] bg-[var(--brand-soft)]" : "border-[var(--border)] opacity-80"
      }`}
    >
      <span className="block text-sm font-bold text-[var(--fg)]">{rotulo}</span>
      <span className="block text-xs text-[var(--fg-muted)]">{detalhe}</span>
    </button>
  );

  return (
    <Card className="filtros-painel space-y-4 p-5">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <p className="text-base font-semibold text-[var(--fg)]">Tipo de contrato</p>
          <p className="text-xs text-[var(--fg-muted)]">{podeEditar ? "Definido pela gestão. O consultor só consulta." : "Definido pela gestão — você só consulta."}</p>
        </div>
        {perfil.tipo ? <Badge variant="brand">{perfil.tipo === "Pj" ? `PJ · ${perfil.modeloPj === "Recorrencia" ? "recorrência" : "bônus por placa"}` : "CLT"}{perfil.inicioContrato ? ` desde ${mesAno(perfil.inicioContrato)}` : ""}</Badge> : <Badge variant="warning">Não definido</Badge>}
      </div>

      <div className="flex flex-wrap gap-3">
        {botao("Pj", "PJ", "Recebe a adesão inteira e ganha bônus por placa ou recorrência (um dos dois). Tem custos fixos.")}
        {botao("Clt", "CLT", "Recebe salário e uma porcentagem da adesão. Sem custos fixos.")}
      </div>

      {tipo === "Pj" && (
        <div>
          <p className="mb-2 text-sm font-medium text-[var(--fg)]">Como o consultor PJ é remunerado</p>
          <div className="flex flex-wrap gap-3" role="radiogroup" aria-label="Remuneração do PJ">
            {([
              ["BonusPorPlaca", "Bônus por placa", "Um valor por venda (placa). Pode mudar de um mês para outro."],
              ["Recorrencia", "Recorrência", "Um valor por mês. Varia todo mês e vale só no mês lançado."],
            ] as const).map(([v, titulo, detalhe]) => (
              <button
                key={v}
                type="button"
                role="radio"
                aria-checked={modelo === v}
                disabled={!podeEditar}
                onClick={() => setModelo(v)}
                className={`focus-ring flex-1 rounded-xl border px-4 py-3 text-left transition-colors disabled:cursor-default ${podeEditar ? "cursor-pointer" : ""} ${
                  modelo === v ? "border-[var(--brand)] bg-[var(--brand-soft)]" : "border-[var(--border)] opacity-80"
                }`}
              >
                <span className="block text-sm font-bold text-[var(--fg)]">{titulo}</span>
                <span className="block text-xs text-[var(--fg-muted)]">{detalhe}</span>
              </button>
            ))}
          </div>
          <p className="mt-1 text-xs text-[var(--fg-muted)]">É um ou outro: não existem os dois ao mesmo tempo.</p>
        </div>
      )}

      {podeEditar && (
        <div className="grid items-end gap-3 sm:grid-cols-3">
          <div>
            <Label htmlFor="ct-inicio">Início do contrato</Label>
            <MesInput id="ct-inicio" value={inicio} disabled={!tipo} onChange={setInicio} />
          </div>
          {tipo === "Pj" && (
            <div>
              <Label htmlFor="ct-transp">Transporte fixo</Label>
              <Select id="ct-transp" value={transporte} onChange={(e) => setTransporte(e.target.value)}>
                {TRANSPORTES.map(([v, r]) => <option key={v} value={v}>{r}</option>)}
              </Select>
            </div>
          )}
          <div className="sm:col-span-1">
            <Button loading={salvando} onClick={salvar}>Salvar contrato</Button>
          </div>
        </div>
      )}
      {!podeEditar && perfil.tipo === "Pj" && perfil.transporte !== "Nenhum" && (
        <p className="text-sm text-[var(--fg-muted)]">Transporte fixo: <strong className="text-[var(--fg)]">{TRANSPORTES.find(([v]) => v === perfil.transporte)?.[1]}</strong></p>
      )}
    </Card>
  );
}

// ---------- Valores vigentes e lançamento ----------

function ValoresDoContrato({ dados, onSalvo, onErro }: { dados: Contrato; onSalvo: () => void; onErro: (e: unknown) => void }) {
  return (
    <Card className="p-5">
      <p className="text-base font-semibold text-[var(--fg)]">Valores do contrato</p>
      <p className="mb-4 text-xs text-[var(--fg-muted)]">Cada lançamento vale a partir do mês escolhido e fica no histórico — assim dá para ver quanto o consultor recebia e quanto vai receber.</p>
      <div className="divide-y divide-[var(--border)]">
        {dados.atuais.map((a) => (
          <LinhaDeValor key={a.parametro} dados={dados} atual={a} onSalvo={onSalvo} onErro={onErro} />
        ))}
      </div>
    </Card>
  );
}

function LinhaDeValor({ dados, atual, onSalvo, onErro }: { dados: Contrato; atual: ParametroAtual; onSalvo: () => void; onErro: (e: unknown) => void }) {
  const info = ROTULOS[atual.parametro] ?? { titulo: atual.parametro, ajuda: "" };
  const hoje = new Date().toISOString().slice(0, 7);
  const [mes, setMes] = useState(hoje);
  const [valor, setValor] = useState<number | null>(atual.valor !== null ? (info.percentual ? atual.valor * 100 : atual.valor) : null);
  const [obs, setObs] = useState("");
  const [salvando, setSalvando] = useState(false);

  const ajuda = atual.parametro === "Transporte"
    ? dados.perfil.transporte === "Combustivel" ? "Valor do combustível por semana." : dados.perfil.transporte === "Nenhum" ? "Escolha a modalidade de transporte no contrato acima." : "Valor por dia útil (seg–sex)."
    : info.ajuda;

  async function definir() {
    if (valor === null) return onErro(new Error("Informe o valor."));
    setSalvando(true);
    try {
      await api.put(`/portal/financeiro/contrato/${dados.consultorId}/valores`, {
        parametro: atual.parametro,
        vigenteDesde: `${mes}-01`,
        valor: info.percentual ? valor / 100 : valor,
        observacao: obs || null,
      });
      setObs("");
      onSalvo();
    } catch (e) { onErro(e); } finally { setSalvando(false); }
  }

  return (
    <div className="grid gap-3 py-4 lg:grid-cols-[minmax(0,1fr)_minmax(0,1.4fr)] lg:items-center">
      <div className="min-w-0">
        <div className="flex flex-wrap items-baseline gap-2">
          <p className="text-sm font-semibold text-[var(--fg)]">{info.titulo}</p>
          <span className="text-base font-bold text-[var(--brand)] [font-variant-numeric:tabular-nums]">{formatarValor(atual.parametro, atual.valor)}{atual.parametro === "CustoLead" ? " / lead" : ""}</span>
          {atual.padrao && <Badge variant="neutral">padrão</Badge>}
        </div>
        {atual.parametro === "PercentualAdesao" && atual.valor != null && (() => {
          const m = dados.meses.find((x) => x.mes === new Date().getMonth() + 1);
          return m && m.vendasDoMes ? (
            <p className="mt-1 text-xs font-medium text-[var(--fg)]">
              Neste mês: {m.vendasDoMes} venda(s) com {formatarMoeda(m.adesoesDoMes ?? 0)} de adesão × {pct(atual.valor)} ={" "}
              <strong className="text-[var(--brand)]">{formatarMoeda(m.comissao ?? 0)}</strong> de comissão (calculada sozinha)
            </p>
          ) : null;
        })()}
        {atual.parametro === "CustoLead" && atual.leadsNoMes != null && (
          <p className="mt-1 text-xs font-medium text-[var(--fg)]">
            Neste mês: {atual.leadsNoMes.toLocaleString("pt-BR")} lead(s) × {formatarMoeda(atual.valor ?? 0)} ={" "}
            <strong className="text-[var(--brand)]">{formatarMoeda(atual.custoRealNoMes ?? 0)}</strong> de custo real
            {atual.custoRealNoMes == null && " (ainda dentro do prazo, sem cobrança)"}
          </p>
        )}
        <p className="text-xs text-[var(--fg-muted)]">
          {atual.desde ? `Vigente desde ${mesAno(atual.desde)}. ` : atual.padrao ? "Ainda não definido pela gestão. " : atual.parametro === "Recorrencia" ? "Nada lançado neste mês. " : "Ainda não definido. "}
          {ajuda}
        </p>
      </div>

      {dados.podeEditar && (
        <div className="filtros-painel flex flex-wrap items-end gap-2">
          <div className="w-36">
            <Label htmlFor={`m-${atual.parametro}`}>A partir de</Label>
            <MesInput id={`m-${atual.parametro}`} value={mes} onChange={setMes} />
          </div>
          <div className="w-36">
            <Label htmlFor={`v-${atual.parametro}`}>{info.percentual ? "Porcentagem" : atual.parametro === "CustoLead" ? "Valor por lead" : "Valor"}</Label>
            {info.percentual
              ? <Input id={`v-${atual.parametro}`} type="number" min={0} max={100} step={0.5} value={valor ?? ""} onChange={(e) => setValor(e.target.value === "" ? null : Number(e.target.value))} />
              : <MoneyInput id={`v-${atual.parametro}`} value={valor} onChange={setValor} />}
          </div>
          <div className="min-w-32 flex-1">
            <Label htmlFor={`o-${atual.parametro}`}>Observação</Label>
            <Input id={`o-${atual.parametro}`} maxLength={300} placeholder="Opcional" value={obs} onChange={(e) => setObs(e.target.value)} />
          </div>
          <Button size="sm" loading={salvando} onClick={definir}>Definir</Button>
        </div>
      )}

      {atual.carenciaMeses != null && <PrazoDoCusto dados={dados} atual={atual} onSalvo={onSalvo} onErro={onErro} />}
    </div>
  );
}

/** Prazo (carência) do custo fixo deste consultor: meses depois da entrada até começar a pagar. Editável pela gestão, por consultor. */
function PrazoDoCusto({ dados, atual, onSalvo, onErro }: { dados: Contrato; atual: ParametroAtual; onSalvo: () => void; onErro: (e: unknown) => void }) {
  const [meses, setMeses] = useState(String(atual.carenciaMeses ?? 0));
  const [salvando, setSalvando] = useState(false);
  useEffect(() => setMeses(String(atual.carenciaMeses ?? 0)), [atual.carenciaMeses]);

  async function definir(valor: number | null) {
    if (valor !== null && (!Number.isInteger(valor) || valor < 0 || valor > 60)) return onErro(new Error("O prazo deve ficar entre 0 e 60 meses."));
    setSalvando(true);
    try {
      await api.put(`/portal/financeiro/contrato/${dados.consultorId}/prazos`, { parametro: atual.parametro, carenciaMeses: valor });
      onSalvo();
    } catch (e) { onErro(e); } finally { setSalvando(false); }
  }

  const n = atual.carenciaMeses ?? 0;
  return (
    <div className="space-y-2 rounded-xl bg-[var(--surface-hover)] px-4 py-3 lg:col-span-2">
      <p className="text-xs text-[var(--fg-muted)]">
        {n === 0
          ? "Cobrado desde o início do contrato."
          : atual.inicioCobranca
            ? `Começa a ser cobrado em ${mesAno(atual.inicioCobranca)} — ${n} ${n === 1 ? "mês" : "meses"} depois da entrada.`
            : `Começa ${n} ${n === 1 ? "mês" : "meses"} depois da entrada.`}{" "}
        {atual.carenciaPropria ? <strong className="text-[var(--fg)]">Prazo definido só para este consultor.</strong> : `Segue o padrão de todos (${atual.carenciaPadrao ?? 0} ${atual.carenciaPadrao === 1 ? "mês" : "meses"}).`}
      </p>
      {dados.podeEditar && (
        <div className="filtros-painel flex flex-wrap items-end gap-2">
          <div className="w-44">
            <Label htmlFor={`p-${atual.parametro}`}>Começa a cobrar após (meses)</Label>
            <Input id={`p-${atual.parametro}`} type="number" min={0} max={60} step={1} value={meses} onChange={(e) => setMeses(e.target.value)} />
          </div>
          <Button size="sm" loading={salvando} onClick={() => definir(meses === "" ? null : Number(meses))}>Salvar prazo</Button>
          {atual.carenciaPropria && <Button size="sm" variant="secondary" disabled={salvando} onClick={() => definir(null)}>Voltar ao padrão</Button>}
        </div>
      )}
    </div>
  );
}

// ---------- Padrões de todos os consultores PJ ----------

interface Padrao { parametro: string; valor: number; carenciaMeses: number; valorDefinido: boolean; carenciaDefinida: boolean; definidoPor: string | null }
interface Padroes { podeEditar: boolean; padroes: Padrao[] }

/** Valor e prazo padrão dos custos fixos de TODOS os consultores PJ. Cada consultor pode ter o dele por cima (na linha do contrato). */
function PadroesDoPj({ onSalvo, onErro }: { onSalvo: () => void; onErro: (e: unknown) => void }) {
  const [dados, setDados] = useState<Padroes | null>(null);
  const carregar = useCallback(() => {
    api.get<Padroes>("/portal/financeiro/contrato/padroes").then(setDados).catch(() => setDados(null));
  }, []);
  useEffect(carregar, [carregar]);
  if (!dados) return null;

  return (
    <Card className="p-5">
      <p className="text-base font-semibold text-[var(--fg)]">Padrão de todos os consultores PJ</p>
      <p className="mb-4 text-xs text-[var(--fg-muted)]">
        Valor mensal (no custo de lead, o valor por lead) e prazo para começar a cobrar, iguais para todos os PJ. Para um consultor diferente, altere o valor ou o prazo no contrato dele.{" "}
        {!dados.podeEditar && "Só administrador, gestor master e supervisor alteram o padrão."}
      </p>
      <div className="divide-y divide-[var(--border)]">
        {dados.padroes.map((p) => (
          <LinhaDePadrao key={p.parametro} padrao={p} podeEditar={dados.podeEditar} onSalvo={() => { carregar(); onSalvo(); }} onErro={onErro} />
        ))}
      </div>
    </Card>
  );
}

function LinhaDePadrao({ padrao, podeEditar, onSalvo, onErro }: { padrao: Padrao; podeEditar: boolean; onSalvo: () => void; onErro: (e: unknown) => void }) {
  const info = ROTULOS[padrao.parametro] ?? { titulo: padrao.parametro, ajuda: "" };
  const [valor, setValor] = useState<number | null>(padrao.valor);
  const [meses, setMeses] = useState(String(padrao.carenciaMeses));
  const [salvando, setSalvando] = useState(false);
  useEffect(() => { setValor(padrao.valor); setMeses(String(padrao.carenciaMeses)); }, [padrao.valor, padrao.carenciaMeses]);

  async function salvar() {
    if (valor === null) return onErro(new Error("Informe o valor."));
    const n = Number(meses);
    if (meses === "" || !Number.isInteger(n) || n < 0 || n > 60) return onErro(new Error("O prazo deve ficar entre 0 e 60 meses."));
    setSalvando(true);
    try {
      await api.put("/portal/financeiro/contrato/padroes", { parametro: padrao.parametro, valor, carenciaMeses: n });
      onSalvo();
    } catch (e) { onErro(e); } finally { setSalvando(false); }
  }

  return (
    <div className="grid gap-3 py-4 lg:grid-cols-[minmax(0,1fr)_minmax(0,1.4fr)] lg:items-center">
      <div className="min-w-0">
        <div className="flex flex-wrap items-baseline gap-2">
          <p className="text-sm font-semibold text-[var(--fg)]">{info.titulo}</p>
          <span className="text-base font-bold text-[var(--brand)] [font-variant-numeric:tabular-nums]">{formatarMoeda(padrao.valor)}{padrao.parametro === "CustoLead" ? " / lead" : ""}</span>
          <span className="text-xs text-[var(--fg-muted)]">{padrao.carenciaMeses === 0 ? "desde o início" : `após ${padrao.carenciaMeses} ${padrao.carenciaMeses === 1 ? "mês" : "meses"}`}</span>
          {!padrao.valorDefinido && !padrao.carenciaDefinida && <Badge variant="neutral">padrão do sistema</Badge>}
        </div>
        {padrao.definidoPor && <p className="text-xs text-[var(--fg-muted)]">Definido por {padrao.definidoPor}.</p>}
      </div>
      {podeEditar && (
        <div className="filtros-painel flex flex-wrap items-end gap-2">
          <div className="w-36">
            <Label htmlFor={`pv-${padrao.parametro}`}>{padrao.parametro === "CustoLead" ? "Valor por lead" : "Valor mensal"}</Label>
            <MoneyInput id={`pv-${padrao.parametro}`} value={valor} onChange={setValor} />
          </div>
          <div className="w-44">
            <Label htmlFor={`pm-${padrao.parametro}`}>Começa a cobrar após (meses)</Label>
            <Input id={`pm-${padrao.parametro}`} type="number" min={0} max={60} step={1} value={meses} onChange={(e) => setMeses(e.target.value)} />
          </div>
          <Button size="sm" loading={salvando} onClick={salvar}>Salvar padrão</Button>
        </div>
      )}
    </div>
  );
}

// ---------- Mês a mês do ano ----------

function PrevisaoDoAno({ dados, ano }: { dados: Contrato; ano: number }) {
  const tipo = dados.perfil.tipo;
  const mesAtual = new Date().getMonth() + 1;
  const anoAtual = new Date().getFullYear();
  return (
    <Card className="overflow-x-auto">
      <div className="border-b border-[var(--border)] px-5 py-3">
        <p className="text-base font-semibold text-[var(--fg)]">Mês a mês — {ano}</p>
        <p className="text-xs text-[var(--fg-muted)]">O que o contrato rende e custa em cada mês. Salário, recorrência e custos fixos contam do início do contrato até o mês atual. No PJ, bônus por placa e recorrência são pagos 2 meses depois (venda de janeiro = pago em março). No CLT, a comissão é a porcentagem da adesão de cada venda, calculada sozinha.</p>
      </div>
      <table className="w-full min-w-[40rem] text-sm [font-variant-numeric:tabular-nums]">
        <thead>
          <tr className="text-left">
            {(tipo === "Pj" ? ["Mês", dados.perfil.modeloPj === "Recorrencia" ? "Recorrência" : "Bônus por placa", "Custos fixos"] : ["Mês", "Adesões vendidas", "% da adesão", "Comissão", "Salário", "Total"]).map((h, i) => (
              <th key={h} className={`border-b border-[var(--border)] px-4 py-2.5 text-[11px] font-semibold uppercase tracking-wide text-[var(--fg-muted)] ${i ? "text-right" : ""}`}>{h}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {dados.meses.map((m) => {
            const atual = ano === anoAtual && m.mes === mesAtual;
            const fixos = m.custos.reduce((n, c) => n + c.valor, 0);
            return (
              <tr key={m.mes} className={`border-b border-[var(--border)] last:border-0 ${atual ? "bg-[var(--brand-soft)]" : ""}`}>
                <td className="px-4 py-2 font-medium text-[var(--fg)]">{MESES[m.mes - 1]}</td>
                {tipo === "Pj" ? (
                  <td className="px-4 py-2 text-right">{formatarMoeda(dados.perfil.modeloPj === "Recorrencia" ? m.recorrencia : m.bonusPorPlaca)}</td>
                ) : (
                  <>
                    <td className="px-4 py-2 text-right" title={`${m.vendasDoMes ?? 0} venda(s)`}>{m.adesoesDoMes ? formatarMoeda(m.adesoesDoMes) : "—"}</td>
                    <td className="px-4 py-2 text-right">{pct(m.percentualAdesao)}</td>
                    <td className="px-4 py-2 text-right font-medium text-[var(--fg)]">{m.comissao ? formatarMoeda(m.comissao) : "—"}</td>
                    <td className="px-4 py-2 text-right">{m.salario ? formatarMoeda(m.salario) : "—"}</td>
                    <td className="px-4 py-2 text-right font-bold text-[var(--brand)]">{m.comissao || m.salario ? formatarMoeda((m.comissao ?? 0) + m.salario) : "—"}</td>
                  </>
                )}
                {tipo === "Pj" && (
                  <td className="px-4 py-2 text-right" title={m.custos.map((c) => `${c.descricao}: ${formatarMoeda(c.valor)}`).join("\n")}>{fixos ? formatarMoeda(fixos) : "—"}</td>
                )}
              </tr>
            );
          })}
        </tbody>
      </table>
    </Card>
  );
}

// ---------- Histórico ----------

function HistoricoDeValores({ dados, onExcluir }: { dados: Contrato; onExcluir: (v: ValorHistorico) => void }) {
  // Para cada lançamento, o valor que valia antes dele (o "quanto recebia"): o lançamento anterior do mesmo parâmetro.
  const linhas = useMemo(() => {
    const porParametro = new Map<string, ValorHistorico[]>();
    for (const h of dados.historico) porParametro.set(h.parametro, [...(porParametro.get(h.parametro) ?? []), h]);
    return dados.historico.map((h) => {
      const doParametro = porParametro.get(h.parametro)!;
      const anterior = doParametro.filter((x) => x.vigenteDesde < h.vigenteDesde).sort((a, b) => b.vigenteDesde.localeCompare(a.vigenteDesde))[0];
      return { h, anterior };
    });
  }, [dados.historico]);

  return (
    <Card className="overflow-x-auto">
      <div className="flex items-center gap-2 border-b border-[var(--border)] px-5 py-3">
        <History className="size-5 text-[var(--brand)]" aria-hidden />
        <div>
          <p className="text-base font-semibold text-[var(--fg)]">Histórico de valores</p>
          <p className="text-xs text-[var(--fg-muted)]">Quanto o consultor recebia, a partir de quando mudou e quem definiu.</p>
        </div>
      </div>
      {linhas.length === 0 ? (
        <p className="px-5 py-6 text-sm text-[var(--fg-muted)]">Nenhum valor definido ainda.</p>
      ) : (
        <table className="w-full min-w-[44rem] text-sm [font-variant-numeric:tabular-nums]">
          <thead>
            <tr className="text-left">
              {["Valor", "Vigente desde", "Valor", "Antes", "Definido por", "Em", ""].map((h, i) => (
                <th key={`${h}${i}`} className={`border-b border-[var(--border)] px-4 py-2.5 text-[11px] font-semibold uppercase tracking-wide text-[var(--fg-muted)] ${i === 2 || i === 3 ? "text-right" : ""}`}>{i === 0 ? "Item" : h}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {linhas.map(({ h, anterior }) => (
              <tr key={h.id} className="border-b border-[var(--border)] last:border-0 hover:bg-[var(--brand-soft)]">
                <td className="px-4 py-2 font-medium text-[var(--fg)]">{ROTULOS[h.parametro]?.titulo ?? h.parametro}</td>
                <td className="whitespace-nowrap px-4 py-2">{mesAno(h.vigenteDesde)}</td>
                <td className="px-4 py-2 text-right font-semibold">{formatarValor(h.parametro, h.valor)}</td>
                <td className="px-4 py-2 text-right text-[var(--fg-muted)]">{anterior ? formatarValor(h.parametro, anterior.valor) : "—"}</td>
                <td className="px-4 py-2">{h.definidoPor ?? "—"}{h.observacao ? <span className="block text-xs text-[var(--fg-muted)]">{h.observacao}</span> : null}</td>
                <td className="whitespace-nowrap px-4 py-2 text-xs text-[var(--fg-muted)]">{new Date(h.definidoEm).toLocaleDateString("pt-BR")}</td>
                <td className="px-4 py-2 text-right">
                  {dados.podeEditar && (
                    <button type="button" aria-label="Remover lançamento" onClick={() => onExcluir(h)} className="focus-ring cursor-pointer rounded p-1 text-[var(--fg-muted)] hover:text-[var(--danger)]">
                      <Trash2 className="size-4" />
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </Card>
  );
}

