import { Search, SearchX } from "lucide-react";
import { useEffect, useState, type FormEvent } from "react";
import { Link } from "react-router-dom";
import { ApiRequestError, api, toQueryString } from "../../lib/api";
import type { AuditoriaQuadroGrupo, LeadNoQuadro } from "../../lib/types";
import { Badge, Button, Card, Input } from "../ui";

/**
 * Gestão comercial: "Por que este cliente não aparece no quadro?". Pesquisa por nome, e-mail, placa ou telefone e mostra, para cada cliente achado, a etapa, o
 * consultor, a posição dele na coluna (o quadro mostra só os mais recentes de cada coluna) e o motivo provável.
 */
export function DiagnosticoDoQuadroCard() {
  const [busca, setBusca] = useState("");
  const [resultado, setResultado] = useState<LeadNoQuadro[] | null>(null);
  const [consultado, setConsultado] = useState("");
  const [carregando, setCarregando] = useState(false);
  const [erro, setErro] = useState<string | null>(null);
  const [auditoria, setAuditoria] = useState<AuditoriaQuadroGrupo[] | null>(null);
  const [aberto, setAberto] = useState<string | null>(null);

  useEffect(() => {
    api.get<AuditoriaQuadroGrupo[]>("/crm/leads/kanban/auditoria").then(setAuditoria).catch(() => setAuditoria(null));
  }, []);

  async function pesquisar(e: FormEvent) {
    e.preventDefault();
    setErro(null);
    setCarregando(true);
    try {
      setResultado(await api.get<LeadNoQuadro[]>(`/crm/leads/kanban/diagnostico${toQueryString({ busca: busca.trim() })}`));
      setConsultado(busca.trim());
    } catch (ex) {
      setResultado(null);
      setErro(ex instanceof ApiRequestError ? ex.message : "Não foi possível pesquisar agora.");
    } finally {
      setCarregando(false);
    }
  }

  return (
    <Card className="!rounded-2xl p-4">
      <div className="mb-3 flex items-center gap-2.5">
        <span className="flex size-8 items-center justify-center rounded-xl bg-[var(--surface-hover)] text-[var(--fg-muted)]">
          <SearchX className="size-4" aria-hidden />
        </span>
        <div>
          <h2 className="text-[15px] font-bold leading-tight text-[var(--fg)]">Cliente não aparece no quadro?</h2>
          <p className="text-xs text-[var(--fg-muted)]">Pesquise pelo nome, e-mail, placa ou telefone e veja onde ele está e por que não aparece de início.</p>
        </div>
      </div>

      <form onSubmit={pesquisar} className="flex flex-wrap gap-2" noValidate>
        <Input className="min-w-0 flex-1" value={busca} onChange={(e) => setBusca(e.target.value)} placeholder="Ex.: Fabiano Padilha" aria-label="Cliente a pesquisar" />
        <Button type="submit" loading={carregando} disabled={busca.trim().length < 3}>
          <Search className="size-4" aria-hidden /> Pesquisar
        </Button>
      </form>

      {erro && (
        <p role="alert" className="mt-3 rounded-lg bg-[var(--danger-soft)] px-3 py-2 text-sm text-[var(--danger)]">
          {erro}
        </p>
      )}

      {auditoria && (
        <div className="mt-3 space-y-1.5">
          {auditoria.map((g) => (
            <div key={g.codigo} className="rounded-xl bg-[var(--surface-hover)] px-3 py-2 text-sm">
              <button type="button" disabled={g.total === 0} onClick={() => setAberto(aberto === g.codigo ? null : g.codigo)} className="focus-ring flex w-full cursor-pointer items-center justify-between gap-2 text-left disabled:cursor-default">
                <span className="text-[var(--fg)]">{g.titulo}</span>
                <Badge variant={g.total > 0 ? "warning" : "success"}>{g.total.toLocaleString("pt-BR")}</Badge>
              </button>
              {aberto === g.codigo && (
                <div className="mt-2 border-t border-[var(--border)] pt-2">
                  <p className="text-xs text-[var(--fg-muted)]">{g.explicacao}</p>
                  <ul className="mt-1.5 space-y-0.5 text-xs">
                    {g.exemplos.map((x) => (
                      <li key={x.leadId}>
                        <Link to={`/app/crm/leads/${x.leadId}`} className="font-medium text-[var(--fg)] hover:text-[var(--brand)]">{x.nome}</Link>
                        <span className="text-[var(--fg-muted)]"> · {x.consultor ?? "sem responsável"} · {x.etapa ?? "Sem etapa"}</span>
                      </li>
                    ))}
                    {g.total > g.exemplos.length && <li className="text-[var(--fg-muted)]">…e mais {(g.total - g.exemplos.length).toLocaleString("pt-BR")}.</li>}
                  </ul>
                </div>
              )}
            </div>
          ))}
        </div>
      )}

      {resultado && resultado.length === 0 && <p className="mt-3 rounded-xl bg-[var(--surface-hover)] px-3 py-3 text-sm text-[var(--fg-muted)]">Nenhum cliente encontrado para "{consultado}".</p>}

      {resultado && resultado.length > 0 && (
        <ul className="mt-3 space-y-2">
          {resultado.map((l) => (
            <li key={l.leadId} className="rounded-xl border border-[var(--border)] p-3 text-sm">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <Link to={`/app/crm/leads/${l.leadId}`} className="font-semibold text-[var(--fg)] hover:text-[var(--brand)]">
                  {l.nome}
                </Link>
                <div className="flex flex-wrap gap-1.5">
                  <Badge variant={l.etapaAtiva ? "neutral" : "warning"}>{l.etapa ?? "Sem etapa"}</Badge>
                  {l.arquivado && <Badge variant="warning">{l.naLixeira ? "Na lixeira" : "Arquivado"}</Badge>}
                  {l.posicaoNaColuna != null && <Badge variant={l.posicaoNaColuna > l.cartoesPorPagina ? "warning" : "success"}>Posição {l.posicaoNaColuna.toLocaleString("pt-BR")} de {l.totalNaColuna?.toLocaleString("pt-BR")}</Badge>}
                </div>
              </div>
              <p className="mt-1 text-xs text-[var(--fg-muted)]">
                Consultor: {l.consultor ?? "sem responsável"}
                {!l.consultorAtivo ? " (inativo)" : ""} · {l.vendasGanhas} venda(s) ganha(s) · {l.oportunidadesAbertas} oportunidade(s) aberta(s)
              </p>
              <p className="mt-2 text-[var(--fg)]">{l.veredito}</p>
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
}
