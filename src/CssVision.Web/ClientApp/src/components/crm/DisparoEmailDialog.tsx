import { Send, Sparkles } from "lucide-react";
import { useEffect, useState } from "react";
import { api, ApiRequestError } from "../../lib/api";
import { sugestaoParaEtapa } from "../../lib/sugestoesEmail";
import type { LeadStage } from "../../lib/types";
import { Button, Input, Modal, Select, Textarea, useToast } from "../ui";

interface Previa {
  destinatarios: number;
  limite: number;
}

interface Resultado {
  destinatarios: number;
  enviados: number;
  falhas: number;
}

/** O consultor escreve um e-mail e ele sai para os clientes dele (com e-mail) que estão na etapa escolhida. */
export function DisparoEmailDialog({ open, onClose, etapas }: { open: boolean; onClose: () => void; etapas: LeadStage[] }) {
  const { notificar } = useToast();
  const opcoes = etapas.filter((e) => e.id && e.ativa);
  const [etapaId, setEtapaId] = useState("");
  const [assunto, setAssunto] = useState("");
  const [mensagem, setMensagem] = useState("");
  const [previa, setPrevia] = useState<Previa | null>(null);
  const [enviando, setEnviando] = useState(false);

  useEffect(() => {
    if (!etapaId) {
      setPrevia(null);
      return;
    }
    let atual = true;
    setPrevia(null);
    api
      .get<Previa>(`/crm/disparo-email/previa?etapaId=${etapaId}`)
      .then((p) => atual && setPrevia(p))
      .catch(() => atual && setPrevia({ destinatarios: 0, limite: 0 }));
    return () => {
      atual = false;
    };
  }, [etapaId]);

  const etapaEscolhida = opcoes.find((e) => e.id === etapaId);
  const excedeLimite = !!previa && previa.destinatarios > previa.limite && previa.limite > 0;
  const podeEnviar = !!etapaId && assunto.trim().length >= 3 && mensagem.trim().length >= 3 && !!previa && previa.destinatarios > 0 && !excedeLimite && !enviando;

  function usarSugestao() {
    if (!etapaEscolhida) return;
    const s = sugestaoParaEtapa(etapaEscolhida.nome);
    setAssunto(s.assunto);
    setMensagem(s.mensagem);
  }

  async function enviar() {
    setEnviando(true);
    try {
      const r = await api.post<Resultado>("/crm/disparo-email", { etapaId, assunto: assunto.trim(), mensagem: mensagem.trim() });
      notificar(r.falhas > 0 ? "error" : "success", r.falhas > 0 ? `${r.enviados} e-mails enviados e ${r.falhas} com falha.` : `${r.enviados} e-mails enviados.`);
      setAssunto("");
      setMensagem("");
      onClose();
    } catch (e) {
      notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível enviar os e-mails.");
    } finally {
      setEnviando(false);
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Disparar e-mail para meus clientes"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={enviando}>
            Cancelar
          </Button>
          <Button onClick={enviar} disabled={!podeEnviar}>
            <Send className="size-4" /> {enviando ? "Enviando…" : previa ? `Enviar para ${previa.destinatarios}` : "Enviar"}
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <label className="block text-sm font-medium text-[var(--fg)]">
          Etapa
          <Select value={etapaId} onChange={(e) => setEtapaId(e.target.value)} className="mt-1">
            <option value="">Escolha a etapa</option>
            {opcoes.map((e) => (
              <option key={e.id!} value={e.id!}>
                {e.nome}
              </option>
            ))}
          </Select>
        </label>
        {etapaId && (
          <p className="text-sm text-[var(--fg-muted)]">
            {previa === null
              ? "Contando clientes…"
              : previa.destinatarios === 0
                ? "Você não tem clientes com e-mail nessa etapa."
                : excedeLimite
                  ? `São ${previa.destinatarios} clientes — o limite por disparo é ${previa.limite}.`
                  : `${previa.destinatarios} cliente(s) seu(s) com e-mail nessa etapa vão receber.`}
          </p>
        )}
        {etapaEscolhida && (
          <Button variant="secondary" size="sm" type="button" onClick={usarSugestao}>
            <Sparkles className="size-4" /> Usar mensagem sugerida para "{etapaEscolhida.nome}"
          </Button>
        )}
        <label className="block text-sm font-medium text-[var(--fg)]">
          Assunto
          <Input className="mt-1" value={assunto} maxLength={150} onChange={(e) => setAssunto(e.target.value)} />
        </label>
        <label className="block text-sm font-medium text-[var(--fg)]">
          Mensagem
          <Textarea className="mt-1" rows={7} value={mensagem} maxLength={5000} onChange={(e) => setMensagem(e.target.value)} placeholder="Olá, {nome}! …" />
        </label>
        <p className="text-xs text-[var(--fg-muted)]">
          Use {"{nome}"} para o primeiro nome do cliente. O e-mail sai com o seu nome e as respostas chegam no seu e-mail.
        </p>
      </div>
    </Modal>
  );
}
