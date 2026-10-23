import { ArrowRightLeft, Car, Clock, Mail, Phone, Trash2, UserCog, XCircle } from "lucide-react";
import type { DragEvent, MouseEvent, ReactNode } from "react";
import { Link } from "react-router-dom";
import { diasRelativos, formatarData, formatarDataHora, formatarMoeda, formatarTelefone } from "../../lib/format";
import type { LeadKanbanCard } from "../../lib/types";
import { TIPO_INDICACAO_LEAD } from "../../lib/opcoesLead";
import { Avatar } from "../ui";
import { BotaoWhatsApp } from "./BotaoWhatsApp";

/** Cores das etiquetas do cartão: cada uma mistura a cor com o texto do tema, então vale nos temas claro, escuro e translúcido. */
const COR_ETIQUETA = {
  lead: "#2563eb",
  indicacao: "#9333ea",
  neutra: "var(--fg-muted)",
  aviso: "var(--warning)",
  ok: "var(--success)",
} as const;

/** Etiqueta bem visível: fundo colorido, borda e texto mais fortes que a Badge padrão. */
function Etiqueta({ cor, children }: { cor: keyof typeof COR_ETIQUETA; children: ReactNode }) {
  const c = COR_ETIQUETA[cor];
  return (
    <span
      className="inline-flex items-center rounded-full px-2.5 py-[3px] text-xs font-bold leading-none tracking-tight"
      style={{
        color: `color-mix(in srgb, ${c} 78%, var(--fg))`,
        backgroundColor: `color-mix(in srgb, ${c} 20%, transparent)`,
        boxShadow: `inset 0 0 0 1px color-mix(in srgb, ${c} 38%, transparent)`,
      }}
    >
      {children}
    </span>
  );
}

/** Lead ou indicação — mesma regra das colunas "(Leads)"/"(Indicação)" do quadro. */
export function classificarCartao(cartao: LeadKanbanCard): "lead" | "indicacao" | null {
  const tipo = cartao.tipoIndicacao?.trim().toLowerCase();
  if (tipo === "lead") return "lead";
  // Indicação, Pessoal, Contemplando Sonhos... — qualquer tipo que não seja "Lead".
  if (cartao.criadoManualmente || tipo) return "indicacao";
  return null;
}

function BotaoAcao({ titulo, perigo, onClick, children }: { titulo: string; perigo?: boolean; onClick: () => void; children: ReactNode }) {
  return (
    <button
      type="button"
      title={titulo}
      aria-label={titulo}
      onClick={(e: MouseEvent) => {
        e.stopPropagation();
        onClick();
      }}
      className={`focus-ring cursor-pointer rounded-md p-1 text-[var(--fg-muted)] hover:bg-[var(--surface-hover)] ${
        perigo ? "hover:text-[var(--danger)]" : "hover:text-[var(--fg)]"
      }`}
    >
      {children}
    </button>
  );
}

/**
 * Cartão do quadro de leads: nome e ações no topo, etiquetas, contato, veículo e, no rodapé, a foto
 * e o nome do consultor responsável com a data e a hora em que o lead chegou.
 */
export function CartaoLead({
  cartao,
  corColuna,
  nomeColuna,
  podeGerir,
  podeExcluir,
  podeVerOrigem,
  mostrarAdesao = false,
  mostrarDataVenda = false,
  onAbrir,
  onMover,
  onTrocarResponsavel,
  onExcluir,
  onDragStart,
  onDragEnd,
}: {
  cartao: LeadKanbanCard;
  /** Cor e nome da coluna onde o cartão está: a cor vira um ponto ao lado do nome; o nome aparece ao passar o mouse. */
  corColuna: string;
  nomeColuna: string;
  podeGerir: boolean;
  podeExcluir: boolean;
  podeVerOrigem: boolean;
  /** Coluna "Cotação": mostra o valor da adesão informado ao mover o lead para lá. */
  mostrarAdesao?: boolean;
  /** Colunas "Venda concluída" (Leads e Indicação): mostra a data em que a venda foi fechada. */
  mostrarDataVenda?: boolean;
  onAbrir: () => void;
  onMover: () => void;
  onTrocarResponsavel: () => void;
  onExcluir: () => void;
  onDragStart: (e: DragEvent) => void;
  onDragEnd: () => void;
}) {
  const classe = classificarCartao(cartao);
  const tipo = cartao.tipoIndicacao?.trim();
  const rotuloTipo = classe === "lead" ? "Lead" : classe === "indicacao" ? (tipo && tipo.toLowerCase() !== "lead" ? tipo : "Indicação") : null;
  const temVeiculo = !!(cartao.placa || cartao.estado || cartao.utilidadeVeiculo);

  return (
    <article
      draggable
      onDragStart={onDragStart}
      onDragEnd={onDragEnd}
      onClick={onAbrir}
      className="group relative cursor-grab rounded-xl border border-[var(--border)] bg-[var(--surface)] p-3.5 text-sm shadow-[var(--shadow-card)] transition-[box-shadow,transform] hover:-translate-y-px hover:shadow-[0_8px_24px_-10px_rgb(0_0_0/0.22)] active:cursor-grabbing"
    >
      {/* Nome e ações */}
      <div className="flex items-start justify-between gap-2">
        <div className="flex min-w-0 flex-1 items-start gap-2">
          <span className="mt-[7px] size-2 shrink-0 rounded-full" style={{ backgroundColor: corColuna }} title={nomeColuna} aria-label={`Etapa: ${nomeColuna}`} />
          <Link
            to={`/app/crm/leads/${cartao.leadId}`}
            onClick={(e) => {
              e.stopPropagation();
              // Clique normal abre no painel lateral; Ctrl/⌘+clique continua abrindo numa aba nova.
              if (!e.ctrlKey && !e.metaKey && !e.shiftKey && e.button === 0) {
                e.preventDefault();
                onAbrir();
              }
            }}
            className="line-clamp-2 min-w-0 flex-1 text-[14px] font-semibold leading-snug tracking-tight text-[var(--fg)] hover:text-[var(--brand)]"
          >
            {cartao.nomeOuRazaoSocial}
          </Link>
        </div>
        <div className="-mr-1 flex shrink-0 items-center transition-opacity sm:opacity-0 sm:group-hover:opacity-100 sm:group-focus-within:opacity-100">
          <BotaoAcao titulo="Mover para outra etapa" onClick={onMover}>
            <ArrowRightLeft className="size-3.5" />
          </BotaoAcao>
          {podeGerir && (
            <BotaoAcao titulo="Alterar responsável" onClick={onTrocarResponsavel}>
              <UserCog className="size-3.5" />
            </BotaoAcao>
          )}
          {podeExcluir && (
            <BotaoAcao titulo="Excluir lead" perigo onClick={onExcluir}>
              <Trash2 className="size-3.5" />
            </BotaoAcao>
          )}
        </div>
      </div>

      {/* Etiquetas */}
      <div className="mt-2 flex flex-wrap gap-1">
        {/* Cards de Indicação não mostram a etiqueta "O que?" (AGV...). */}
        {cartao.oQue && classe !== "indicacao" && <Etiqueta cor="lead">{cartao.oQue}</Etiqueta>}
        {rotuloTipo && <Etiqueta cor={classe === "lead" ? "lead" : "indicacao"}>{rotuloTipo}</Etiqueta>}
        {cartao.indicacao && rotuloTipo !== "Indicação" && rotuloTipo !== TIPO_INDICACAO_LEAD && <Etiqueta cor="indicacao">Indicação</Etiqueta>}
        {cartao.veiculoAdicional && <Etiqueta cor="neutra">Veículo adicional</Etiqueta>}
        {cartao.migracao && <Etiqueta cor="neutra">Migração</Etiqueta>}
        {podeVerOrigem && cartao.origem && <Etiqueta cor="neutra">{cartao.origem}</Etiqueta>}
        {cartao.temSeguro === true && <Etiqueta cor="aviso">Tem seguro</Etiqueta>}
        {cartao.temSeguro === false && <Etiqueta cor="ok">Sem seguro</Etiqueta>}
        {cartao.semContato && <Etiqueta cor="aviso">Sem contato</Etiqueta>}
        {cartao.arquivado && <Etiqueta cor="neutra">Arquivado</Etiqueta>}
      </div>

      {/* Cartão perdido: o motivo fica em destaque, logo abaixo das etiquetas. */}
      {(cartao.motivoPerda || nomeColuna === "Perdido") && (
        <div
          className={`mt-2 rounded-lg border px-2.5 py-1.5 text-xs ${
            cartao.motivoPerda
              ? "border-[color-mix(in_srgb,var(--danger)_35%,transparent)] bg-[color-mix(in_srgb,var(--danger)_9%,transparent)]"
              : "border-dashed border-[var(--border)] bg-[var(--surface-hover)]"
          }`}
          title={cartao.motivoPerdaObservacao ? `${cartao.motivoPerda}: ${cartao.motivoPerdaObservacao}` : "Motivo da perda"}
        >
          <p className={`flex items-center gap-1 text-[10px] font-bold uppercase tracking-wide ${cartao.motivoPerda ? "text-[var(--danger)]" : "text-[var(--fg-muted)]"}`}>
            <XCircle className="size-3 shrink-0" aria-hidden /> Motivo da perda
          </p>
          <p className={`mt-0.5 font-semibold ${cartao.motivoPerda ? "text-[var(--fg)]" : "font-normal italic text-[var(--fg-muted)]"}`}>
            {cartao.motivoPerda ?? "Sem motivo informado"}
          </p>
          {cartao.motivoPerdaObservacao && <p className="mt-0.5 line-clamp-3 text-[var(--fg-muted)]">{cartao.motivoPerdaObservacao}</p>}
        </div>
      )}

      {mostrarAdesao && cartao.valorAdesao != null && (
        <p className="mt-2 text-xs text-[var(--fg-muted)]" title="Valor da adesão informado na cotação">
          Adesão: <span className="font-semibold text-[var(--fg)]">{formatarMoeda(cartao.valorAdesao)}</span>
        </p>
      )}

      {mostrarDataVenda && cartao.dataVenda && (
        <p className="mt-2 text-xs text-[var(--fg-muted)]" title="Data em que a venda foi fechada">
          Venda: <span className="font-semibold text-[var(--fg)]">{formatarData(cartao.dataVenda)}</span>
        </p>
      )}

      {/* Contato */}
      <div className="mt-2.5 space-y-1 text-xs text-[var(--fg-muted)]">
        {[cartao.telefone, cartao.telefone2].filter(Boolean).map((t) => (
          <p key={t} className="flex items-center gap-1.5">
            <Phone className="size-3.5 shrink-0" aria-hidden />
            <span className="truncate">{formatarTelefone(t)}</span>
            <BotaoWhatsApp telefone={t} nome={cartao.nomeOuRazaoSocial} leadId={cartao.leadId} etapa={nomeColuna} className="size-6" />
          </p>
        ))}
        {cartao.email && (
          <p className="flex items-center gap-1.5">
            <Mail className="size-3.5 shrink-0" aria-hidden />
            <span className="truncate">{cartao.email}</span>
          </p>
        )}
      </div>

      {/* Veículo */}
      {temVeiculo && (
        <div className="mt-2 flex flex-wrap items-center gap-1.5 text-xs text-[var(--fg-muted)]">
          {cartao.placa && (
            <span
              title="Placa do veículo"
              className="rounded-lg border border-[var(--border)] bg-[var(--surface-hover)] px-2 py-0.5 font-mono font-semibold tracking-wider text-[var(--fg)]"
            >
              {cartao.placa}
            </span>
          )}
          {cartao.estado && <span className="rounded-md bg-[var(--surface-hover)] px-1.5 py-px font-medium">{cartao.estado}</span>}
          {cartao.utilidadeVeiculo && <span className="truncate">{cartao.utilidadeVeiculo}</span>}
        </div>
      )}

      {cartao.tags.length > 0 && (
        <div className="mt-2 flex flex-wrap gap-1">
          {cartao.tags.map((tag) => (
            <span key={tag} className="rounded-full border border-[var(--border)] px-2 py-px text-[11px] text-[var(--fg-muted)]">
              {tag}
            </span>
          ))}
        </div>
      )}

      {podeGerir && cartao.campanha && (
        <p className="mt-2 truncate text-[11px] text-[var(--fg-muted)]" title="Campanha">
          Campanha: {cartao.campanha}
        </p>
      )}

      {cartao.veiculoNaoAtendido && (
        <p className="mt-1.5 flex items-start gap-1 text-[11px] font-medium text-[var(--danger)]" title="Veículo que não atendemos">
          <Car className="mt-px size-3 shrink-0" aria-hidden />
          <span className="line-clamp-2">Veículo: {cartao.veiculoNaoAtendido}</span>
        </p>
      )}

      {/* Responsável e chegada */}
      <div className="mt-3 flex items-center justify-between gap-2 border-t border-[var(--border)] pt-2.5">
        {cartao.responsavelNome ? (
          <div className="flex min-w-0 items-center gap-1.5" title={`Responsável: ${cartao.responsavelNome}`}>
            <Avatar nome={cartao.responsavelNome} fotoUrl={cartao.responsavelFotoUrl} className="size-6 text-[10px]" />
            <span className="truncate text-xs font-medium text-[var(--fg)]">{cartao.responsavelNome}</span>
          </div>
        ) : (
          <span className="text-xs italic text-[var(--fg-muted)]">Sem responsável</span>
        )}
        <span className="flex shrink-0 items-center gap-1 text-[11px] text-[var(--fg-muted)]" title={`Chegou ${diasRelativos(cartao.criadoEm)}`}>
          <Clock className="size-3" aria-hidden />
          {formatarDataHora(cartao.criadoEm)}
        </span>
      </div>
    </article>
  );
}
