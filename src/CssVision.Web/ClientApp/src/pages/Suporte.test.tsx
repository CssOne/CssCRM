import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";

const get = vi.fn();
const post = vi.fn();
const put = vi.fn();
const uploadFile = vi.fn();
vi.mock("../lib/api", () => ({
  api: { get: (...a: unknown[]) => get(...a), post: (...a: unknown[]) => post(...a), put: (...a: unknown[]) => put(...a) },
  isAbortError: () => false,
  uploadFile: (...a: unknown[]) => uploadFile(...a),
}));

import { ToastProvider } from "../components/ui";
import { SuportePage } from "./Suporte";

const horasAtras = (h: number) => new Date(Date.now() - h * 3_600_000).toISOString();

const chamado = (id: string, extra: Record<string, unknown> = {}) => ({
  id,
  assunto: `Assunto ${id}`,
  categoria: "Problema",
  status: "Aberto",
  solicitanteId: "u1",
  solicitanteNome: "Ana Souza",
  solicitanteEmail: "ana@x.com",
  criadoEm: horasAtras(50),
  ultimaMensagemEm: horasAtras(30),
  mensagens: 1,
  aguardandoSuporte: false,
  aguardandoDesde: null,
  ...extra,
});

function lista(atende: boolean) {
  return {
    atende,
    abertos: 2,
    chamados: [
      chamado("c1", { assunto: "Não consigo anexar", status: "EmAtendimento", aguardandoSuporte: false }),
      chamado("c2", { assunto: "Erro no login", aguardandoSuporte: true, aguardandoDesde: horasAtras(30) }),
      chamado("c3", { assunto: "Como exportar", status: "Resolvido" }),
    ],
  };
}

const detalhe = (id: string, mensagens: unknown[]) => ({ resumo: chamado(id, { assunto: "Não consigo anexar", status: "EmAtendimento" }), mensagens });

function abrir(atende: boolean, chamadoId?: string) {
  get.mockImplementation((caminho: string) => {
    if (caminho === "/crm/suporte") return Promise.resolve(lista(atende));
    if (caminho.startsWith("/crm/suporte/c1"))
      return Promise.resolve(
        detalhe("c1", [
          { id: "m1", autorNome: "Ana Souza", doSuporte: false, texto: "Anexo o print.", criadoEm: horasAtras(2), anexoUrl: "/uploads/suporte/a.png", anexoNome: "print.png" },
          { id: "m2", autorNome: "Suporte", doSuporte: true, texto: "", criadoEm: horasAtras(1), anexoUrl: "/uploads/suporte/b.pdf", anexoNome: "manual.pdf" },
        ])
      );
    return Promise.reject(new Error("rota inesperada " + caminho));
  });
  return render(
    <ToastProvider>
      <MemoryRouter initialEntries={[chamadoId ? `/app/suporte?chamado=${chamadoId}` : "/app/suporte"]}>
        <SuportePage />
      </MemoryRouter>
    </ToastProvider>
  );
}

describe("Suporte", () => {
  beforeEach(() => {
    get.mockReset();
    post.mockReset();
    put.mockReset();
    uploadFile.mockReset();
  });

  it("mostra filtros com contagem; o atendente ainda ganha 'Sem resposta' e 'Em atendimento'", async () => {
    abrir(true);

    const filtros = await screen.findByRole("group", { name: "Filtrar chamados" });
    expect(within(filtros).getByRole("button", { name: /Em aberto\s*2/ })).toBeInTheDocument();
    expect(within(filtros).getByRole("button", { name: /Sem resposta\s*1/ })).toBeInTheDocument();
    expect(within(filtros).getByRole("button", { name: /Em atendimento\s*1/ })).toBeInTheDocument();
    expect(within(filtros).getByRole("button", { name: /Resolvidos\s*1/ })).toBeInTheDocument();
    expect(within(filtros).getByRole("button", { name: /Todos\s*3/ })).toBeInTheDocument();
  });

  it("quem abre chamado não vê os filtros do atendente", async () => {
    abrir(false);

    const filtros = await screen.findByRole("group", { name: "Filtrar chamados" });
    expect(within(filtros).queryByRole("button", { name: /Sem resposta/ })).not.toBeInTheDocument();
    expect(within(filtros).queryByRole("button", { name: /Em atendimento/ })).not.toBeInTheDocument();
  });

  it("filtra por 'Sem resposta' e pela busca", async () => {
    abrir(true);
    await screen.findByText("Erro no login");
    expect(screen.queryByText("Como exportar")).not.toBeInTheDocument(); // resolvido não entra em "Em aberto"

    await userEvent.click(screen.getByRole("button", { name: /Sem resposta\s*1/ }));
    expect(screen.getByText("Erro no login")).toBeInTheDocument();
    expect(screen.queryByText("Não consigo anexar")).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: /Todos\s*3/ }));
    await userEvent.type(screen.getByLabelText("Buscar chamados"), "exportar");
    expect(screen.getByText("Como exportar")).toBeInTheDocument();
    expect(screen.queryByText("Erro no login")).not.toBeInTheDocument();
  });

  it("avisa há quanto tempo o chamado espera, diferente para o atendente e para o solicitante", async () => {
    const { unmount } = abrir(true);
    expect(await screen.findByText(/Sem resposta há 1 dia/)).toBeInTheDocument();
    unmount();

    abrir(false);
    expect(await screen.findByText(/Aguardando o suporte há 1 dia/)).toBeInTheDocument();
  });

  it("mostra os anexos da conversa: imagem com miniatura e PDF como link", async () => {
    abrir(true, "c1");

    const imagem = await screen.findByAltText("print.png");
    expect(imagem).toHaveAttribute("src", "/uploads/suporte/a.png");
    expect(screen.getByRole("link", { name: /manual\.pdf/ })).toHaveAttribute("href", "/uploads/suporte/b.pdf");
    expect(screen.getByText("Anexo o print.")).toBeInTheDocument();
  });

  it("envia a resposta com anexo (sem texto) pelo endpoint de anexo", async () => {
    abrir(true, "c1");
    await screen.findByAltText("print.png");
    uploadFile.mockResolvedValue(detalhe("c1", []));

    const arquivo = new File([new Uint8Array([1, 2, 3])], "erro.png", { type: "image/png" });
    await userEvent.upload(screen.getByLabelText("Anexar arquivo"), arquivo);
    expect(screen.getByText("erro.png")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: /Enviar/ }));

    await waitFor(() => expect(uploadFile).toHaveBeenCalledWith("/crm/suporte/c1/anexo", arquivo, { texto: "" }));
    expect(post).not.toHaveBeenCalled();
  });

  it("recusa arquivo de tipo ou tamanho inválido e não anexa", async () => {
    abrir(true, "c1");
    await screen.findByAltText("print.png");

    // applyAccept:false: o filtro do input não esconderia o arquivo; a validação é da própria tela.
    await userEvent.setup({ applyAccept: false }).upload(screen.getByLabelText("Anexar arquivo"), new File(["x"], "virus.exe"));

    expect(await screen.findByText(/Anexe uma imagem/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Remover anexo" })).not.toBeInTheDocument();
  });

  it("cola um print (Ctrl+V) como anexo e permite remover", async () => {
    abrir(true, "c1");
    await screen.findByAltText("print.png");

    const imagem = new File([new Uint8Array([1])], "image.png", { type: "image/png" });
    fireEvent.paste(screen.getByPlaceholderText(/Escreva sua mensagem/), { clipboardData: { files: [imagem], getData: () => "" } });
    expect(await screen.findByText("image.png")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Remover anexo" }));
    expect(screen.queryByText("image.png")).not.toBeInTheDocument();
  });

  it("sem texto e sem anexo o botão Enviar fica desabilitado; só texto vai pelo endpoint normal", async () => {
    abrir(true, "c1");
    await screen.findByAltText("print.png");
    expect(screen.getByRole("button", { name: /Enviar/ })).toBeDisabled();

    post.mockResolvedValue(detalhe("c1", []));
    await userEvent.type(screen.getByPlaceholderText(/Escreva sua mensagem/), "Obrigada!");
    await userEvent.click(screen.getByRole("button", { name: /Enviar/ }));

    await waitFor(() => expect(post).toHaveBeenCalledWith("/crm/suporte/c1/mensagens", { texto: "Obrigada!" }));
    expect(uploadFile).not.toHaveBeenCalled();
  });
});
