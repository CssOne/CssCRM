import { Bell, BellOff, CheckSquare, Mail, MapPin, MessageCircle, Phone, Repeat, StickyNote, Users, type LucideIcon } from "lucide-react";
import { useState, type FormEvent } from "react";
import { DataHoraInput } from "../PeriodoInput";
import { TipoAtividade } from "../../lib/types";
import { Button, Input, Label, Textarea } from "../ui";

export interface ActivityFormValues {
  tipo: TipoAtividade;
  assunto: string;
  descricao: string;
  dataHoraPrevista: string;
  lembreteMinutosAntes: string;
}

const tipoLabel: Record<TipoAtividade, string> = {
  [TipoAtividade.Ligacao]: "Ligação",
  [TipoAtividade.WhatsApp]: "WhatsApp",
  [TipoAtividade.Email]: "E-mail",
  [TipoAtividade.Reuniao]: "Reunião",
  [TipoAtividade.Visita]: "Visita",
  [TipoAtividade.Retorno]: "Retorno",
  [TipoAtividade.Tarefa]: "Tarefa",
  [TipoAtividade.Observacao]: "Observação",
};

export { tipoLabel };

/** Uma cor por tipo de atividade — a mesma no formulário, no mês, na semana, na lista e na legenda da Agenda. */
export const COR_TIPO: Record<TipoAtividade, string> = {
  [TipoAtividade.Ligacao]: "#2563eb",
  [TipoAtividade.WhatsApp]: "#16a34a",
  [TipoAtividade.Email]: "#0ea5e9",
  [TipoAtividade.Reuniao]: "#8b5cf6",
  [TipoAtividade.Visita]: "#f97316",
  [TipoAtividade.Retorno]: "#d97706",
  [TipoAtividade.Tarefa]: "#475569",
  [TipoAtividade.Observacao]: "#94a3b8",
};

export const tipoIcone: Record<TipoAtividade, LucideIcon> = {
  [TipoAtividade.Ligacao]: Phone,
  [TipoAtividade.WhatsApp]: MessageCircle,
  [TipoAtividade.Email]: Mail,
  [TipoAtividade.Reuniao]: Users,
  [TipoAtividade.Visita]: MapPin,
  [TipoAtividade.Retorno]: Repeat,
  [TipoAtividade.Tarefa]: CheckSquare,
  [TipoAtividade.Observacao]: StickyNote,
};

/** Sugestão de assunto no campo, de acordo com o tipo escolhido. */
const assuntoExemplo: Record<TipoAtividade, string> = {
  [TipoAtividade.Ligacao]: "Ex.: Ligar para apresentar a proposta",
  [TipoAtividade.WhatsApp]: "Ex.: Enviar a cotação pelo WhatsApp",
  [TipoAtividade.Email]: "Ex.: Enviar o contrato por e-mail",
  [TipoAtividade.Reuniao]: "Ex.: Reunião para fechar a adesão",
  [TipoAtividade.Visita]: "Ex.: Vistoria do veículo",
  [TipoAtividade.Retorno]: "Ex.: Retornar sobre a cotação",
  [TipoAtividade.Tarefa]: "Ex.: Conferir documentos do cliente",
  [TipoAtividade.Observacao]: "Ex.: Cliente prefere contato à tarde",
};

const LEMBRETES = [
  { valor: "", rotulo: "Sem lembrete" },
  { valor: "15", rotulo: "15 min antes" },
  { valor: "30", rotulo: "30 min antes" },
  { valor: "60", rotulo: "1 h antes" },
  { valor: "1440", rotulo: "1 dia antes" },
];

/** "aaaa-mm-ddThh:mm" no horário local para daqui a `dias` dias, no horário `hora` (ou daqui a 1 h quando sem hora). */
function emDias(dias: number, hora?: number): string {
  const d = new Date();
  if (hora === undefined) d.setHours(d.getHours() + 1, 0, 0, 0);
  else {
    d.setDate(d.getDate() + dias);
    d.setHours(hora, 0, 0, 0);
  }
  return new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
}

const ATALHOS_DATA = [
  { rotulo: "Daqui a 1 h", valor: () => emDias(0) },
  { rotulo: "Hoje 18h", valor: () => emDias(0, 18) },
  { rotulo: "Amanhã 9h", valor: () => emDias(1, 9) },
  { rotulo: "Em 3 dias", valor: () => emDias(3, 9) },
  { rotulo: "Próx. semana", valor: () => emDias(7, 9) },
];

const chipBase = "focus-ring inline-flex cursor-pointer items-center gap-1.5 rounded-full border px-3 py-1.5 text-[13px] font-medium transition-colors";
const chipOn = "border-[var(--brand)] bg-[var(--brand-soft)] text-[var(--brand)]";
const chipOff = "border-[var(--border)] bg-[var(--surface)] text-[var(--fg-muted)] hover:border-[var(--brand)]/50 hover:text-[var(--fg)]";

function agoraLocalIso(): string {
  const agora = new Date(Date.now() - new Date().getTimezoneOffset() * 60000);
  return agora.toISOString().slice(0, 16);
}

/** Converte um ISO vindo do backend (UTC) para o formato que o input datetime-local espera, no fuso local do navegador. */
export function isoParaDatetimeLocal(iso: string): string {
  const data = new Date(iso);
  const local = new Date(data.getTime() - data.getTimezoneOffset() * 60000);
  return local.toISOString().slice(0, 16);
}

export function ActivityForm({
  valoresIniciais,
  modoEdicao,
  salvando,
  onSubmit,
  onCancel,
}: {
  valoresIniciais?: ActivityFormValues;
  modoEdicao?: boolean;
  salvando: boolean;
  onSubmit: (valores: ActivityFormValues) => void;
  onCancel: () => void;
}) {
  const [valores, setValores] = useState<ActivityFormValues>(
    valoresIniciais ?? {
      tipo: TipoAtividade.Ligacao,
      assunto: "",
      descricao: "",
      dataHoraPrevista: agoraLocalIso(),
      lembreteMinutosAntes: "",
    }
  );

  function set<K extends keyof ActivityFormValues>(campo: K, valor: ActivityFormValues[K]) {
    setValores((v) => ({ ...v, [campo]: valor }));
  }

  const [tentou, setTentou] = useState(false);
  const semAssunto = !valores.assunto.trim();
  const semData = !valores.dataHoraPrevista;
  const lembretePadrao = LEMBRETES.some((l) => l.valor === valores.lembreteMinutosAntes);

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setTentou(true);
    if (semAssunto || semData) return;
    onSubmit(valores);
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-5" noValidate>
      <div>
        <Label>Tipo de atividade</Label>
        <div role="radiogroup" aria-label="Tipo de atividade" className="flex flex-wrap gap-1.5">
          {Object.entries(tipoLabel).map(([valor, rotulo]) => {
            const tipo = Number(valor) as TipoAtividade;
            const Icone = tipoIcone[tipo];
            const ativo = valores.tipo === tipo;
            return (
              <button
                key={valor}
                type="button"
                role="radio"
                aria-checked={ativo}
                onClick={() => set("tipo", tipo)}
                style={{ "--cor": COR_TIPO[tipo] } as React.CSSProperties}
                className={`${chipBase} ${
                  ativo
                    ? "border-[var(--cor)] bg-[color-mix(in_srgb,var(--cor)_16%,transparent)] text-[color-mix(in_srgb,var(--cor)_75%,var(--fg))] shadow-[0_0_0_2px_color-mix(in_srgb,var(--cor)_22%,transparent)]"
                    : "border-[color-mix(in_srgb,var(--cor)_30%,var(--border))] bg-[color-mix(in_srgb,var(--cor)_7%,var(--surface))] text-[var(--fg)] hover:bg-[color-mix(in_srgb,var(--cor)_14%,var(--surface))]"
                }`}
              >
                <Icone className="size-3.5 text-[var(--cor)]" aria-hidden /> {rotulo}
              </button>
            );
          })}
        </div>
      </div>

      <div>
        <Label htmlFor="ativ-assunto" required>
          Assunto
        </Label>
        <Input
          id="ativ-assunto"
          value={valores.assunto}
          maxLength={200}
          placeholder={assuntoExemplo[valores.tipo]}
          aria-invalid={tentou && semAssunto}
          onChange={(e) => set("assunto", e.target.value)}
          required
          autoFocus
        />
        {tentou && semAssunto && <p className="mt-1 text-xs text-[var(--danger)]">Informe o assunto da atividade.</p>}
      </div>

      <div>
        <Label htmlFor="ativ-data" required>
          Quando
        </Label>
        <DataHoraInput id="ativ-data" value={valores.dataHoraPrevista} onChange={(v) => set("dataHoraPrevista", v)} />
        <div className="mt-2 flex flex-wrap gap-1.5">
          {ATALHOS_DATA.map((a) => (
            <button
              key={a.rotulo}
              type="button"
              onClick={() => set("dataHoraPrevista", a.valor())}
              className="focus-ring cursor-pointer rounded-full bg-[var(--surface-hover)] px-2.5 py-1 text-xs font-medium text-[var(--fg)] transition-colors hover:bg-[var(--brand-soft)] hover:text-[var(--brand)]"
            >
              {a.rotulo}
            </button>
          ))}
        </div>
        {tentou && semData && <p className="mt-1 text-xs text-[var(--danger)]">Escolha a data e a hora.</p>}
      </div>

      <div>
        <Label>Lembrete</Label>
        <div className="flex flex-wrap gap-1.5">
          {LEMBRETES.map((l) => {
            const ativo = valores.lembreteMinutosAntes === l.valor;
            const Icone = l.valor ? Bell : BellOff;
            return (
              <button key={l.rotulo} type="button" aria-pressed={ativo} onClick={() => set("lembreteMinutosAntes", l.valor)} className={`${chipBase} ${ativo ? chipOn : chipOff}`}>
                <Icone className="size-3.5" aria-hidden /> {l.rotulo}
              </button>
            );
          })}
          {/* Valor fora dos atalhos (ex.: atividade antiga editada): mostra os minutos para não perder o que estava salvo. */}
          {!lembretePadrao && (
            <span className={`${chipBase} ${chipOn} cursor-default`}>
              <Bell className="size-3.5" aria-hidden /> {valores.lembreteMinutosAntes} min antes
            </span>
          )}
        </div>
      </div>

      <div>
        <Label htmlFor="ativ-desc">Descrição</Label>
        <Textarea id="ativ-desc" rows={3} value={valores.descricao} placeholder="Detalhes, o que combinar ou lembrar (opcional)" onChange={(e) => set("descricao", e.target.value)} />
      </div>

      <div className="flex justify-end gap-2 border-t border-[var(--border)] pt-4">
        <Button type="button" variant="secondary" onClick={onCancel} disabled={salvando}>
          Cancelar
        </Button>
        <Button type="submit" loading={salvando}>
          {modoEdicao ? "Salvar" : "Agendar"}
        </Button>
      </div>
    </form>
  );
}
