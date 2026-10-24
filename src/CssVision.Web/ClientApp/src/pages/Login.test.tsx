import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";

const { login, loginDoisFatores, post, ApiRequestErrorFalso, isUnauthorizedFalso } = vi.hoisted(() => {
  class ApiRequestErrorFalso extends Error {
    status: number;
    codigo?: string;
    constructor(status: number, mensagem: string, codigo?: string) {
      super(mensagem);
      this.status = status;
      this.codigo = codigo;
    }
  }
  return {
    login: vi.fn(),
    loginDoisFatores: vi.fn(),
    post: vi.fn(),
    ApiRequestErrorFalso,
    isUnauthorizedFalso: (e: unknown) => e instanceof ApiRequestErrorFalso && e.status === 401,
  };
});

vi.mock("../lib/api", () => ({ api: { post: (...a: unknown[]) => post(...a) }, ApiRequestError: ApiRequestErrorFalso, isUnauthorized: isUnauthorizedFalso }));
vi.mock("../context/AuthContext", () => ({ useAuth: () => ({ sessao: null, login, loginDoisFatores }) }));
vi.mock("../components/Shell", () => ({ SeletorDeTema: () => null }));

import { LoginPage, mensagemDeErroDoLogin } from "./Login";

const sessaoOk = { areaInicial: "/app/crm" };

function abrir() {
  return render(
    <MemoryRouter initialEntries={["/login"]}>
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route path="/app/crm" element={<p>Dentro do CRM</p>} />
      </Routes>
    </MemoryRouter>
  );
}

const campoEmail = () => screen.getByLabelText("E-mail");
const campoSenha = () => screen.getByLabelText("Senha");
const botaoEntrar = () => screen.getByRole("button", { name: /Entrar/ });

describe("LoginPage", () => {
  beforeEach(() => {
    login.mockReset();
    loginDoisFatores.mockReset();
    post.mockReset();
    localStorage.clear();
  });

  it("entra com e-mail e senha, tirando os espaços das pontas do e-mail", async () => {
    login.mockResolvedValue({ requerDoisFatores: false, sessao: sessaoOk });
    abrir();

    await userEvent.type(campoEmail(), "  ana@empresa.com  ");
    await userEvent.type(campoSenha(), "Senha-Forte-1");
    await userEvent.click(botaoEntrar());

    await waitFor(() => expect(login).toHaveBeenCalledWith("ana@empresa.com", "Senha-Forte-1", true));
    expect(await screen.findByText("Dentro do CRM")).toBeInTheDocument();
  });

  it("não vai ao servidor sem e-mail ou sem senha: diz o que falta e leva o cursor para o campo", async () => {
    abrir();

    await userEvent.click(botaoEntrar());
    expect(screen.getByRole("alert")).toHaveTextContent("Informe o seu e-mail.");
    expect(campoEmail()).toHaveFocus();
    expect(campoEmail()).toHaveAttribute("aria-invalid", "true");

    await userEvent.type(campoEmail(), "ana@empresa.com");
    await userEvent.click(botaoEntrar());
    expect(screen.getByRole("alert")).toHaveTextContent("Informe a sua senha.");
    expect(campoSenha()).toHaveFocus();
    expect(login).not.toHaveBeenCalled();
  });

  it("senha errada: mostra o erro, limpa a senha, mantém o e-mail e volta o cursor para a senha", async () => {
    login.mockRejectedValue(new ApiRequestErrorFalso(401, "x"));
    abrir();

    await userEvent.type(campoEmail(), "ana@empresa.com");
    await userEvent.type(campoSenha(), "errada");
    await userEvent.click(botaoEntrar());

    expect(await screen.findByRole("alert")).toHaveTextContent("E-mail ou senha inválidos.");
    expect(campoSenha()).toHaveValue("");
    expect(campoSenha()).toHaveFocus();
    expect(campoEmail()).toHaveValue("ana@empresa.com");
  });

  it("lembra só o e-mail da última entrada (nunca a senha) e já deixa o cursor na senha", async () => {
    login.mockResolvedValue({ requerDoisFatores: false, sessao: sessaoOk });
    const primeira = abrir();
    await userEvent.type(campoEmail(), "ana@empresa.com");
    await userEvent.type(campoSenha(), "Senha-Forte-1");
    await userEvent.click(botaoEntrar());
    await screen.findByText("Dentro do CRM");
    primeira.unmount();

    abrir();
    expect(campoEmail()).toHaveValue("ana@empresa.com");
    expect(campoSenha()).toHaveValue("");
    expect(campoSenha()).toHaveFocus();
    expect(Object.values(localStorage).join(" ")).not.toContain("Senha-Forte-1");
  });

  it("avisa quando o Caps Lock está ligado e some ao sair do campo", async () => {
    abrir();

    fireEvent.keyDown(campoSenha(), { key: "a", modifierCapsLock: true });
    expect(screen.getByRole("status")).toHaveTextContent("Caps Lock está ligado");

    fireEvent.keyUp(campoSenha(), { key: "a", modifierCapsLock: false });
    expect(screen.queryByRole("status")).not.toBeInTheDocument();

    fireEvent.keyDown(campoSenha(), { key: "a", modifierCapsLock: true });
    fireEvent.blur(campoSenha());
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
  });

  it("mostra e esconde a senha", async () => {
    abrir();
    expect(campoSenha()).toHaveAttribute("type", "password");

    await userEvent.click(screen.getByRole("button", { name: "Mostrar senha" }));
    expect(campoSenha()).toHaveAttribute("type", "text");
    await userEvent.click(screen.getByRole("button", { name: "Ocultar senha" }));
    expect(campoSenha()).toHaveAttribute("type", "password");
  });

  it("com verificação em dois fatores, aceita só dígitos e entra sozinho ao completar 6", async () => {
    login.mockResolvedValue({ requerDoisFatores: true, sessao: null });
    loginDoisFatores.mockResolvedValue(sessaoOk);
    abrir();
    await userEvent.type(campoEmail(), "ana@empresa.com");
    await userEvent.type(campoSenha(), "Senha-Forte-1");
    await userEvent.click(botaoEntrar());

    const codigo = await screen.findByLabelText("Código de 6 dígitos");
    await userEvent.type(codigo, "12a3 45");
    expect(loginDoisFatores).not.toHaveBeenCalled();
    expect(codigo).toHaveValue("12345");

    await userEvent.type(codigo, "6");
    await waitFor(() => expect(loginDoisFatores).toHaveBeenCalledTimes(1));
    expect(loginDoisFatores).toHaveBeenCalledWith("123456", true, false);
    expect(await screen.findByText("Dentro do CRM")).toBeInTheDocument();
  });

  it("código colado com espaço vira 6 dígitos; código errado limpa o campo e mostra o erro", async () => {
    login.mockResolvedValue({ requerDoisFatores: true, sessao: null });
    loginDoisFatores.mockRejectedValue(new ApiRequestErrorFalso(401, "x"));
    abrir();
    await userEvent.type(campoEmail(), "ana@empresa.com");
    await userEvent.type(campoSenha(), "Senha-Forte-1");
    await userEvent.click(botaoEntrar());

    const codigo = await screen.findByLabelText("Código de 6 dígitos");
    await userEvent.click(codigo);
    await userEvent.paste("123 456");

    await waitFor(() => expect(loginDoisFatores).toHaveBeenCalledWith("123456", true, false));
    expect(await screen.findByRole("alert")).toHaveTextContent("Código inválido.");
    expect(codigo).toHaveValue("");
  });

  it("código de recuperação não é filtrado nem enviado sozinho", async () => {
    login.mockResolvedValue({ requerDoisFatores: true, sessao: null });
    abrir();
    await userEvent.type(campoEmail(), "ana@empresa.com");
    await userEvent.type(campoSenha(), "Senha-Forte-1");
    await userEvent.click(botaoEntrar());
    await userEvent.click(await screen.findByRole("button", { name: /Usar código de recuperação/ }));

    const codigo = screen.getByLabelText("Código de recuperação");
    await userEvent.type(codigo, "abcde-12345");

    expect(codigo).toHaveValue("abcde-12345");
    expect(loginDoisFatores).not.toHaveBeenCalled();
  });

  it("esqueci a senha: leva o e-mail digitado (sem espaços) e confirma o envio", async () => {
    post.mockResolvedValue(undefined);
    abrir();
    await userEvent.type(campoEmail(), " ana@empresa.com ");
    await userEvent.click(screen.getByRole("button", { name: "Esqueceu a senha?" }));

    expect(screen.getByLabelText("E-mail")).toHaveValue("ana@empresa.com");
    await userEvent.click(screen.getByRole("button", { name: /Enviar link de redefinição/ }));

    await waitFor(() => expect(post).toHaveBeenCalledWith("/account/forgot-password", { email: "ana@empresa.com" }));
    expect(await screen.findByText("Verifique seu e-mail")).toBeInTheDocument();
  });
});

describe("mensagemDeErroDoLogin", () => {
  it("usa o texto do servidor na senha inicial antiga e na falta de conexão", () => {
    expect(mensagemDeErroDoLogin(new ApiRequestErrorFalso(401, "A senha inicial não vale mais.", "senha_inicial"))).toBe("A senha inicial não vale mais.");
    expect(mensagemDeErroDoLogin(new ApiRequestErrorFalso(0, "Sem conexão com o servidor.", "sem_conexao"))).toBe("Sem conexão com o servidor.");
  });

  it("é genérica para credenciais, excesso de tentativas e erros inesperados", () => {
    expect(mensagemDeErroDoLogin(new ApiRequestErrorFalso(401, "x"))).toBe("E-mail ou senha inválidos.");
    expect(mensagemDeErroDoLogin(new ApiRequestErrorFalso(429, "x"))).toMatch(/Muitas tentativas/);
    expect(mensagemDeErroDoLogin(new ApiRequestErrorFalso(500, "x"))).toBe("Não foi possível entrar. Tente novamente.");
    expect(mensagemDeErroDoLogin(new Error("boom"))).toBe("Não foi possível entrar. Tente novamente.");
  });
});
