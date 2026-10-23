import { describe, expect, it } from "vitest";
import { mensagemWhatsApp } from "./mensagensWhatsApp";
import { sugestaoParaEtapa } from "./sugestoesEmail";

describe("mensagemWhatsApp", () => {
  it("abre com o primeiro nome do cliente e do consultor", () => {
    const m = mensagemWhatsApp("Cotação", "Maria Souza", "Ana Paula Lima");
    expect(m).toContain("Olá, Maria!");
    expect(m).toContain("Aqui é Ana da CSS Brasil.");
  });

  it("muda o texto conforme a etapa, sem depender de acento ou maiúsculas", () => {
    expect(mensagemWhatsApp("COTACAO", "Maria", "Ana")).toContain("cotação");
    expect(mensagemWhatsApp("Venda concluída (Leads)", "Maria", "Ana")).toContain("Seja bem-vindo");
    expect(mensagemWhatsApp("Perdido", "Maria", "Ana")).toContain("nova proposta");
    expect(mensagemWhatsApp("Não fazemos", "Maria", "Ana")).toContain("não conseguimos atender");
    expect(mensagemWhatsApp("Em atendimento (Indicação)", "Maria", "Ana")).toContain("indicação");
    expect(mensagemWhatsApp("Em atendimento (Leads)", "Maria", "Ana")).toContain("ficou alguma dúvida");
  });

  it("tem texto genérico sem etapa ou com etapa desconhecida, e sem nome do consultor", () => {
    expect(mensagemWhatsApp(null, "Maria", null)).toContain("Aqui é o seu consultor");
    expect(mensagemWhatsApp("Etapa nova", "Maria", "Ana")).toContain("Posso te ajudar");
  });
});

describe("sugestaoParaEtapa (e-mail)", () => {
  it("sugere assunto e mensagem com {nome} por etapa, e um texto genérico para as demais", () => {
    expect(sugestaoParaEtapa("Cotação").assunto).toContain("{nome}");
    expect(sugestaoParaEtapa("Em atendimento (Indicação)").assunto).toContain("indicação");
    expect(sugestaoParaEtapa("Venda concluída").mensagem).toContain("{nome}");
    expect(sugestaoParaEtapa("Qualquer outra").assunto).toBe("{nome}, tudo bem?");
  });
});
