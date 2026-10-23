import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

const { get, post, put, papeisDoUsuario } = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), papeisDoUsuario: { atual: ["Admin"] as string[] } }));

vi.mock("../../lib/api", async () => {
  const real = await vi.importActual<typeof import("../../lib/api")>("../../lib/api");
  return { ...real, api: { get: (...a: unknown[]) => get(...a), post: (...a: unknown[]) => post(...a), put: (...a: unknown[]) => put(...a), del: vi.fn() } };
});
vi.mock("../../context/AuthContext", () => ({
  useAuth: () => ({
    sessao: { id: "eu", nomeCompleto: "Eu Mesmo" },
    temPapel: (...papeis: string[]) => papeis.some((p) => papeisDoUsuario.atual.includes(p)),
  }),
}));
vi.mock("../../components/portal/ContratoFinanceiro", () => ({ ContratoFinanceiro: () => null }));

import { ToastProvider } from "../../components/ui";
import { UsersPage } from "./Users";

const usuario = (id: string, nome: string, extra: Record<string, unknown> = {}) => ({
  id,
  nomeCompleto: nome,
  email: `${nome.split(" ")[0].toLowerCase()}@x.com`,
  telefone: "31988887777",
  papeis: ["Comercial"],
  regionalNome: "MG132",
  ativo: true,
  criadoEm: "2026-01-10T12:00:00Z",
  fotoUrl: null,
  ...extra,
});

const lista = (itens: unknown[]) => ({ itens, pagina: 1, tamanhoPagina: 20, totalRegistros: itens.length, totalPaginas: 1 });

const consultas = () => get.mock.calls.map((c) => String(c[0])).filter((u) => u.startsWith("/crm/users"));

function abrir(itens: unknown[]) {
  get.mockImplementation((caminho: string) => {
    if (caminho.startsWith("/crm/settings/regionals")) return Promise.resolve([{ id: "r1", nome: "MG132", ativa: true }]);
    if (caminho.startsWith("/crm/users")) return Promise.resolve(lista(itens));
    return Promise.resolve([]);
  });
  return render(
    <ToastProvider>
      <UsersPage />
    </ToastProvider>
  );
}

describe("Usuários", () => {
  beforeEach(() => {
    get.mockReset();
    post.mockReset();
    put.mockReset();
    papeisDoUsuario.atual = ["Admin"];
  });

  it("lista com avatar (iniciais), papel, situação e quantidade; inativo fica identificado", async () => {
    abrir([usuario("u1", "Ana Souza"), usuario("u2", "Bruno Lima", { ativo: false, papeis: ["GestorComercial"] })]);

    expect(await screen.findByText("Ana Souza")).toBeInTheDocument();
    expect(screen.getByText("AS")).toBeInTheDocument();
    expect(screen.getByText("BL")).toBeInTheDocument();
    expect(screen.getByText("Gestor regional")).toBeInTheDocument();
    expect(screen.getByText("Inativo")).toBeInTheDocument();
    expect(screen.getByText("2 usuário(s)")).toBeInTheDocument();
  });

  it("não oferece Inativar para a própria conta; oferece Ativar para conta inativa", async () => {
    abrir([usuario("eu", "Eu Mesmo"), usuario("u2", "Bruno Lima", { ativo: false })]);

    await screen.findByText("Eu Mesmo");
    const linhaEu = screen.getByText("Eu Mesmo").closest("div.rounded-xl") as HTMLElement;
    expect(within(linhaEu).queryByRole("button", { name: /Inativar|Ativar/ })).not.toBeInTheDocument();
    const linhaBruno = screen.getByText("Bruno Lima").closest("div.rounded-xl") as HTMLElement;
    expect(within(linhaBruno).getByRole("button", { name: /Ativar/ })).toBeInTheDocument();
  });

  it("inativar pede confirmação e só então chama a API", async () => {
    put.mockResolvedValue({});
    abrir([usuario("u1", "Ana Souza")]);
    await screen.findByText("Ana Souza");

    await userEvent.click(screen.getByRole("button", { name: /Inativar/ }));
    expect(put).not.toHaveBeenCalled();
    expect(await screen.findByText(/A pessoa deixa de conseguir entrar/)).toBeInTheDocument();

    await userEvent.click(within(screen.getByRole("dialog", { name: "Inativar usuário" })).getByRole("button", { name: "Inativar" }));
    await waitFor(() => expect(put).toHaveBeenCalledWith("/crm/users/u1/ativo", { ativo: false }));
  });

  it("ativar uma conta inativa vai direto, sem confirmação", async () => {
    put.mockResolvedValue({});
    abrir([usuario("u2", "Bruno Lima", { ativo: false })]);
    await screen.findByText("Bruno Lima");

    await userEvent.click(screen.getByRole("button", { name: /Ativar/ }));

    await waitFor(() => expect(put).toHaveBeenCalledWith("/crm/users/u2/ativo", { ativo: true }));
  });

  it("os filtros rápidos de situação e de papel refazem a consulta", async () => {
    abrir([usuario("u1", "Ana Souza")]);
    await screen.findByText("Ana Souza");

    await userEvent.click(screen.getByRole("button", { name: "Inativos" }));
    await waitFor(() => expect(consultas().some((u) => u.includes("ativo=false"))).toBe(true));

    await userEvent.click(screen.getByRole("button", { name: "Gestores" }));
    await waitFor(() => expect(consultas().some((u) => u.includes("papel=GestorComercial"))).toBe(true));
    expect(screen.getByRole("button", { name: "Gestores" })).toHaveAttribute("aria-pressed", "true");

    await userEvent.click(screen.getByRole("button", { name: "Gestores" }));
    expect(screen.getByRole("button", { name: "Gestores" })).toHaveAttribute("aria-pressed", "false");
  });

  it("quem não é administrador não vê os filtros rápidos de papel", async () => {
    papeisDoUsuario.atual = ["GestorComercial"];
    abrir([usuario("u1", "Ana Souza")]);
    await screen.findByText("Ana Souza");

    expect(screen.getByRole("button", { name: "Ativos" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Gestores" })).not.toBeInTheDocument();
  });

  it("a busca só consulta quando a pessoa para de digitar (uma chamada, não uma por tecla)", async () => {
    abrir([usuario("u1", "Ana Souza")]);
    await screen.findByText("Ana Souza");
    const antes = consultas().length;

    await userEvent.type(screen.getByPlaceholderText("Buscar por nome ou e-mail"), "ana");

    await waitFor(() => expect(consultas().some((u) => u.includes("busca=ana"))).toBe(true));
    const comBusca = consultas().slice(antes).filter((u) => u.includes("busca="));
    expect(comBusca).toHaveLength(1);
    expect(comBusca[0]).toContain("busca=ana");
  });

  it("sem resultado com filtro mostra 'Limpar filtros'; sem nenhum usuário, convida a cadastrar", async () => {
    const { unmount } = abrir([]);
    expect(await screen.findByText("Nenhum usuário cadastrado")).toBeInTheDocument();
    unmount();

    abrir([]);
    await screen.findByText("Nenhum usuário cadastrado");
    await userEvent.click(screen.getByRole("button", { name: "Ativos" }));
    expect(await screen.findByText("Nenhum usuário com esses filtros")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Limpar filtros" }));
    expect(await screen.findByText("Nenhum usuário cadastrado")).toBeInTheDocument();
  });

  it("redefinir senha: exige 10 caracteres, avisa quantos faltam, gera senha forte e envia", async () => {
    post.mockResolvedValue({});
    abrir([usuario("u1", "Ana Souza")]);
    await screen.findByText("Ana Souza");
    await userEvent.click(screen.getByRole("button", { name: /Senha/ }));

    const dialogo = await screen.findByRole("dialog", { name: "Redefinir senha" });
    const campo = within(dialogo).getByLabelText("Nova senha");
    const redefinir = within(dialogo).getByRole("button", { name: "Redefinir" });
    expect(redefinir).toBeDisabled();

    await userEvent.type(campo, "curta123");
    expect(within(dialogo).getByRole("status")).toHaveTextContent("Faltam 2 caractere(s)");
    expect(redefinir).toBeDisabled();

    await userEvent.click(within(dialogo).getByRole("button", { name: /Gerar senha forte/ }));
    expect((campo as HTMLInputElement).value).toHaveLength(16);
    expect(campo).toHaveAttribute("type", "text"); // a senha gerada fica visível para ser passada à pessoa
    expect(redefinir).toBeEnabled();

    await userEvent.click(redefinir);
    await waitFor(() => expect(post).toHaveBeenCalledWith("/crm/users/u1/reset-password", { novaSenha: (campo as HTMLInputElement).value }));
  });
});
