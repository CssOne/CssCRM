import { fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const post = vi.fn();
vi.mock("../../lib/api", () => ({ api: { post: (...args: unknown[]) => post(...args) } }));
vi.mock("../../context/AuthContext", () => ({ useAuth: () => ({ sessao: { nomeCompleto: "Ana Paula Lima" } }) }));

import { BotaoWhatsApp } from "./BotaoWhatsApp";

describe("BotaoWhatsApp", () => {
  beforeEach(() => {
    post.mockReset();
    post.mockResolvedValue(undefined);
  });

  it("abre a conversa do cliente com a mensagem da etapa, em nova aba", () => {
    render(<BotaoWhatsApp telefone="(31) 98888-7777" nome="Maria Souza" etapa="Cotação" />);

    const link = screen.getByRole("link", { name: "Chamar Maria Souza no WhatsApp" });
    expect(link).toHaveAttribute("target", "_blank");
    expect(link).toHaveAttribute("rel", expect.stringContaining("noopener"));
    const href = new URL(link.getAttribute("href")!);
    expect(href.origin + href.pathname).toBe("https://wa.me/5531988887777");
    const texto = href.searchParams.get("text")!;
    expect(texto).toContain("Olá, Maria!");
    expect(texto).toContain("Aqui é Ana da CSS Brasil");
    expect(texto).toContain("cotação");
  });

  it("não aparece para telefone fixo ou inválido", () => {
    const { container, rerender } = render(<BotaoWhatsApp telefone="(31) 3333-4444" nome="Maria" />);
    expect(container).toBeEmptyDOMElement();
    rerender(<BotaoWhatsApp telefone={null} nome="Maria" />);
    expect(container).toBeEmptyDOMElement();
  });

  it("registra o contato quando recebe o id do lead, sem deixar o clique chegar ao cartão", () => {
    const aoClicarNoCartao = vi.fn();
    render(
      <div onClick={aoClicarNoCartao}>
        <BotaoWhatsApp telefone="31988887777" nome="Maria" leadId="lead-1" />
      </div>
    );

    fireEvent.click(screen.getByRole("link"));

    expect(post).toHaveBeenCalledWith("/crm/leads/lead-1/contato", { canal: "WhatsApp" });
    expect(aoClicarNoCartao).not.toHaveBeenCalled();
  });

  it("sem id do lead não registra nada, e falha ao registrar não atrapalha", () => {
    render(<BotaoWhatsApp telefone="31988887777" nome="Maria" />);
    fireEvent.click(screen.getByRole("link"));
    expect(post).not.toHaveBeenCalled();

    post.mockRejectedValue(new Error("fora do ar"));
    render(<BotaoWhatsApp telefone="31988887777" nome="João" leadId="lead-2" />);
    expect(() => fireEvent.click(screen.getByRole("link", { name: /João/ }))).not.toThrow();
  });
});
