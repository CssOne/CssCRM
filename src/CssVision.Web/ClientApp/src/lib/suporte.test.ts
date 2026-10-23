import { describe, expect, it } from "vitest";
import { avisoDeEspera, ehImagem, erroDoAnexo, LIMITE_SEM_RESPOSTA_HORAS, TAMANHO_MAXIMO_ANEXO } from "./suporte";

describe("erroDoAnexo", () => {
  it("aceita imagem e PDF até 5 MB", () => {
    expect(erroDoAnexo({ name: "print.PNG", size: 1000 })).toBeNull();
    expect(erroDoAnexo({ name: "foto.jpeg", size: TAMANHO_MAXIMO_ANEXO })).toBeNull();
    expect(erroDoAnexo({ name: "contrato.pdf", size: 2048 })).toBeNull();
  });

  it("recusa tipo, tamanho e arquivo vazio", () => {
    expect(erroDoAnexo({ name: "virus.exe", size: 1000 })).toMatch(/imagem/);
    expect(erroDoAnexo({ name: "grande.png", size: TAMANHO_MAXIMO_ANEXO + 1 })).toMatch(/5 MB/);
    expect(erroDoAnexo({ name: "vazio.png", size: 0 })).toMatch(/vazio/);
  });
});

describe("ehImagem", () => {
  it("diferencia imagem de PDF pela extensão", () => {
    expect(ehImagem("/uploads/suporte/a.webp")).toBe(true);
    expect(ehImagem("/uploads/suporte/a.pdf")).toBe(false);
    expect(ehImagem(null)).toBe(false);
  });
});

describe("avisoDeEspera", () => {
  const agora = new Date("2026-10-23T12:00:00Z").getTime();
  const horasAtras = (h: number) => new Date(agora - h * 3_600_000).toISOString();

  it("não mostra nada quando o chamado não espera o suporte", () => {
    expect(avisoDeEspera(false, horasAtras(5), true, agora)).toBeNull();
    expect(avisoDeEspera(true, null, true, agora)).toBeNull();
  });

  it("muda o texto para o atendente e para o solicitante", () => {
    expect(avisoDeEspera(true, horasAtras(3), true, agora)?.texto).toBe("Sem resposta há 3 h");
    expect(avisoDeEspera(true, horasAtras(3), false, agora)?.texto).toBe("Aguardando o suporte há 3 h");
  });

  it("fica urgente a partir do limite de horas", () => {
    expect(avisoDeEspera(true, horasAtras(LIMITE_SEM_RESPOSTA_HORAS - 1), true, agora)?.urgente).toBe(false);
    expect(avisoDeEspera(true, horasAtras(LIMITE_SEM_RESPOSTA_HORAS), true, agora)?.urgente).toBe(true);
  });
});
