import { describe, expect, it } from "vitest";
import { ignoraFiltroDeMes, rotuloDaDataDeAdesao, vendasVisiveis } from "./vendasPendentes";

interface V {
  cliente: string;
  mes: number;
  pendente: boolean;
  atrasada: boolean;
}

const vendas: V[] = [
  { cliente: "Setembro pago", mes: 9, pendente: false, atrasada: false },
  { cliente: "Setembro agendada p/ outubro", mes: 9, pendente: true, atrasada: false },
  { cliente: "Agosto atrasada", mes: 8, pendente: true, atrasada: true },
  { cliente: "Outubro pago", mes: 10, pendente: false, atrasada: false },
  { cliente: "Outubro agendada", mes: 10, pendente: true, atrasada: false },
];

const confere = (v: V, f: string) => (!f ? true : f === "pendente" ? v.pendente : f === "atrasada" ? v.atrasada : !v.pendente);

const ver = (mes: number, filtro: string, passaOutros: (v: V) => boolean = () => true) =>
  vendasVisiveis(vendas, { mes, filtro, noMes: (v) => v.mes === mes, passaOutros, confere });

describe("vendasVisiveis", () => {
  it("sem atalho respeita o mês escolhido", () => {
    expect(ver(10, "").lista.map((v) => v.cliente)).toEqual(["Outubro pago", "Outubro agendada"]);
  });

  it("'Com pendência' mostra as pendentes de qualquer mês, inclusive as fechadas em meses anteriores", () => {
    expect(ver(10, "pendente").lista.map((v) => v.cliente)).toEqual(["Setembro agendada p/ outubro", "Agosto atrasada", "Outubro agendada"]);
  });

  it("'Em atraso' mostra as atrasadas de qualquer mês", () => {
    expect(ver(10, "atrasada").lista.map((v) => v.cliente)).toEqual(["Agosto atrasada"]);
  });

  it("'Tudo recebido' continua só no mês escolhido", () => {
    expect(ver(10, "recebida").lista.map((v) => v.cliente)).toEqual(["Outubro pago"]);
  });

  it("as contagens dos atalhos seguem a mesma regra", () => {
    const r = ver(10, "");
    expect(r.contagem("")).toBe(2);
    expect(r.contagem("pendente")).toBe(3);
    expect(r.contagem("atrasada")).toBe(1);
    expect(r.contagem("recebida")).toBe(1);
  });

  it("ano inteiro (mês 0) não corta nada", () => {
    const r = ver(0, "pendente");
    expect(r.lista).toHaveLength(3);
    expect(r.fezCortePorMes).toBe(false);
  });

  it("avisa quando há pendências de outros meses aparecendo", () => {
    expect(ver(10, "pendente").fezCortePorMes).toBe(true);
    expect(ver(10, "").fezCortePorMes).toBe(false);
  });

  it("os outros filtros (busca, produto, consultor) continuam valendo nas pendências", () => {
    const r = ver(10, "pendente", (v) => v.cliente.includes("Agosto"));
    expect(r.lista.map((v) => v.cliente)).toEqual(["Agosto atrasada"]);
  });
});

describe("rotuloDaDataDeAdesao", () => {
  it("mostra a data combinada: 'paga em' para pendente e 'venceu em' (em destaque) para atrasada", () => {
    expect(rotuloDaDataDeAdesao("Pendente", "2026-10-30")).toEqual({ texto: "paga em 30/10/2026", atrasada: false });
    expect(rotuloDaDataDeAdesao("Atrasado", "2026-10-20")).toEqual({ texto: "venceu em 20/10/2026", atrasada: true });
  });

  it("aceita data com horário e não mostra nada sem data ou fora de pendente/atrasada", () => {
    expect(rotuloDaDataDeAdesao("Pendente", "2026-10-30T00:00:00")?.texto).toBe("paga em 30/10/2026");
    expect(rotuloDaDataDeAdesao("Pendente", null)).toBeNull();
    expect(rotuloDaDataDeAdesao("Recebido", "2026-10-30")).toBeNull();
    expect(rotuloDaDataDeAdesao("Cancelado", "2026-10-30")).toBeNull();
    expect(rotuloDaDataDeAdesao("Sem valor", "2026-10-30")).toBeNull();
  });
});

describe("ignoraFiltroDeMes", () => {
  it("só vale para pendência e atraso", () => {
    expect(ignoraFiltroDeMes("pendente")).toBe(true);
    expect(ignoraFiltroDeMes("atrasada")).toBe(true);
    expect(ignoraFiltroDeMes("recebida")).toBe(false);
    expect(ignoraFiltroDeMes("")).toBe(false);
  });
});
