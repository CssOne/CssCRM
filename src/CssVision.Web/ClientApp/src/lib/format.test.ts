import { describe, expect, it } from "vitest";
import { linkWhatsApp, tempoDecorrido } from "./format";

describe("linkWhatsApp", () => {
  it("monta o link do wa.me com DDI 55 para celular com DDD (formatado ou não)", () => {
    expect(linkWhatsApp("(31) 98888-7777")).toBe("https://wa.me/5531988887777");
    expect(linkWhatsApp("31988887777")).toBe("https://wa.me/5531988887777");
  });

  it("aceita o 55 já na frente e o zero do DDD", () => {
    expect(linkWhatsApp("+55 31 98888-7777")).toBe("https://wa.me/5531988887777");
    expect(linkWhatsApp("031988887777")).toBe("https://wa.me/5531988887777");
  });

  it("aceita celular antigo de 10 dígitos (começa em 6 a 9) e recusa fixo", () => {
    expect(linkWhatsApp("3188887777")).toBe("https://wa.me/553188887777");
    expect(linkWhatsApp("(31) 3333-4444")).toBeNull();
  });

  it("recusa vazio, curto, sem DDD e 11 dígitos sem o 9", () => {
    expect(linkWhatsApp(null)).toBeNull();
    expect(linkWhatsApp("")).toBeNull();
    expect(linkWhatsApp("12345")).toBeNull();
    expect(linkWhatsApp("988887777")).toBeNull();
    expect(linkWhatsApp("31888887777")).toBeNull();
  });

  it("anexa a mensagem pronta, com os caracteres especiais codificados", () => {
    expect(linkWhatsApp("31988887777", "Olá, Maria! Tudo bem?")).toBe("https://wa.me/5531988887777?text=Ol%C3%A1%2C%20Maria!%20Tudo%20bem%3F");
  });
});

describe("tempoDecorrido", () => {
  const agora = new Date("2026-10-23T12:00:00Z").getTime();
  const atras = (ms: number) => new Date(agora - ms).toISOString();

  it("fala em minutos, horas e dias", () => {
    expect(tempoDecorrido(atras(20_000), agora)).toBe("agora");
    expect(tempoDecorrido(atras(35 * 60_000), agora)).toBe("35 min");
    expect(tempoDecorrido(atras(3 * 3_600_000 + 5 * 60_000), agora)).toBe("3 h");
    expect(tempoDecorrido(atras(30 * 3_600_000), agora)).toBe("1 dia");
    expect(tempoDecorrido(atras(3 * 86_400_000), agora)).toBe("3 dias");
  });

  it("não quebra sem data nem com data no futuro", () => {
    expect(tempoDecorrido(null, agora)).toBe("-");
    expect(tempoDecorrido(new Date(agora + 5 * 60_000).toISOString(), agora)).toBe("agora");
  });
});
