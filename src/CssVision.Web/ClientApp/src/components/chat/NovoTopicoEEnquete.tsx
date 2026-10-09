import { Plus, X } from "lucide-react";
import { useState } from "react";
import { api, ApiRequestError } from "../../lib/api";
import type { DiscordChatMensagem } from "../../lib/types";
import { Button, Checkbox, Input, Modal, useToast } from "../ui";

const DURACOES: { horas: number; rotulo: string }[] = [
  { horas: 1, rotulo: "1 hora" },
  { horas: 4, rotulo: "4 horas" },
  { horas: 8, rotulo: "8 horas" },
  { horas: 24, rotulo: "24 horas" },
  { horas: 72, rotulo: "3 dias" },
  { horas: 168, rotulo: "1 semana" },
];

/** "Criar tópico": abre uma thread pública no grupo e avisa o grupo, com o link, no nome de quem criou. */
export function NovoTopico({ chave, aberto, aoFechar, aoCriado }: { chave: string; aberto: boolean; aoFechar: () => void; aoCriado: (aviso: DiscordChatMensagem) => void }) {
  const { notificar } = useToast();
  const [nome, setNome] = useState("");
  const [criando, setCriando] = useState(false);

  async function criar() {
    if (!nome.trim() || criando) return;
    setCriando(true);
    try {
      const aviso = await api.post<DiscordChatMensagem>(`/crm/discord/chat/canais/${encodeURIComponent(chave)}/topicos`, { nome: nome.trim() });
      setNome("");
      aoCriado(aviso);
      aoFechar();
      notificar("success", "Tópico criado no Discord.");
    } catch (e) {
      notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível criar o tópico.");
    } finally {
      setCriando(false);
    }
  }

  return (
    <Modal
      open={aberto}
      onClose={aoFechar}
      title="Criar tópico"
      size="sm"
      footer={
        <>
          <Button variant="ghost" onClick={aoFechar} disabled={criando}>
            Cancelar
          </Button>
          <Button onClick={() => void criar()} loading={criando} disabled={!nome.trim()}>
            Criar tópico
          </Button>
        </>
      }
    >
      <form
        onSubmit={(e) => {
          e.preventDefault();
          void criar();
        }}
        className="space-y-2"
      >
        <label htmlFor="topico-nome" className="text-sm font-medium text-[var(--fg)]">
          Nome do tópico
        </label>
        <Input id="topico-nome" value={nome} onChange={(e) => setNome(e.target.value)} maxLength={100} autoFocus placeholder="Ex.: Metas de outubro" />
        <p className="text-xs text-[var(--fg-muted)]">O tópico é aberto no Discord, para o grupo conversar sobre o assunto em separado. O grupo recebe o aviso com o link.</p>
      </form>
    </Modal>
  );
}

/** "Criar enquete": pergunta, 2 a 10 respostas, duração e (opcional) várias escolhas. Votar é no Discord; o chat mostra os votos. */
export function NovaEnquete({ chave, aberto, aoFechar, aoCriada }: { chave: string; aberto: boolean; aoFechar: () => void; aoCriada: (enquete: DiscordChatMensagem) => void }) {
  const { notificar } = useToast();
  const [pergunta, setPergunta] = useState("");
  const [respostas, setRespostas] = useState(["", ""]);
  const [horas, setHoras] = useState(24);
  const [varias, setVarias] = useState(false);
  const [criando, setCriando] = useState(false);

  const preenchidas = respostas.filter((r) => r.trim()).length;
  const pronta = pergunta.trim().length > 0 && preenchidas >= 2;

  async function criar() {
    if (!pronta || criando) return;
    setCriando(true);
    try {
      const enquete = await api.post<DiscordChatMensagem>(`/crm/discord/chat/canais/${encodeURIComponent(chave)}/enquetes`, {
        pergunta: pergunta.trim(),
        respostas: respostas.map((r) => r.trim()).filter(Boolean),
        horas,
        variasEscolhas: varias,
      });
      setPergunta("");
      setRespostas(["", ""]);
      setHoras(24);
      setVarias(false);
      aoCriada(enquete);
      aoFechar();
    } catch (e) {
      notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível criar a enquete.");
    } finally {
      setCriando(false);
    }
  }

  return (
    <Modal
      open={aberto}
      onClose={aoFechar}
      title="Criar enquete"
      footer={
        <>
          <Button variant="ghost" onClick={aoFechar} disabled={criando}>
            Cancelar
          </Button>
          <Button onClick={() => void criar()} loading={criando} disabled={!pronta}>
            Publicar enquete
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <div className="space-y-1">
          <label htmlFor="enquete-pergunta" className="text-sm font-medium text-[var(--fg)]">
            Pergunta
          </label>
          <Input id="enquete-pergunta" value={pergunta} onChange={(e) => setPergunta(e.target.value)} maxLength={300} autoFocus placeholder="Sobre o que é a enquete?" />
        </div>

        <div className="space-y-2">
          <span className="text-sm font-medium text-[var(--fg)]">Respostas (2 a 10)</span>
          {respostas.map((r, i) => (
            <div key={i} className="flex items-center gap-2">
              <Input
                value={r}
                onChange={(e) => setRespostas((atual) => atual.map((x, j) => (j === i ? e.target.value : x)))}
                maxLength={55}
                placeholder={`Resposta ${i + 1}`}
                aria-label={`Resposta ${i + 1}`}
              />
              {respostas.length > 2 && (
                <button type="button" onClick={() => setRespostas((atual) => atual.filter((_, j) => j !== i))} aria-label={`Remover resposta ${i + 1}`} className="rounded p-1 text-[var(--fg-muted)] hover:bg-[var(--surface-hover)] hover:text-[var(--danger)]">
                  <X className="size-4" />
                </button>
              )}
            </div>
          ))}
          {respostas.length < 10 && (
            <Button type="button" variant="ghost" size="sm" onClick={() => setRespostas((atual) => [...atual, ""])}>
              <Plus className="size-4" /> Adicionar resposta
            </Button>
          )}
        </div>

        <div className="flex flex-wrap items-center gap-x-5 gap-y-2">
          <label className="flex items-center gap-2 text-sm text-[var(--fg)]">
            Duração
            <select
              value={horas}
              onChange={(e) => setHoras(Number(e.target.value))}
              className="rounded-lg border border-[var(--border)] bg-[var(--bg)] px-2 py-1 text-sm text-[var(--fg)]"
            >
              {DURACOES.map((d) => (
                <option key={d.horas} value={d.horas}>
                  {d.rotulo}
                </option>
              ))}
            </select>
          </label>
          <Checkbox label="Permitir mais de uma resposta" checked={varias} onChange={(e) => setVarias(e.target.checked)} />
        </div>
        <p className="text-xs text-[var(--fg-muted)]">A votação acontece no Discord. Aqui no chat você acompanha a pergunta e os votos.</p>
      </div>
    </Modal>
  );
}
