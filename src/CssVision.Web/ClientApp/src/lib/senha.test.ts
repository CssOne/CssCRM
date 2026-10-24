import { describe, expect, it } from "vitest";
import { gerarSenha, TAMANHO_MINIMO_DA_SENHA } from "./senha";

describe("gerarSenha", () => {
  it("tem o tamanho pedido (nunca menos que o mínimo do servidor)", () => {
    expect(gerarSenha()).toHaveLength(16);
    expect(gerarSenha(20)).toHaveLength(20);
    expect(gerarSenha(4)).toHaveLength(TAMANHO_MINIMO_DA_SENHA);
  });

  it("sempre traz maiúscula, minúscula, dígito e símbolo, sem caracteres confusos", () => {
    for (let i = 0; i < 200; i++) {
      const senha = gerarSenha();
      expect(senha).toMatch(/[a-z]/);
      expect(senha).toMatch(/[A-Z]/);
      expect(senha).toMatch(/[0-9]/);
      expect(senha).toMatch(/[!@#$%&*?]/);
      expect(senha).not.toMatch(/[0O1lI]/);
    }
  });

  it("cada senha sai diferente", () => {
    const senhas = new Set(Array.from({ length: 100 }, () => gerarSenha()));
    expect(senhas.size).toBe(100);
  });
});
