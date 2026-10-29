import { History, Trash2 } from "lucide-react";
import { useCallback, useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
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
  /** Individual (definido para o consultor) ou Grupo (herdado do grupo em que ele está). */
  origem?: "Individual" | "Grupo" | null;
  grupo?: string | null;
}

/** Um período do consultor em um grupo (grupoId nulo = fora de qualquer grupo). `ate` nulo = é o atual. */
interface PeriodoDeGrupo {
  grupoId: string | null;
  grupo: string | null;
  regional: string | null;
  desde: string;
  ate: string | null;
}

interface ParametroAtual {
  parametro: string;
  valor: number | null;
  desde: string | null;
  padrao: boolean;
  /** De onde vem o valor: do consultor, do grupo (nome em `grupo`) ou do padrão geral. */
  origem?: "Individual" | "Grupo" | "Padrao" | null;
  grupo?: string | null;
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
  /** CLT: adesão total vendida no mês, faturado líquido (adesões − taxa de instalação do rastreador), contratos fechados e a bonificação da faixa, calculada sozinha. */
  adesoesDoMes?: number;
  faturadoLiquido?: number;
  /** CLT: benefícios pagos no mês. */
  alimentacao?: number;
  passagem?: number;
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
  grupos?: PeriodoDeGrupo[] | null;
}

const MESES = ["Jan", "Fev", "Mar", "Abr", "Mai", "Jun", "Jul", "Ago", "Set", "Out", "Nov", "Dez"];

/** Plano de bonificação do CLT — espelho de ContratoFinanceiroCalculo.FaixasBonificacaoClt (o cálculo real é no servidor). */
const FAIXAS_CLT = [
  { rotulo: "0 a 19", percentual: 0 },
  { rotulo: "20 a 24", percentual: 10 },
  { rotulo: "25 a 29", percentual: 15 },
  { rotulo: "30 a 34", percentual: 20 },
  { rotulo: "35 a 39", percentual: 25 },
  { rotulo: "40 a 49", percentual: 30 },
  { rotulo: "50 ou mais", percentual: 35 },
];

const ROTULOS: Record<string, { titulo: string; ajuda: string; percentual?: boolean }> = {
  BonusPorPlaca: { titulo: "Bônus por placa", ajuda: "Valor pago por venda (placa). Pode mudar de um mês para outro — vale do mês escolhido até o próximo lançamento." },
  Recorrencia: { titulo: "Recorrência do mês", ajuda: "Varia todo mês: vale só no mês escolhido (não se repete sozinha)." },
  Salario: { titulo: "Salário", ajuda: "Salário mensal — vale do mês escolhido até o próximo lançamento." },
  Alimentacao: { titulo: "Alimentação", ajuda: "Valor da alimentação pago ao CLT por mês — vale do mês escolhido até o próximo lançamento." },
  Passagem: { titulo: "Passagem", ajuda: "Valor da passagem (transporte) pago ao CLT por mês — vale do mês escolhido até o próximo lançamento." },
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
  // Cada carga nova reinicia os rascunhos do editor (o que foi salvo passa a ser o ponto de partida).
  const [versao, setVersao] = useState(0);

  const carregar = useCallback(
    (signal?: AbortSignal) => {
      setErro(null);
      api
        .get<Contrato>(`/portal/financeiro/contrato?consultor=${consultorId}&ano=${ano}`, signal)
        .then((d) => { setDados(d); setVersao((n) => n + 1); })
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
      <EditorDoContrato key={versao} dados={dados} onSalvo={aposSalvar} onErro={falha} />
      {dados.podeEditar && dados.perfil.tipo !== "Clt" && <PadroesDoPj onSalvo={aposSalvar} onErro={falha} />}
      {dados.perfil.tipo && <PrevisaoDoAno dados={dados} ano={ano} />}
      {!!dados.grupos?.length && <HistoricoDeGrupos periodos={dados.grupos} />}
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

// ---------- Editor do contrato: tipo, valores e prazos, com um único Salvar ----------

/** Prazo (carência) em edição de um custo fixo: meses digitados ou "voltar ao padrão". */
interface PrazoRascunho { meses: string; padrao: boolean }

/** Valor mostrado no campo (porcentagem em 0–100, o resto como vem). */
const valorNoCampo = (a: ParametroAtual) => (a.valor !== null ? (ROTULOS[a.parametro]?.percentual ? a.valor * 100 : a.valor) : null);

/**
 * Tipo de contrato, valores e prazos do consultor num formulário só: tudo se altera nos campos e um botão no fim salva o que mudou, na ordem certa
 * (primeiro o tipo, depois valores e prazos). Mudar o tipo salva só o tipo: os valores do novo tipo aparecem em seguida.
 */
function EditorDoContrato({ dados, onSalvo, onErro }: { dados: Contrato; onSalvo: () => void; onErro: (e: unknown) => void }) {
  const { notificar } = useToast();
  const { perfil, podeEditar } = dados;
  const hoje = new Date().toISOString().slice(0, 7);
  const inicioOriginal = mesInput(perfil.inicioContrato) || hoje;
  const [tipo, setTipo] = useState<string>(perfil.tipo ?? "");
  const [modelo, setModelo] = useState<string>(perfil.modeloPj ?? "");
  const [inicio, setInicio] = useState(inicioOriginal);
  const [transporte, setTransporte] = useState<string>(perfil.transporte);
  const [mes, setMes] = useState(hoje);
  const [obs, setObs] = useState("");
  const [valores, setValores] = useState<Record<string, number | null>>(() => Object.fromEntries(dados.atuais.map((a) => [a.parametro, valorNoCampo(a)])));
  const [prazos, setPrazos] = useState<Record<string, PrazoRascunho>>(() =>
    Object.fromEntries(dados.atuais.filter((a) => a.carenciaMeses != null).map((a) => [a.parametro, { meses: String(a.carenciaMeses), padrao: false }])));
  const [salvando, setSalvando] = useState(false);

  // O que mudou em relação ao que está salvo. Com o tipo trocado, os valores do tipo antigo não valem mais: ficam de fora.
  const tipoMudou = tipo !== (perfil.tipo ?? "");
  const perfilMudou = tipoMudou || (!!tipo && inicio !== inicioOriginal) || (tipo === "Pj" && (modelo !== (perfil.modeloPj ?? "") || transporte !== perfil.transporte));
  const valoresMudados = tipoMudou ? [] : dados.atuais.filter((a) => (valores[a.parametro] ?? null) !== valorNoCampo(a));
  const prazosMudados = tipoMudou ? [] : dados.atuais.filter((a) => {
    const r = prazos[a.parametro];
    return a.carenciaMeses != null && !!r && (r.padrao || r.meses !== String(a.carenciaMeses));
  });
  const total = (perfilMudou ? 1 : 0) + valoresMudados.length + prazosMudados.length;

  function descartar() {
    setTipo(perfil.tipo ?? "");
    setModelo(perfil.modeloPj ?? "");
    setInicio(inicioOriginal);
    setTransporte(perfil.transporte);
    setObs("");
    setValores(Object.fromEntries(dados.atuais.map((a) => [a.parametro, valorNoCampo(a)])));
    setPrazos(Object.fromEntries(dados.atuais.filter((a) => a.carenciaMeses != null).map((a) => [a.parametro, { meses: String(a.carenciaMeses), padrao: false }])));
  }

  async function salvar() {
    if (tipo && !inicio) return onErro(new Error("Informe o mês de início do contrato."));
    if (tipo === "Pj" && !modelo) return onErro(new Error("Escolha como o consultor PJ é remunerado: bônus por placa ou recorrência."));
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
      const base = `/portal/financeiro/contrato/${dados.consultorId}`;
      if (perfilMudou) await api.put(`${base}/perfil`, { tipo: tipo || null, modeloPj: tipo === "Pj" ? modelo : null, transporte: tipo === "Pj" ? transporte : "Nenhum", inicioContrato: tipo ? `${inicio}-01` : null });
      for (const a of valoresMudados) {
        const info = ROTULOS[a.parametro];
        const v = valores[a.parametro] as number;
        await api.put(`${base}/valores`, { parametro: a.parametro, vigenteDesde: `${mes}-01`, valor: info?.percentual ? v / 100 : v, observacao: obs || null });
      }
      for (const a of prazosMudados) {
        const r = prazos[a.parametro];
        await api.put(`${base}/prazos`, { parametro: a.parametro, carenciaMeses: r.padrao ? null : Number(r.meses) });
      }
      notificar("success", tipoMudou ? "Tipo de contrato salvo. Agora defina os valores dele." : "Contrato salvo.");
      onSalvo();
    } catch (e) { onErro(e); } finally { setSalvando(false); }
  }

  return (
    <>
      <PerfilContrato
        dados={dados} tipo={tipo} onTipo={setTipo} modelo={modelo} onModelo={setModelo}
        inicio={inicio} onInicio={setInicio} transporte={transporte} onTransporte={setTransporte}
      />
      {tipoMudou ? (
        podeEditar && <p className="rounded-xl border border-dashed border-[var(--border)] px-4 py-3 text-sm text-[var(--fg-muted)]">Salve o novo tipo de contrato para definir os valores dele.</p>
      ) : (
        perfil.tipo && (
          <Card className="p-5">
            <div className="flex flex-wrap items-end justify-between gap-3">
              <div>
                <p className="text-base font-semibold text-[var(--fg)]">Valores do contrato</p>
                <p className="text-xs text-[var(--fg-muted)]">Cada valor novo vale a partir do mês escolhido e fica no histórico — assim dá para ver quanto o consultor recebia e quanto vai receber.</p>
              </div>
              {podeEditar && (
                <div className="filtros-painel flex flex-wrap items-end gap-2">
                  <div className="w-36">
                    <Label htmlFor="ct-vigencia">Valores valem a partir de</Label>
                    <MesInput id="ct-vigencia" value={mes} onChange={setMes} />
                  </div>
                  <div className="w-52">
                    <Label htmlFor="ct-obs">Observação (opcional)</Label>
                    <Input id="ct-obs" maxLength={300} placeholder="Vale para os valores alterados" value={obs} onChange={(e) => setObs(e.target.value)} />
                  </div>
                </div>
              )}
            </div>
            <div className="mt-2 divide-y divide-[var(--border)]">
              {dados.atuais.map((a) => (
                <LinhaDeValor
                  key={a.parametro} dados={dados} atual={a}
                  valor={valores[a.parametro] ?? null} onValor={(v) => setValores((r) => ({ ...r, [a.parametro]: v }))}
                  prazo={prazos[a.parametro]} onPrazo={(p) => setPrazos((r) => ({ ...r, [a.parametro]: p }))}
                  onSalvo={onSalvo} onErro={onErro}
                />
              ))}
            </div>
          </Card>
        )
      )}

      {podeEditar && (
        <div className="sticky bottom-3 z-10 flex flex-wrap items-center justify-between gap-2 rounded-2xl border border-[var(--border)] bg-[var(--surface)] px-4 py-3 shadow-[var(--shadow-card)]">
          <p className="text-sm text-[var(--fg-muted)]">
            {total === 0 ? "Nenhuma alteração." : <><strong className="text-[var(--fg)]">{total}</strong> alteração(ões) para salvar{valoresMudados.length > 0 ? ` — valores a partir de ${mes.slice(5, 7)}/${mes.slice(0, 4)}` : ""}.</>}
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

// ---------- Tipo de contrato ----------

function PerfilContrato({ dados, tipo, onTipo, modelo, onModelo, inicio, onInicio, transporte, onTransporte }: {
  dados: Contrato;
  tipo: string; onTipo: (v: string) => void;
  modelo: string; onModelo: (v: string) => void;
  inicio: string; onInicio: (v: string) => void;
  transporte: string; onTransporte: (v: string) => void;
}) {
  const { perfil, podeEditar } = dados;

  const botao = (valor: string, rotulo: string, detalhe: string) => (
    <button
      key={valor}
      type="button"
      disabled={!podeEditar}
      aria-pressed={tipo === valor}
      onClick={() => onTipo(valor)}
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

      {perfil.origem === "Grupo" && (
        <div className="rounded-xl border border-[var(--brand)] bg-[var(--brand-soft)] px-4 py-3 text-sm text-[var(--fg)]">
          <p>
            Este contrato vem do grupo <strong>{perfil.grupo}</strong>: todos do grupo seguem o mesmo contrato e só o salário (e as exceções abaixo) é individual.
            {podeEditar && " Alterar o tipo aqui cria um contrato só deste consultor, que passa a valer por cima do grupo."}
          </p>
          {dados.grupos?.[0]?.grupoId && (
            <Link to={`/app/crm/setor-financeiro/grupos?grupo=${dados.grupos[0].grupoId}`} className="focus-ring mt-1 inline-block rounded text-xs font-semibold text-[var(--brand)] underline">
              Ver o contrato do grupo
            </Link>
          )}
        </div>
      )}

      <div className="flex flex-wrap gap-3">
        {botao("Pj", "PJ", "Recebe a adesão inteira e ganha bônus por placa ou recorrência (um dos dois). Tem custos fixos.")}
        {botao("Clt", "CLT", "Recebe salário, alimentação, passagem e bonificação por produtividade (faixa de contratos do mês). Sem custos fixos.")}
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
                onClick={() => onModelo(v)}
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
        <div className="grid items-end gap-3 sm:grid-cols-2">
          <div>
            <Label htmlFor="ct-inicio">Início do contrato</Label>
            <MesInput id="ct-inicio" value={inicio} disabled={!tipo} onChange={onInicio} />
          </div>
          {tipo === "Pj" && (
            <div>
              <Label htmlFor="ct-transp">Transporte fixo</Label>
              <Select id="ct-transp" value={transporte} onChange={(e) => onTransporte(e.target.value)}>
                {TRANSPORTES.map(([v, r]) => <option key={v} value={v}>{r}</option>)}
              </Select>
            </div>
          )}
        </div>
      )}
      {!podeEditar && perfil.tipo === "Pj" && perfil.transporte !== "Nenhum" && (
        <p className="text-sm text-[var(--fg-muted)]">Transporte fixo: <strong className="text-[var(--fg)]">{TRANSPORTES.find(([v]) => v === perfil.transporte)?.[1]}</strong></p>
      )}
    </Card>
  );
}

// ---------- Uma linha de valor (e prazo) do contrato ----------

function LinhaDeValor({ dados, atual, valor, onValor, prazo, onPrazo, onSalvo, onErro }: {
  dados: Contrato; atual: ParametroAtual;
  valor: number | null; onValor: (v: number | null) => void;
  prazo?: PrazoRascunho; onPrazo: (p: PrazoRascunho) => void;
  onSalvo: () => void; onErro: (e: unknown) => void;
}) {
  const info = ROTULOS[atual.parametro] ?? { titulo: atual.parametro, ajuda: "" };
  const [salvando, setSalvando] = useState(false);
  const [voltarAoGrupo, setVoltarAoGrupo] = useState(false);

  // Consultor que está em um grupo: o valor do grupo vale, a menos que a gestão tenha lançado uma exceção só para ele (salário e recorrência são sempre dele).
  const noGrupo = !!dados.grupos?.[0]?.grupoId;
  const sempreIndividual = atual.parametro === "Salario" || atual.parametro === "Recorrencia";
  const excecao = noGrupo && !sempreIndividual && atual.origem === "Individual";
  const lancamentosProprios = dados.historico.filter((h) => h.parametro === atual.parametro);

  async function usarODoGrupo() {
    setSalvando(true);
    try {
      for (const h of lancamentosProprios) await api.del(`/portal/financeiro/contrato/${dados.consultorId}/valores/${h.id}`);
      setVoltarAoGrupo(false);
      onSalvo();
    } catch (e) { onErro(e); } finally { setSalvando(false); }
  }

  const ajuda = atual.parametro === "Transporte"
    ? dados.perfil.transporte === "Combustivel" ? "Valor do combustível por semana." : dados.perfil.transporte === "Nenhum" ? "Escolha a modalidade de transporte no contrato acima." : "Valor por dia útil (seg–sex)."
    : info.ajuda;
  const original = valorNoCampo(atual);
  const alterado = valor !== original;
  const n = atual.carenciaMeses ?? 0;

  return (
    <div className="grid gap-3 py-4 lg:grid-cols-[minmax(0,1fr)_minmax(0,1.2fr)] lg:items-start">
      <div className="min-w-0">
        <div className="flex flex-wrap items-baseline gap-2">
          <p className="text-sm font-semibold text-[var(--fg)]">{info.titulo}</p>
          <span className="text-base font-bold text-[var(--brand)] [font-variant-numeric:tabular-nums]">{formatarValor(atual.parametro, atual.valor)}{atual.parametro === "CustoLead" ? " / lead" : ""}</span>
          {atual.padrao && <Badge variant="neutral">padrão</Badge>}
          {atual.origem === "Grupo" && <Badge variant="brand">do grupo{atual.grupo ? ` ${atual.grupo}` : ""}</Badge>}
          {excecao && <Badge variant="warning">exceção individual</Badge>}
          {noGrupo && sempreIndividual && <Badge variant="neutral">sempre individual</Badge>}
        </div>
        {excecao && dados.podeEditar && lancamentosProprios.length > 0 && (
          <button type="button" onClick={() => setVoltarAoGrupo(true)} disabled={salvando} className="focus-ring mt-1 cursor-pointer rounded text-xs font-semibold text-[var(--brand)] underline">
            Usar o valor do grupo
          </button>
        )}
        <ConfirmDialog
          open={voltarAoGrupo}
          title="Usar o valor do grupo"
          confirmLabel="Usar o do grupo"
          loading={salvando}
          message={`Remover ${lancamentosProprios.length === 1 ? "o lançamento individual" : `os ${lancamentosProprios.length} lançamentos individuais`} de ${info.titulo} deste consultor? Ele passa a seguir o valor do grupo.`}
          onConfirm={usarODoGrupo}
          onCancel={() => setVoltarAoGrupo(false)}
        />
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
        {atual.carenciaMeses != null && (
          <p className="mt-1 text-xs text-[var(--fg-muted)]">
            {n === 0
              ? "Cobrado desde o início do contrato."
              : atual.inicioCobranca
                ? `Começa a ser cobrado em ${mesAno(atual.inicioCobranca)} — ${n} ${n === 1 ? "mês" : "meses"} depois da entrada.`
                : `Começa ${n} ${n === 1 ? "mês" : "meses"} depois da entrada.`}{" "}
            {atual.carenciaPropria ? <strong className="text-[var(--fg)]">Prazo definido só para este consultor.</strong> : `Segue o padrão de todos (${atual.carenciaPadrao ?? 0} ${atual.carenciaPadrao === 1 ? "mês" : "meses"}).`}
          </p>
        )}
      </div>

      {dados.podeEditar && (
        <div className="filtros-painel flex flex-wrap items-end gap-3">
          <div className="w-40">
            <Label htmlFor={`v-${atual.parametro}`}>{info.percentual ? "Porcentagem" : atual.parametro === "CustoLead" ? "Valor por lead" : "Valor"}</Label>
            {info.percentual
              ? <Input id={`v-${atual.parametro}`} type="number" min={0} max={100} step={0.5} value={valor ?? ""} onChange={(e) => onValor(e.target.value === "" ? null : Number(e.target.value))} className={alterado ? "!border-[var(--brand)]" : ""} />
              : <MoneyInput id={`v-${atual.parametro}`} value={valor} onChange={onValor} />}
          </div>
          {prazo && (
            <div className="w-44">
              <Label htmlFor={`p-${atual.parametro}`}>Começa a cobrar após (meses)</Label>
              <Input
                id={`p-${atual.parametro}`} type="number" min={0} max={60} step={1}
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

// ---------- Padrões de todos os consultores PJ ----------

interface Padrao { parametro: string; valor: number; carenciaMeses: number; valorDefinido: boolean; carenciaDefinida: boolean; definidoPor: string | null }
interface Padroes { podeEditar: boolean; padroes: Padrao[] }

/** Valor e prazo padrão dos custos fixos de TODOS os consultores PJ, num formulário só com um Salvar. Cada consultor pode ter o dele por cima (na linha do contrato). */
function PadroesDoPj({ onSalvo, onErro }: { onSalvo: () => void; onErro: (e: unknown) => void }) {
  const { notificar } = useToast();
  const [dados, setDados] = useState<Padroes | null>(null);
  const [rascunho, setRascunho] = useState<Record<string, { valor: number | null; meses: string }>>({});
  const [salvando, setSalvando] = useState(false);
  const carregar = useCallback(() => {
    api.get<Padroes>("/portal/financeiro/contrato/padroes")
      .then((d) => {
        setDados(d);
        setRascunho(Object.fromEntries(d.padroes.map((p) => [p.parametro, { valor: p.valor, meses: String(p.carenciaMeses) }])));
      })
      .catch(() => setDados(null));
  }, []);
  useEffect(carregar, [carregar]);
  if (!dados) return null;

  const mudados = dados.padroes.filter((p) => {
    const r = rascunho[p.parametro];
    return !!r && (r.valor !== p.valor || r.meses !== String(p.carenciaMeses));
  });

  async function salvar() {
    for (const p of mudados) {
      const r = rascunho[p.parametro];
      const n = Number(r.meses);
      const titulo = ROTULOS[p.parametro]?.titulo ?? p.parametro;
      if (r.valor === null) return onErro(new Error(`Informe o valor de ${titulo}.`));
      if (r.meses === "" || !Number.isInteger(n) || n < 0 || n > 60) return onErro(new Error(`O prazo de ${titulo} deve ficar entre 0 e 60 meses.`));
    }
    setSalvando(true);
    try {
      for (const p of mudados) {
        const r = rascunho[p.parametro];
        await api.put("/portal/financeiro/contrato/padroes", { parametro: p.parametro, valor: r.valor, carenciaMeses: Number(r.meses) });
      }
      notificar("success", "Padrões salvos.");
      carregar();
      onSalvo();
    } catch (e) { onErro(e); } finally { setSalvando(false); }
  }

  return (
    <Card className="p-5">
      <p className="text-base font-semibold text-[var(--fg)]">Padrão de todos os consultores PJ</p>
      <p className="mb-4 text-xs text-[var(--fg-muted)]">
        Valor mensal (no custo de lead, o valor por lead) e prazo para começar a cobrar, iguais para todos os PJ. Para um consultor diferente, altere o valor ou o prazo no contrato dele.{" "}
        {!dados.podeEditar && "Só administrador, gestor master e supervisor alteram o padrão."}
      </p>
      <div className="divide-y divide-[var(--border)]">
        {dados.padroes.map((p) => {
          const info = ROTULOS[p.parametro] ?? { titulo: p.parametro, ajuda: "" };
          const r = rascunho[p.parametro] ?? { valor: p.valor, meses: String(p.carenciaMeses) };
          return (
            <div key={p.parametro} className="grid gap-3 py-4 lg:grid-cols-[minmax(0,1fr)_minmax(0,1.2fr)] lg:items-center">
              <div className="min-w-0">
                <div className="flex flex-wrap items-baseline gap-2">
                  <p className="text-sm font-semibold text-[var(--fg)]">{info.titulo}</p>
                  <span className="text-base font-bold text-[var(--brand)] [font-variant-numeric:tabular-nums]">{formatarMoeda(p.valor)}{p.parametro === "CustoLead" ? " / lead" : ""}</span>
                  <span className="text-xs text-[var(--fg-muted)]">{p.carenciaMeses === 0 ? "desde o início" : `após ${p.carenciaMeses} ${p.carenciaMeses === 1 ? "mês" : "meses"}`}</span>
                  {!p.valorDefinido && !p.carenciaDefinida && <Badge variant="neutral">padrão do sistema</Badge>}
                </div>
                {p.definidoPor && <p className="text-xs text-[var(--fg-muted)]">Definido por {p.definidoPor}.</p>}
              </div>
              {dados.podeEditar && (
                <div className="filtros-painel flex flex-wrap items-end gap-3">
                  <div className="w-40">
                    <Label htmlFor={`pv-${p.parametro}`}>{p.parametro === "CustoLead" ? "Valor por lead" : "Valor mensal"}</Label>
                    <MoneyInput id={`pv-${p.parametro}`} value={r.valor} onChange={(v) => setRascunho((x) => ({ ...x, [p.parametro]: { ...r, valor: v } }))} />
                  </div>
                  <div className="w-44">
                    <Label htmlFor={`pm-${p.parametro}`}>Começa a cobrar após (meses)</Label>
                    <Input id={`pm-${p.parametro}`} type="number" min={0} max={60} step={1} value={r.meses} onChange={(e) => setRascunho((x) => ({ ...x, [p.parametro]: { ...r, meses: e.target.value } }))} />
                  </div>
                </div>
              )}
            </div>
          );
        })}
      </div>
      {dados.podeEditar && (
        <div className="mt-3 flex justify-end gap-2 border-t border-[var(--border)] pt-3">
          <Button variant="ghost" disabled={mudados.length === 0 || salvando} onClick={carregar}>Descartar</Button>
          <Button loading={salvando} disabled={mudados.length === 0} onClick={salvar}>Salvar padrões{mudados.length > 0 ? ` (${mudados.length})` : ""}</Button>
        </div>
      )}
    </Card>
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
        <p className="text-xs text-[var(--fg-muted)]">O que o contrato rende e custa em cada mês. Salário, recorrência e custos fixos contam do início do contrato até o mês atual. No PJ, bônus por placa e recorrência são pagos 2 meses depois (venda de janeiro = pago em março). No CLT, a bonificação = faturado líquido do mês × % da faixa de contratos fechados, calculada sozinha.</p>
      </div>
      {tipo === "Clt" && (
        <div className="border-b border-[var(--border)] px-5 py-3">
          <p className="text-sm font-semibold text-[var(--fg)]">Plano de bonificação por produtividade</p>
          <div className="mt-2 flex flex-wrap gap-1.5 text-xs">
            {FAIXAS_CLT.map((f) => (
              <span key={f.rotulo} className="rounded-md border border-[var(--border)] px-2 py-1 text-[var(--fg-muted)]">
                <strong className="text-[var(--fg)]">{f.rotulo}</strong> contratos · {f.percentual ? `${f.percentual}%` : "sem bonificação"}
              </span>
            ))}
          </div>
          <p className="mt-2 text-xs text-[var(--fg-muted)]">
            Faturado líquido = total das adesões do mês − taxa de instalação do rastreador (único desconto). A % da faixa atingida vale sobre todo o faturado líquido do mês, desde o primeiro contrato. Vendas canceladas não contam.
          </p>
        </div>
      )}
      <table className="w-full min-w-[40rem] text-sm [font-variant-numeric:tabular-nums]">
        <thead>
          <tr className="text-left">
            {(tipo === "Pj" ? ["Mês", dados.perfil.modeloPj === "Recorrencia" ? "Recorrência" : "Bônus por placa", "Custos fixos"] : ["Mês", "Contratos", "Faturado líquido", "% da faixa", "Bonificação", "Salário", "Alimentação", "Passagem", "Total"]).map((h, i) => (
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
                    <td className="px-4 py-2 text-right">{m.vendasDoMes || "—"}</td>
                    <td className="px-4 py-2 text-right" title={`Adesões: ${formatarMoeda(m.adesoesDoMes ?? 0)}`}>{m.faturadoLiquido ? formatarMoeda(m.faturadoLiquido) : "—"}</td>
                    <td className="px-4 py-2 text-right">{m.percentualAdesao ? pct(m.percentualAdesao) : "—"}</td>
                    <td className="px-4 py-2 text-right font-medium text-[var(--fg)]">{m.comissao ? formatarMoeda(m.comissao) : "—"}</td>
                    <td className="px-4 py-2 text-right">{m.salario ? formatarMoeda(m.salario) : "—"}</td>
                    <td className="px-4 py-2 text-right">{m.alimentacao ? formatarMoeda(m.alimentacao) : "—"}</td>
                    <td className="px-4 py-2 text-right">{m.passagem ? formatarMoeda(m.passagem) : "—"}</td>
                    {(() => {
                      const total = (m.comissao ?? 0) + m.salario + (m.alimentacao ?? 0) + (m.passagem ?? 0);
                      return <td className="px-4 py-2 text-right font-bold text-[var(--brand)]">{total ? formatarMoeda(total) : "—"}</td>;
                    })()}
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

/** Por quais grupos o consultor passou: cada mês é cobrado pelo contrato do grupo em que ele estava. */
function HistoricoDeGrupos({ periodos }: { periodos: PeriodoDeGrupo[] }) {
  return (
    <Card className="p-5">
      <p className="text-base font-semibold text-[var(--fg)]">Histórico de grupos</p>
      <p className="mb-3 text-xs text-[var(--fg-muted)]">Cada mês vale o contrato do grupo em que o consultor estava. Trocar de grupo muda a cobrança só a partir do mês da troca.</p>
      <ol className="divide-y divide-[var(--border)]">
        {periodos.map((p) => (
          <li key={p.desde} className="flex flex-wrap items-baseline justify-between gap-2 py-2 text-sm">
            <span className="text-[var(--fg)]">
              <strong>{p.grupo ?? "Sem grupo"}</strong>
              {p.regional ? <span className="text-[var(--fg-muted)]"> · {p.regional}</span> : null}
            </span>
            <span className="text-xs text-[var(--fg-muted)] [font-variant-numeric:tabular-nums]">
              {mesAno(p.desde)} {p.ate ? `até ${mesAno(p.ate)} (exclusive)` : "— atual"}
            </span>
          </li>
        ))}
      </ol>
    </Card>
  );
}

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

