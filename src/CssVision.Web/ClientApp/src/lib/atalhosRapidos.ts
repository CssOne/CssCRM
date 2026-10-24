/** Parâmetros de URL que os atalhos usam para abrir a tela já na ação certa (ex.: o quadro de leads com o formulário de novo cliente aberto). */
export const PARAM_ACAO = "acao";
export const PARAM_ABA = "aba";
export const PARAM_FILTRO = "filtro";

export type AcaoDoQuadro = "novo-cliente" | "disparar-email";

export interface AtalhoRapido {
  chave: string;
  rotulo: string;
  rota: string;
  /** Texto de apoio (dica ao passar o mouse). */
  dica: string;
}

/** Só rotas que existem no app. Cada atalho leva direto ao que a pessoa quer fazer, não à tela genérica. */
export const ATALHOS_RAPIDOS: AtalhoRapido[] = [
  { chave: "novo-cliente", rotulo: "Novo cliente", rota: `/app/crm/leads/kanban?${PARAM_ACAO}=novo-cliente`, dica: "Abre o formulário de novo cliente no quadro de leads" },
  { chave: "quadro", rotulo: "Quadro de leads", rota: "/app/crm/leads/kanban", dica: "Seus leads por etapa" },
  { chave: "disparar-email", rotulo: "Disparar e-mail", rota: `/app/crm/leads/kanban?${PARAM_ACAO}=disparar-email`, dica: "Manda um e-mail para os clientes de uma etapa" },
  { chave: "adesoes", rotulo: "Adesões a receber", rota: `/app/portal/controle-financeiro?${PARAM_ABA}=vendas&${PARAM_FILTRO}=pendente`, dica: "Vendas com adesão pendente ou em atraso" },
  { chave: "propostas", rotulo: "Minhas propostas", rota: "/app/portal/propostas", dica: "Propostas em andamento, ganhas e perdidas" },
  { chave: "agenda", rotulo: "Agenda", rota: "/app/crm/agenda", dica: "Seus próximos contatos" },
  { chave: "clientes", rotulo: "Meus clientes", rota: "/app/portal/clientes", dica: "Todos os seus clientes" },
  { chave: "financeiro", rotulo: "Controle financeiro", rota: "/app/portal/controle-financeiro", dica: "Vendas, custos e metas" },
  { chave: "suporte", rotulo: "Suporte", rota: "/app/suporte", dica: "Abra um chamado ou acompanhe as respostas" },
];

/** Ação pedida na URL do quadro de leads; nulo se não houver ou se for desconhecida. */
export function acaoDoQuadro(params: URLSearchParams): AcaoDoQuadro | null {
  const acao = params.get(PARAM_ACAO);
  return acao === "novo-cliente" || acao === "disparar-email" ? acao : null;
}

const ABAS_DO_FINANCEIRO = ["resumo", "vendas", "custos", "metas", "cobrancas", "contrato"];
const FILTROS_DE_VENDAS = ["pendente", "atrasada", "recebida"];

/** Aba do Controle financeiro pedida na URL (só as que existem); "resumo" se não houver. */
export function abaDoFinanceiro(params: URLSearchParams): string {
  const aba = params.get(PARAM_ABA);
  return aba && ABAS_DO_FINANCEIRO.includes(aba) ? aba : "resumo";
}

/** Atalho de situação das vendas pedido na URL ("" = todas). */
export function filtroDeVendas(params: URLSearchParams): string {
  const filtro = params.get(PARAM_FILTRO);
  return filtro && FILTROS_DE_VENDAS.includes(filtro) ? filtro : "";
}
