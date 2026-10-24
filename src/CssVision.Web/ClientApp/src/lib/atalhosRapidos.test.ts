import { describe, expect, it } from "vitest";
import { abaDoFinanceiro, acaoDoQuadro, ATALHOS_RAPIDOS, filtroDeVendas } from "./atalhosRapidos";

const params = (q: string) => new URLSearchParams(q);

describe("ATALHOS_RAPIDOS", () => {
  it("tem chaves e rotas únicas, todas dentro do app", () => {
    expect(new Set(ATALHOS_RAPIDOS.map((a) => a.chave)).size).toBe(ATALHOS_RAPIDOS.length);
    expect(new Set(ATALHOS_RAPIDOS.map((a) => a.rota)).size).toBe(ATALHOS_RAPIDOS.length);
    for (const a of ATALHOS_RAPIDOS) expect(a.rota.startsWith("/app/")).toBe(true);
  });

  it("não aponta para telas que não existem", () => {
    const rotas = ATALHOS_RAPIDOS.map((a) => a.rota.split("?")[0]);
    for (const morta of ["/app/portal/materiais", "/app/portal/treinamentos", "/app/portal/suporte"]) expect(rotas).not.toContain(morta);
  });

  it("os atalhos de ação levam os parâmetros que as telas entendem", () => {
    const porChave = Object.fromEntries(ATALHOS_RAPIDOS.map((a) => [a.chave, new URL(a.rota, "http://x")]));
    expect(acaoDoQuadro(porChave["novo-cliente"].searchParams)).toBe("novo-cliente");
    expect(acaoDoQuadro(porChave["disparar-email"].searchParams)).toBe("disparar-email");
    expect(porChave["novo-cliente"].pathname).toBe("/app/crm/leads/kanban");
    expect(abaDoFinanceiro(porChave["adesoes"].searchParams)).toBe("vendas");
    expect(filtroDeVendas(porChave["adesoes"].searchParams)).toBe("pendente");
  });
});

describe("acaoDoQuadro", () => {
  it("só aceita as ações conhecidas", () => {
    expect(acaoDoQuadro(params("acao=novo-cliente"))).toBe("novo-cliente");
    expect(acaoDoQuadro(params("acao=disparar-email"))).toBe("disparar-email");
    expect(acaoDoQuadro(params("acao=apagar-tudo"))).toBeNull();
    expect(acaoDoQuadro(params(""))).toBeNull();
  });
});

describe("abaDoFinanceiro e filtroDeVendas", () => {
  it("usam o que a URL pede, quando existe", () => {
    expect(abaDoFinanceiro(params("aba=custos"))).toBe("custos");
    expect(filtroDeVendas(params("filtro=atrasada"))).toBe("atrasada");
  });

  it("caem no padrão com valor desconhecido ou ausente", () => {
    expect(abaDoFinanceiro(params("aba=hack"))).toBe("resumo");
    expect(abaDoFinanceiro(params(""))).toBe("resumo");
    expect(filtroDeVendas(params("filtro=qualquer"))).toBe("");
    expect(filtroDeVendas(params(""))).toBe("");
  });
});
