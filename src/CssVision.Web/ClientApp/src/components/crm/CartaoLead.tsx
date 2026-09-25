import { ArrowRightLeft, Clock, Mail, Phone, Trash2, UserCog } from "lucide-react";
import type { DragEvent, MouseEvent, ReactNode } from "react";
import { Link } from "react-router-dom";
import { diasRelativos, formatarDataHora, formatarTelefone } from "../../lib/format";
import type { LeadKanbanCard } from "../../lib/types";
import { Avatar, Badge } from "../ui";

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
  podeGerir,
  podeExcluir,
  podeVerOrigem,
  onAbrir,
  onMover,
  onTrocarResponsavel,
  onExcluir,
  onDragStart,
  onDragEnd,
}: {
  cartao: LeadKanbanCard;
  corColuna: string;
  podeGerir: boolean;
  podeExcluir: boolean;
  podeVerOrigem: boolean;
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
      style={{ borderLeftColor: corColuna }}
      className="group cursor-grab rounded-xl border border-l-[3px] border-[var(--border)] bg-[var(--surface)] p-3 text-sm shadow-sm transition hover:-translate-y-px hover:shadow-md active:cursor-grabbing"
    >
      {/* Nome e ações */}
      <div className="flex items-start justify-between gap-2">
        <Link
          to={`/app/crm/leads/${cartao.leadId}`}
          onClick={(e) => e.stopPropagation()}
          className="line-clamp-2 font-semibold leading-snug text-[var(--fg)] hover:text-[var(--brand)]"
        >
          {cartao.nomeOuRazaoSocial}
        </Link>
        <div className="-mr-1 -mt-0.5 flex shrink-0 items-center transition-opacity sm:opacity-0 sm:group-hover:opacity-100 sm:group-focus-within:opacity-100">
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
        {cartao.oQue && <Badge variant="info">{cartao.oQue}</Badge>}
        {rotuloTipo && <Badge variant={classe === "lead" ? "info" : "brand"}>{rotuloTipo}</Badge>}
        {cartao.indicacao && rotuloTipo !== "Indicação" && <Badge variant="brand">Indicação</Badge>}
        {cartao.migracao && <Badge variant="neutral">Migração</Badge>}
        {podeVerOrigem && cartao.origem && <Badge variant="neutral">{cartao.origem}</Badge>}
        {cartao.temSeguro === true && <Badge variant="warning">Tem seguro</Badge>}
        {cartao.temSeguro === false && <Badge variant="success">Sem seguro</Badge>}
        {cartao.semContato && <Badge variant="warning">Sem contato</Badge>}
        {cartao.arquivado && <Badge variant="neutral">Arquivado</Badge>}
      </div>

      {/* Contato */}
      <div className="mt-2 space-y-0.5 text-xs text-[var(--fg-muted)]">
        {(cartao.telefone || cartao.telefone2) && (
          <p className="flex items-center gap-1.5 truncate">
            <Phone className="size-3 shrink-0" aria-hidden />
            <span className="truncate">
              {[cartao.telefone, cartao.telefone2].filter(Boolean).map((t) => formatarTelefone(t)).join(" · ")}
            </span>
          </p>
        )}
        {cartao.email && (
          <p className="flex items-center gap-1.5">
            <Mail className="size-3 shrink-0" aria-hidden />
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
              className="rounded-md border border-[var(--border)] bg-[var(--surface-hover)] px-1.5 py-px font-mono font-semibold tracking-wider text-[var(--fg)]"
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

      {/* Responsável e chegada */}
      <div className="mt-3 flex items-center justify-between gap-2 border-t border-[var(--border)] pt-2">
        {cartao.responsavelNome ? (
          <div className="flex min-w-0 items-center gap-1.5" title={`Responsável: ${cartao.responsavelNome}`}>
            <Avatar nome={cartao.responsavelNome} fotoUrl={cartao.responsavelFotoUrl} className="size-6 text-[10px] ring-2 ring-[var(--brand-soft)]" />
            <span className="truncate text-xs font-semibold text-[var(--brand)]">{cartao.responsavelNome}</span>
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
