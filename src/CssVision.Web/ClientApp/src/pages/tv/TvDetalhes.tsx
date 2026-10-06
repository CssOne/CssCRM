import { Activity, ArrowLeft, ArrowUpRight, CheckCircle2, CircleDollarSign, Info, RefreshCw, Satellite, ShoppingBag, Target, TrendingUp, X } from "lucide-react";
import type { CSSProperties, ReactNode } from "react";
import { useEffect, useMemo } from "react";
import type { TvAdministrativoIndicador, TvAdministrativoRegistro, TvComercial, TvVendaMes } from "../../lib/types";
import { diaBrasilia, diaMesBr, dataBr, Foto, horaBr, moeda, moedaExata } from "./tvComum";

/** O que a janela de detalhes mostra: cada card do painel abre o seu, e dá para navegar de um para o outro (Voltar). */
export type Detalhe =
  | { tipo: "consultor"; id: string }
  | { tipo: "metrica"; id: "vendasHoje" | "vendasMes" | "valorHoje" | "valorMes" }
  | { tipo: "evolucao" }
  | { tipo: "admin"; id: string }
  | { tipo: "regional"; id: string }
  | { tipo: "venda"; id: string };

/** A janela se fecha sozinha: a TV fica sem ninguém por perto e não pode ficar tapando o painel. */
const FECHA_SOZINHA_MS = 120_000;

const BOTAO_LINHA: CSSProperties = { font: "inherit", color: "inherit", textAlign: "left", cursor: "pointer", width: "100%" };
const ICONES_ADMIN: Record<string, typeof Activity> = { reintegration: RefreshCw, claim: CheckCircle2, tracker: Satellite };

const dataRegistro = (s: string) => (/^\d{4}-\d{2}-\d{2}$/.test(s) ? `${s.slice(8, 10)}/${s.slice(5, 7)}/${s.slice(0, 4)}` : dataBr.format(new Date(s)));
const plural = (n: number, um: string, varios: string) => `${n} ${n === 1 ? um : varios}`;

interface Resumo { id: string; nome: string; fotoUrl?: string | null; regional: string; qtd: number; valor: number }

function agrupar(vendas: TvVendaMes[]): Resumo[] {
  const mapa = new Map<string, Resumo>();
  for (const v of vendas) {
    const atual = mapa.get(v.consultorId) ?? { id: v.consultorId, nome: v.consultor, fotoUrl: v.fotoUrl, regional: v.regional, qtd: 0, valor: 0 };
    atual.qtd += 1;
    atual.valor += v.valor;
    mapa.set(v.consultorId, atual);
  }
  return [...mapa.values()];
}

function Moldura({ eyebrow, titulo, icone: Icone, foto, classe = "", aoFechar, aoVoltar, rodape, children }: {
  eyebrow: string; titulo: string; icone?: typeof Activity; foto?: { nome: string; url?: string | null }; classe?: string;
  aoFechar: () => void; aoVoltar?: () => void; rodape: string; children: ReactNode;
}) {
  return (
    <div className="indicator-overlay" role="presentation" onMouseDown={(e) => { if (e.target === e.currentTarget) aoFechar(); }}>
      <section className={`indicator-modal ${classe}`} role="dialog" aria-modal="true" aria-labelledby="tv-detalhe-titulo">
        <div className="indicator-modal-header">
          {foto ? <Foto nome={foto.nome} url={foto.url} grande /> : <div className="indicator-modal-icon">{Icone && <Icone />}</div>}
          <div><span className="eyebrow">{eyebrow}</span><h2 id="tv-detalhe-titulo">{titulo}</h2></div>
          <div style={{ display: "flex", gap: "0.5em" }}>
            {aoVoltar && <button type="button" className="icon-button" aria-label="Voltar" onClick={aoVoltar}><ArrowLeft /></button>}
            <button type="button" className="icon-button" aria-label="Fechar" onClick={aoFechar}><X /></button>
          </div>
        </div>
        <div className="indicator-modal-body">{children}</div>
        <div className="indicator-modal-footer"><Info /><span>{rodape}</span></div>
      </section>
    </div>
  );
}

function Fatos({ itens }: { itens: Array<[string, string]> }) {
  return <div className="indicator-facts">{itens.map(([rotulo, valor]) => <div key={rotulo}><span>{rotulo}</span><strong>{valor}</strong></div>)}</div>;
}

function Secao({ eyebrow, titulo, total, vazio, children }: { eyebrow: string; titulo: string; total: number; vazio: string; children: ReactNode }) {
  return (
    <div className="indicator-sales">
      <div className="indicator-sales-title"><div><span className="eyebrow">{eyebrow}</span><h3>{titulo}</h3></div><strong>{total}</strong></div>
      {total === 0 ? <div className="indicator-sales-state">{vazio}</div> : <div className="indicator-sales-list">{children}</div>}
    </div>
  );
}

function LinhaVenda({ v, aoAbrir }: { v: TvVendaMes; aoAbrir?: () => void }) {
  const conteudo = (
    <>
      <Foto nome={v.consultor} url={v.fotoUrl} />
      <div className="indicator-sale-person"><strong>{v.consultor}</strong><span>{v.regional}{v.cliente ? ` · ${v.cliente}` : ""}</span></div>
      <div className="indicator-sale-meta"><strong>{diaMesBr.format(new Date(v.dataVenda))}</strong><span>{v.placa || "Sem placa"}</span></div>
      <strong className="indicator-sale-value">{moedaExata.format(v.valor)}</strong>
    </>
  );
  return aoAbrir
    ? <button type="button" className="indicator-sale-row" style={BOTAO_LINHA} onClick={aoAbrir}>{conteudo}</button>
    : <div className="indicator-sale-row">{conteudo}</div>;
}

function LinhaConsultor({ nome, fotoUrl, regional, destaque, detalhe, valor, aoAbrir }: {
  nome: string; fotoUrl?: string | null; regional: string; destaque: string; detalhe: string; valor: string; aoAbrir?: () => void;
}) {
  const conteudo = (
    <>
      <Foto nome={nome} url={fotoUrl} />
      <div className="indicator-sale-person"><strong>{nome}</strong><span>{regional}</span></div>
      <div className="indicator-sale-meta"><strong>{destaque}</strong><span>{detalhe}</span></div>
      <strong className="indicator-sale-value">{valor}</strong>
    </>
  );
  return aoAbrir
    ? <button type="button" className="indicator-sale-row" style={BOTAO_LINHA} onClick={aoAbrir}>{conteudo}</button>
    : <div className="indicator-sale-row">{conteudo}</div>;
}

export function TvDetalhes({ dados, detalhe, mediaPorDia, projecao, aoAbrir, aoVoltar, aoFechar }: {
  dados: TvComercial; detalhe: Detalhe; mediaPorDia: number; projecao: number;
  aoAbrir: (d: Detalhe) => void; aoVoltar?: () => void; aoFechar: () => void;
}) {
  useEffect(() => {
    const tecla = (e: KeyboardEvent) => { if (e.key === "Escape") aoFechar(); };
    window.addEventListener("keydown", tecla);
    const id = window.setTimeout(aoFechar, FECHA_SOZINHA_MS);
    return () => { window.removeEventListener("keydown", tecla); window.clearTimeout(id); };
  }, [aoFechar, detalhe]);

  const vendas = useMemo(() => dados.vendasDoMes ?? [], [dados.vendasDoMes]);
  const hoje = diaBrasilia.format(new Date());
  const vendasHoje = useMemo(() => vendas.filter((v) => diaBrasilia.format(new Date(v.dataVenda)) === hoje), [vendas, hoje]);
  const atualizado = `Atualizado em ${new Date(dados.atualizadoEm).toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit", second: "2-digit" })}`;
  const abrirConsultor = (id: string) => () => aoAbrir({ tipo: "consultor", id });
  const props = { aoFechar, aoVoltar };

  // ---------- consultor ----------
  if (detalhe.tipo === "consultor") {
    const r = dados.rankingConsultores.find((x) => x.consultorId === detalhe.id);
    const c = dados.rankingConversao.find((x) => x.consultorId === detalhe.id);
    const dele = vendas.filter((v) => v.consultorId === detalhe.id);
    const nome = r?.nome ?? c?.nome ?? dele[0]?.consultor ?? "Consultor";
    const foto = r?.fotoUrl ?? c?.fotoUrl ?? dele[0]?.fotoUrl;
    const regional = r?.regional ?? c?.regional ?? dele[0]?.regional ?? "Sem regional";
    const hojeDele = dele.filter((v) => diaBrasilia.format(new Date(v.dataVenda)) === hoje).length;
    return (
      <Moldura eyebrow="DETALHES DO CONSULTOR" titulo={nome} foto={{ nome, url: foto }} rodape={`Regional ${regional} · ${atualizado}`} {...props}>
        <span className="indicator-value-label">ADESÃO NO MÊS</span>
        <strong className="indicator-value">{moedaExata.format(r?.valorVendido ?? 0)}</strong>
        <Fatos itens={[
          ["Vendas no mês", String(r?.quantidadeVendas ?? 0)],
          ["Vendas hoje", String(hojeDele)],
          ["Posição no ranking", r ? `${r.posicao}º` : "—"],
          ["Meta de vendas", r?.quantidadeMeta ? `${r.quantidadeVendas} / ${r.quantidadeMeta}` : "Não cadastrada"],
          ["% da meta", r?.percentualMeta == null ? "—" : `${r.percentualMeta.toFixed(0)}%`],
          ["Leads recebidos", c ? String(c.leadsAtendidos) : "—"],
          ["Conversão", c ? `${c.taxaConversao.toFixed(1)}%` : "—"],
          ["Leads perdidos", c ? String(c.leadsPerdidos) : "—"],
          ["Ticket médio de adesão", r && r.quantidadeVendas ? moeda.format(r.valorVendido / r.quantidadeVendas) : "—"],
        ]} />
        <Secao eyebrow="COMPOSIÇÃO DO DESEMPENHO" titulo="Vendas do mês" total={dele.length} vazio="Nenhuma venda deste consultor neste período.">
          {dele.map((v) => <LinhaVenda key={v.vendaId} v={v} aoAbrir={() => aoAbrir({ tipo: "venda", id: v.vendaId })} />)}
        </Secao>
      </Moldura>
    );
  }

  // ---------- cards de resumo (vendas/valor, hoje/mês) ----------
  if (detalhe.tipo === "metrica") {
    const cfg = {
      vendasHoje: { titulo: "Vendas hoje", icone: ShoppingBag, rotulo: "VENDAS HOJE", valor: String(dados.resumo.vendasHoje), lista: vendasHoje, ordem: "qtd" as const },
      vendasMes: { titulo: "Vendas no mês", icone: TrendingUp, rotulo: "VENDAS NO MÊS", valor: String(dados.resumo.vendasNoMes), lista: vendas, ordem: "qtd" as const },
      valorHoje: { titulo: "Valor hoje", icone: CircleDollarSign, rotulo: "ADESÃO HOJE", valor: moedaExata.format(dados.resumo.valorHoje), lista: vendasHoje, ordem: "valor" as const },
      valorMes: { titulo: "Valor no mês", icone: ArrowUpRight, rotulo: "ADESÃO NO MÊS", valor: moedaExata.format(dados.resumo.valorNoMes), lista: vendas, ordem: "valor" as const },
    }[detalhe.id];
    const grupos = agrupar(cfg.lista).sort((a, b) => (cfg.ordem === "qtd" ? b.qtd - a.qtd || b.valor - a.valor : b.valor - a.valor || b.qtd - a.qtd));
    return (
      <Moldura eyebrow="DETALHES DO INDICADOR" titulo={cfg.titulo} icone={cfg.icone} rodape={`Somente vendas confirmadas · ${atualizado}`} {...props}>
        <span className="indicator-value-label">{cfg.rotulo}</span>
        <strong className="indicator-value">{cfg.valor}</strong>
        <Fatos itens={[
          ["Consultores com venda", String(grupos.length)],
          ["Maior contribuição", grupos[0] ? grupos[0].nome : "—"],
          ["Ticket médio de adesão", cfg.lista.length ? moeda.format(cfg.lista.reduce((s, v) => s + v.valor, 0) / cfg.lista.length) : "—"],
        ]} />
        <Secao eyebrow="POR CONSULTOR" titulo="Quem fez o resultado" total={grupos.length} vazio="Nenhuma venda confirmada neste período.">
          {grupos.map((g) => (
            <LinhaConsultor key={g.id} nome={g.nome} fotoUrl={g.fotoUrl} regional={g.regional} destaque={plural(g.qtd, "venda", "vendas")}
              detalhe={cfg.ordem === "qtd" ? "no período" : "em adesão"} valor={moedaExata.format(g.valor)} aoAbrir={abrirConsultor(g.id)} />
          ))}
        </Secao>
        <Secao eyebrow="COMPOSIÇÃO DO INDICADOR" titulo="Vendas correspondentes" total={cfg.lista.length} vazio="Nenhuma venda confirmada neste período.">
          {cfg.lista.map((v) => <LinhaVenda key={v.vendaId} v={v} aoAbrir={() => aoAbrir({ tipo: "venda", id: v.vendaId })} />)}
        </Secao>
      </Moldura>
    );
  }

  // ---------- evolução do mês ----------
  if (detalhe.tipo === "evolucao") {
    const lista = [...dados.rankingConsultores].sort((a, b) => (b.percentualMeta ?? -1) - (a.percentualMeta ?? -1) || b.quantidadeVendas - a.quantidadeVendas);
    return (
      <Moldura eyebrow="RITMO DO MÊS" titulo="Evolução das vendas" icone={Target} rodape={`Meta geral = vendas do mês ÷ metas cadastradas · ${atualizado}`} {...props}>
        <span className="indicator-value-label">DA META GERAL</span>
        <strong className="indicator-value">{dados.resumo.percentualMetaGeral == null ? "—" : `${dados.resumo.percentualMetaGeral.toFixed(1)}%`}</strong>
        <Fatos itens={[
          ["Vendas no mês", String(dados.resumo.vendasNoMes)],
          ["Média por dia", mediaPorDia.toFixed(1)],
          ["Projeção do mês", String(projecao)],
        ]} />
        <Secao eyebrow="POR CONSULTOR" titulo="Meta de cada consultor" total={lista.length} vazio="Nenhuma venda neste período.">
          {lista.map((r) => (
            <LinhaConsultor key={r.consultorId} nome={r.nome} fotoUrl={r.fotoUrl} regional={r.regional}
              destaque={r.quantidadeMeta ? `${r.quantidadeVendas} / ${r.quantidadeMeta} vendas` : plural(r.quantidadeVendas, "venda", "vendas")}
              detalhe={r.percentualMeta == null ? "Meta não cadastrada" : `${r.percentualMeta.toFixed(0)}% da meta`}
              valor={moedaExata.format(r.valorVendido)} aoAbrir={abrirConsultor(r.consultorId)} />
          ))}
        </Secao>
      </Moldura>
    );
  }

  // ---------- administrativo (Notion) ----------
  if (detalhe.tipo === "admin") {
    const item: TvAdministrativoIndicador | undefined = dados.administrativo?.indicadores.find((i) => i.id === detalhe.id);
    if (!item) return null;
    const registros: TvAdministrativoRegistro[] = item.registros ?? [];
    const porPessoa = new Map<string, { pessoa: string; fotoUrl?: string | null; total: number; hoje: number }>();
    for (const r of registros) {
      const p = porPessoa.get(r.pessoa) ?? { pessoa: r.pessoa, fotoUrl: r.fotoUrl, total: 0, hoje: 0 };
      p.total += 1;
      if (r.hoje) p.hoje += 1;
      porPessoa.set(r.pessoa, p);
    }
    const pessoas = [...porPessoa.values()].sort((a, b) => b.total - a.total || a.pessoa.localeCompare(b.pessoa));
    return (
      <Moldura eyebrow="ACOMPANHAMENTO OPERACIONAL" titulo={item.rotulo} icone={ICONES_ADMIN[item.id] ?? Activity} classe={`operational-modal operational-modal-${item.id}`}
        rodape={`Fonte: Notion · ${atualizado}`} {...props}>
        <span className="indicator-value-label">REGISTROS NO MÊS</span>
        <strong className="indicator-value">{item.total}</strong>
        <Fatos itens={[
          ["Registrados hoje", String(item.hoje)],
          ["Último responsável", item.ultimo?.pessoa || "Sem registro"],
          ["Última data", item.ultimo ? dataRegistro(item.ultimo.data) : "—"],
        ]} />
        <Secao eyebrow="POR PESSOA" titulo={item.acao} total={pessoas.length} vazio="Nenhum registro deste indicador no mês.">
          {pessoas.map((p) => (
            <LinhaConsultor key={p.pessoa} nome={p.pessoa} fotoUrl={p.fotoUrl} regional="Responsável pelo registro"
              destaque={plural(p.total, "registro", "registros")} detalhe={`${p.hoje} hoje`} valor={String(p.total)} />
          ))}
        </Secao>
        <Secao eyebrow="REGISTROS DO INDICADOR" titulo="Todos os registros do mês" total={registros.length} vazio="Nenhum registro deste indicador no mês.">
          {registros.map((r, i) => (
            <div className="indicator-sale-row operational-record-row" key={`${r.pessoa}-${r.data}-${i}`}>
              <Foto nome={r.pessoa} url={r.fotoUrl} />
              <div className="indicator-sale-person"><strong>{r.pessoa}</strong><span>{r.cliente || r.tipoEvento || "Responsável pelo registro"}</span></div>
              <div className="indicator-sale-meta"><strong>{dataRegistro(r.data)}</strong><span>{r.placa || "Sem placa informada"}</span></div>
              <strong className="indicator-sale-value">{r.tipoEvento || item.acao}</strong>
            </div>
          ))}
        </Secao>
      </Moldura>
    );
  }

  // ---------- regional ----------
  if (detalhe.tipo === "regional") {
    const reg = dados.rankingRegionais.find((x) => x.regionalId === detalhe.id);
    if (!reg) return null;
    const consultores = dados.rankingConsultores.filter((x) => x.regional === reg.nome);
    const dela = vendas.filter((v) => v.regional === reg.nome);
    return (
      <Moldura eyebrow="DETALHES DA REGIONAL" titulo={reg.nome} icone={Target} rodape={`${reg.posicao}ª no ranking regional · ${atualizado}`} {...props}>
        <span className="indicator-value-label">ADESÃO NO MÊS</span>
        <strong className="indicator-value">{moedaExata.format(reg.valorTotal)}</strong>
        <Fatos itens={[
          ["Vendas no mês", reg.quantidadeMeta ? `${reg.quantidadeVendas} / ${reg.quantidadeMeta}` : String(reg.quantidadeVendas)],
          [reg.percentualMeta == null ? "Participação no total" : "% da meta", reg.percentualMeta == null ? `${reg.percentualParticipacao.toFixed(0)}%` : `${reg.percentualMeta.toFixed(1)}%`],
          ["Consultores com venda", String(consultores.length)],
        ]} />
        <Secao eyebrow="POR CONSULTOR" titulo="Consultores da regional" total={consultores.length} vazio="Nenhuma venda desta regional neste período.">
          {consultores.map((r) => (
            <LinhaConsultor key={r.consultorId} nome={r.nome} fotoUrl={r.fotoUrl} regional={r.regional} destaque={plural(r.quantidadeVendas, "venda", "vendas")}
              detalhe={r.percentualMeta == null ? "Meta não cadastrada" : `${r.percentualMeta.toFixed(0)}% da meta`} valor={moedaExata.format(r.valorVendido)}
              aoAbrir={abrirConsultor(r.consultorId)} />
          ))}
        </Secao>
        <Secao eyebrow="COMPOSIÇÃO" titulo="Vendas da regional" total={dela.length} vazio="Nenhuma venda desta regional neste período.">
          {dela.map((v) => <LinhaVenda key={v.vendaId} v={v} aoAbrir={() => aoAbrir({ tipo: "venda", id: v.vendaId })} />)}
        </Secao>
      </Moldura>
    );
  }

  // ---------- venda ----------
  const venda = vendas.find((v) => v.vendaId === detalhe.id);
  const ultima = dados.ultimasVendas.find((v) => v.vendaId === detalhe.id);
  const consultor = venda?.consultor ?? ultima?.consultor;
  if (!consultor) return null;
  const consultorId = venda?.consultorId ?? dados.rankingConsultores.find((r) => r.nome === consultor)?.consultorId;
  const data = venda?.dataVenda ?? ultima!.dataVenda;
  return (
    <Moldura eyebrow="DETALHES DA VENDA" titulo={consultor} foto={{ nome: consultor, url: venda?.fotoUrl ?? ultima?.fotoUrl }} rodape={`Somente vendas confirmadas · ${atualizado}`} {...props}>
      <span className="indicator-value-label">VALOR DA ADESÃO</span>
      <strong className="indicator-value">{moedaExata.format(venda?.valor ?? ultima!.valor)}</strong>
      <Fatos itens={[
        ["Data da venda", dataBr.format(new Date(data))],
        ["Hora", horaBr.format(new Date(ultima?.atualizadaEm ?? data))],
        ["Regional", venda?.regional ?? ultima!.regional],
        ["Cliente", venda?.cliente ?? ultima?.cliente ?? "Não informado"],
        ["Placa / contrato", venda?.placa ?? ultima?.numeroContrato ?? "Não informado"],
        ["Origem", venda?.origem ?? ultima?.origem ?? "Não informado"],
      ]} />
      {consultorId && (
        <div style={{ marginTop: "1em" }}>
          <button type="button" className="operational-test-button" style={{ marginLeft: 0 }} onClick={abrirConsultor(consultorId)}>Ver o desempenho de {consultor}</button>
        </div>
      )}
    </Moldura>
  );
}
