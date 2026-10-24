import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

const { get, post, put, ApiRequestErrorFalso } = vi.hoisted(() => {
  class ApiRequestErrorFalso extends Error {
    codigo?: string;
    constructor(mensagem: string, codigo?: string) {
      super(mensagem);
      this.codigo = codigo;
    }
  }
  return { get: vi.fn(), post: vi.fn(), put: vi.fn(), ApiRequestErrorFalso };
});
vi.mock("../../lib/api", () => ({
  api: { get: (...a: unknown[]) => get(...a), post: (...a: unknown[]) => post(...a), put: (...a: unknown[]) => put(...a) },
  ApiRequestError: ApiRequestErrorFalso,
}));

import { ToastProvider } from "../ui";
import { DisparoEmailDialog } from "./DisparoEmailDialog";
import type { LeadStage } from "../../lib/types";

const etapas: LeadStage[] = [
  { id: "e1", nome: "Cotação", ordem: 1, fechada: false, ativa: true },
  { id: "e2", nome: "Perdido", ordem: 2, fechada: true, ativa: true },
];

const disparo = (extra: Record<string, unknown> = {}) => ({
  id: "d1",
  etapaNome: "Cotação",
  assunto: "Oferta de março",
  status: "Concluido",
  total: 10,
  enviados: 9,
  falhas: 1,
  erro: null,
  criadoEm: new Date().toISOString(),
  concluidoEm: new Date().toISOString(),
  ...extra,
});

interface Cenario {
  conta?: { email: string; host: string; port: number };
  historico?: unknown[];
  previa?: { destinatarios: number; limite: number; descadastrados: number };
}

function configurar({ conta, historico = [], previa = { destinatarios: 3, limite: 500, descadastrados: 0 } }: Cenario = {}) {
  get.mockImplementation((caminho: string) => {
    if (caminho === "/crm/disparo-email/conta") return Promise.resolve(conta);
    if (caminho === "/crm/disparo-email/historico") return Promise.resolve(historico);
    if (caminho.startsWith("/crm/disparo-email/previa")) return Promise.resolve(previa);
    return Promise.reject(new Error("rota inesperada " + caminho));
  });
}

const abrirDialogo = () =>
  render(
    <ToastProvider>
      <DisparoEmailDialog open onClose={() => undefined} etapas={etapas} />
    </ToastProvider>
  );

async function escolherEtapa(nome: string) {
  await userEvent.click(await screen.findByRole("button", { name: /Etapa/, expanded: false }));
  await userEvent.click(await screen.findByRole("option", { name: nome }));
}

describe("DisparoEmailDialog", () => {
  beforeEach(() => {
    get.mockReset();
    post.mockReset();
    put.mockReset();
  });

  it("sem e-mail conectado pede o endereço e a senha de app, e conecta", async () => {
    configurar({ conta: undefined });
    put.mockResolvedValue({ email: "ana@gmail.com", host: "smtp.gmail.com", port: 587 });
    abrirDialogo();

    await userEvent.type(await screen.findByLabelText("Seu e-mail"), "ana@gmail.com");
    await userEvent.type(screen.getByLabelText("Senha de app"), "abcd efgh");
    await userEvent.click(screen.getByRole("button", { name: /Conectar e-mail/ }));

    await waitFor(() => expect(put).toHaveBeenCalledWith("/crm/disparo-email/conta", { email: "ana@gmail.com", senha: "abcd efgh", host: null, port: null }));
    expect(await screen.findByText("ana@gmail.com")).toBeInTheDocument();
  });

  it("mostra quantos vão receber e quantos cancelaram a inscrição", async () => {
    configurar({ conta: { email: "ana@gmail.com", host: "smtp.gmail.com", port: 587 }, previa: { destinatarios: 3, limite: 500, descadastrados: 2 } });
    abrirDialogo();

    await escolherEtapa("Cotação");

    expect(await screen.findByText(/3 cliente\(s\) seu\(s\) com e-mail nessa etapa vão receber/)).toBeInTheDocument();
    expect(screen.getByText(/2 cancelou\(aram\) a inscrição e fica\(m\) de fora/)).toBeInTheDocument();
  });

  it("avisa quando passa do limite e não deixa enviar", async () => {
    configurar({ conta: { email: "ana@gmail.com", host: "h", port: 587 }, previa: { destinatarios: 600, limite: 500, descadastrados: 0 } });
    abrirDialogo();

    await escolherEtapa("Cotação");
    await userEvent.type(screen.getByLabelText("Assunto"), "Oferta");
    await userEvent.type(screen.getByLabelText("Mensagem"), "Olá {{nome}}");

    expect(await screen.findByText(/o limite por disparo é 500/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Enviar/ })).toBeDisabled();
  });

  it("enfileira o disparo e leva para o histórico", async () => {
    configurar({ conta: { email: "ana@gmail.com", host: "h", port: 587 } });
    post.mockResolvedValue(disparo({ status: "Pendente", enviados: 0, falhas: 0, total: 3, concluidoEm: null }));
    abrirDialogo();

    await escolherEtapa("Cotação");
    await userEvent.type(screen.getByLabelText("Assunto"), "Oferta de março");
    await userEvent.type(screen.getByLabelText("Mensagem"), "Olá!");
    await userEvent.click(await screen.findByRole("button", { name: /Enviar para 3/ }));

    await waitFor(() => expect(post).toHaveBeenCalledWith("/crm/disparo-email", { etapaId: "e1", assunto: "Oferta de março", mensagem: "Olá!" }));
    expect(await screen.findByRole("tab", { name: /Histórico/, selected: true })).toBeInTheDocument();
  });

  it("o histórico mostra andamento (barra), contagem, falhas e o motivo de um disparo que falhou", async () => {
    configurar({
      conta: { email: "ana@gmail.com", host: "h", port: 587 },
      historico: [
        disparo({ id: "d1", assunto: "Em andamento", status: "Enviando", total: 10, enviados: 4, falhas: 1, concluidoEm: null }),
        disparo({ id: "d2", assunto: "Deu ruim", status: "Falhou", total: 5, enviados: 0, falhas: 5, erro: "O provedor recusou o login." }),
        disparo({ id: "d3", assunto: "Tudo certo", status: "Concluido", total: 10, enviados: 10, falhas: 0 }),
      ],
    });
    abrirDialogo();

    await userEvent.click(await screen.findByRole("tab", { name: /Histórico/ }));

    expect(await screen.findByText("Em andamento")).toBeInTheDocument();
    expect(screen.getByRole("progressbar", { name: "Andamento do disparo" })).toHaveAttribute("aria-valuenow", "50");
    expect(screen.getByText("4 de 10 enviado(s) · 1 com falha")).toBeInTheDocument();
    expect(screen.getByText("O provedor recusou o login.")).toBeInTheDocument();
    expect(screen.getByText("10 de 10 enviado(s)")).toBeInTheDocument();
  });

  it("com disparo em andamento não deixa começar outro", async () => {
    configurar({
      conta: { email: "ana@gmail.com", host: "h", port: 587 },
      historico: [disparo({ status: "Enviando", concluidoEm: null })],
    });
    abrirDialogo();

    expect(await screen.findByRole("status")).toHaveTextContent(/disparo em andamento/);
    await escolherEtapa("Cotação");
    await userEvent.type(screen.getByLabelText("Assunto"), "Oferta");
    await userEvent.type(screen.getByLabelText("Mensagem"), "Olá!");
    expect(screen.getByRole("button", { name: /Enviar/ })).toBeDisabled();
  });
});
