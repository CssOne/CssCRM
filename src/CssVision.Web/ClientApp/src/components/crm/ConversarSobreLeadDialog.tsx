import { Hash } from "lucide-react";
import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { api, ApiRequestError, isAbortError } from "../../lib/api";
import type { DiscordChatCanal } from "../../lib/types";
import { Avatar, Button, Label, Modal, Skeleton, Textarea, useToast } from "../ui";

const LIMITE_COMENTARIO = 500;

/**
 * "Conversar sobre este lead": publica numa conversa do chat (grupo ou 1:1) um resumo do lead — nome, etapa, responsável e produto, sem
 * telefone, e-mail nem documento — com o link para abri-lo no CRM. A equipe conversa por ali (e no Discord, inclusive no celular).
 */
export function ConversarSobreLeadDialog({ open, leadId, nomeDoLead, onClose }: { open: boolean; leadId: string; nomeDoLead: string; onClose: () => void }) {
  const { notificar } = useToast();
  const [canais, setCanais] = useState<DiscordChatCanal[] | null>(null);
  const [escolhido, setEscolhido] = useState<string | null>(null);
  const [comentario, setComentario] = useState("");
  const [enviando, setEnviando] = useState(false);
  const [enviadoPara, setEnviadoPara] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    setEscolhido(null);
    setComentario("");
    setEnviadoPara(null);
    setCanais(null);
    const controller = new AbortController();
    api
      .get<DiscordChatCanal[]>("/crm/discord/chat/canais", controller.signal)
      .then(setCanais)
      .catch((e) => {
        if (!isAbortError(e)) setCanais([]);
      });
    return () => controller.abort();
  }, [open]);

  async function enviar() {
    if (!escolhido) return;
    setEnviando(true);
    try {
      await api.post(`/crm/discord/chat/canais/${encodeURIComponent(escolhido)}/lead`, { leadId, comentario: comentario.trim() || null });
      setEnviadoPara(canais?.find((c) => c.chave === escolhido)?.nome ?? "a conversa");
      notificar("success", "Lead compartilhado no chat.");
    } catch (e) {
      notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível compartilhar o lead.");
    } finally {
      setEnviando(false);
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Conversar sobre este lead"
      size="md"
      footer={
        enviadoPara ? (
          <Button onClick={onClose}>Fechar</Button>
        ) : (
          <>
            <Button variant="secondary" onClick={onClose} disabled={enviando}>
              Cancelar
            </Button>
            <Button onClick={enviar} loading={enviando} disabled={!escolhido}>
              Enviar para o chat
            </Button>
          </>
        )
      }
    >
      {enviadoPara ? (
        <div className="space-y-2 text-sm text-[var(--fg)]">
          <p>
            O lead <strong>{nomeDoLead}</strong> foi publicado em <strong>{enviadoPara}</strong>.
          </p>
          <Link to="/app/chat" className="font-medium text-[var(--brand)] hover:underline" onClick={onClose}>
            Abrir o chat
          </Link>
        </div>
      ) : (
        <div className="space-y-4">
          <p className="text-xs text-[var(--fg-muted)]">
            Vai o nome do lead, a etapa, o responsável, o produto e o link para abri-lo no CRM. Telefone, e-mail e documento <strong>não</strong> vão.
          </p>

          {!canais ? (
            <Skeleton className="h-24" />
          ) : canais.length === 0 ? (
            <p className="text-sm text-[var(--fg-muted)]">Nenhuma conversa disponível. O chat ainda não foi ativado ou os grupos não foram criados no Discord.</p>
          ) : (
            <fieldset className="space-y-1">
              <legend className="mb-1 text-sm font-medium text-[var(--fg)]">Em qual conversa?</legend>
              <ul className="max-h-56 space-y-1 overflow-y-auto">
                {canais.map((c) => (
                  <li key={c.chave}>
                    <label className={`flex cursor-pointer items-center gap-2 rounded-lg border px-3 py-2 text-sm ${escolhido === c.chave ? "border-[var(--brand)] bg-[var(--brand)]/10" : "border-[var(--border)] hover:bg-[var(--bg-muted)]"}`}>
                      <input type="radio" name="conversa" checked={escolhido === c.chave} onChange={() => setEscolhido(c.chave)} className="sr-only" />
                      {c.tipo === "direta" ? <Avatar nome={c.nome} fotoUrl={c.fotoUrl} className="size-5 text-[9px]" /> : <Hash className="size-4 shrink-0 text-[var(--fg-muted)]" aria-hidden />}
                      <span className="truncate">{c.nome}</span>
                      {c.tipo === "direta" && <span className="ml-auto text-xs text-[var(--fg-muted)]">conversa direta</span>}
                    </label>
                  </li>
                ))}
              </ul>
            </fieldset>
          )}

          <div>
            <Label htmlFor="comentario-lead">Comentário (opcional)</Label>
            <Textarea id="comentario-lead" rows={3} maxLength={LIMITE_COMENTARIO} value={comentario} onChange={(e) => setComentario(e.target.value)} placeholder="Ex.: alguém já atendeu esse cliente?" />
          </div>
        </div>
      )}
    </Modal>
  );
}

