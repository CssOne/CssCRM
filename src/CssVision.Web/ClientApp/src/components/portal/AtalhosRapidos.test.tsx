import { render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";

const get = vi.fn();
vi.mock("../../lib/api", () => ({ api: { get: (...a: unknown[]) => get(...a) } }));

import { AtalhosRapidos } from "./AtalhosRapidos";

const abrir = () =>
  render(
    <MemoryRouter>
      <AtalhosRapidos />
    </MemoryRouter>
  );

describe("AtalhosRapidos", () => {
  beforeEach(() => get.mockReset());

  it("mostra os atalhos, cada um levando à ação certa", async () => {
    get.mockResolvedValue([]);
    abrir();

    expect(screen.getByRole("link", { name: /Novo cliente/ })).toHaveAttribute("href", "/app/crm/leads/kanban?acao=novo-cliente");
    expect(screen.getByRole("link", { name: /Quadro de leads/ })).toHaveAttribute("href", "/app/crm/leads/kanban");
    expect(screen.getByRole("link", { name: /Disparar e-mail/ })).toHaveAttribute("href", "/app/crm/leads/kanban?acao=disparar-email");
    expect(screen.getByRole("link", { name: /Adesões a receber/ })).toHaveAttribute("href", "/app/portal/controle-financeiro?aba=vendas&filtro=pendente");
    expect(screen.getByRole("link", { name: /Minhas propostas/ })).toHaveAttribute("href", "/app/portal/propostas");
    expect(screen.getByRole("link", { name: /Suporte/ })).toHaveAttribute("href", "/app/suporte");
    expect(screen.queryByRole("link", { name: /Materiais|Treinamentos/ })).not.toBeInTheDocument();
    await waitFor(() => expect(get).toHaveBeenCalledWith("/crm/opportunities/lembretes-adesao", expect.anything()));
  });

  it("mostra o contador de adesões vencidas ou vencendo hoje no atalho de adesões", async () => {
    get.mockResolvedValue([{ opportunityId: "1" }, { opportunityId: "2" }, { opportunityId: "3" }]);
    abrir();

    expect(await screen.findByLabelText("3 adesão(ões) vencida(s) ou vencendo hoje")).toHaveTextContent("3");
  });

  it("sem adesão vencida, ou se a consulta falhar, não mostra contador", async () => {
    get.mockRejectedValue(new Error("fora do ar"));
    const { unmount } = abrir();
    await waitFor(() => expect(get).toHaveBeenCalled());
    expect(screen.queryByLabelText(/adesão\(ões\)/)).not.toBeInTheDocument();
    unmount();

    get.mockResolvedValue([]);
    abrir();
    await waitFor(() => expect(get).toHaveBeenCalledTimes(2));
    expect(screen.queryByLabelText(/adesão\(ões\)/)).not.toBeInTheDocument();
  });
});
