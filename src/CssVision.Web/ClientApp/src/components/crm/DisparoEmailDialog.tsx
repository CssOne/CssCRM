import { Mail, Send, Sparkles } from "lucide-react";
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

interface ContaEmail {
  email: string;
  host: string;
  port: number;
}

/** O consultor conecta o próprio e-mail (senha de app) e dispara uma mensagem para os clientes dele que estão em uma etapa. */
export function DisparoEmailDialog({ open, onClose, etapas }: { open: boolean; onClose: () => void; etapas: LeadStage[] }) {
  const { notificar } = useToast();
  const opcoes = etapas.filter((e) => e.id && e.ativa);
  // undefined = carregando; null = ainda não conectou o e-mail.
  const [conta, setConta] = useState<ContaEmail | null | undefined>(undefined);
  const [trocandoConta, setTrocandoConta] = useState(false);
  const [email, setEmail] = useState("");
  const [senha, setSenha] = useState("");
  const [host, setHost] = useState("");
  const [porta, setPorta] = useState("");
  const [salvandoConta, setSalvandoConta] = useState(false);

  const [etapaId, setEtapaId] = useState("");
  const [assunto, setAssunto] = useState("");
  const [mensagem, setMensagem] = useState("");
  const [previa, setPrevia] = useState<Previa | null>(null);
  const [enviando, setEnviando] = useState(false);

  useEffect(() => {
    if (!open) return;
    setConta(undefined);
    setTrocandoConta(false);
    api
      .get<ContaEmail | undefined>("/crm/disparo-email/conta")
      .then((c) => setConta(c ?? null))
      .catch(() => setConta(null));
  }, [open]);

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
  const mostrandoConta = conta === null || trocandoConta;
  const podeSalvarConta = email.includes("@") && senha.trim().length >= 4 && !salvandoConta;

  function usarSugestao() {
    if (!etapaEscolhida) return;
    const s = sugestaoParaEtapa(etapaEscolhida.nome);
    setAssunto(s.assunto);
    setMensagem(s.mensagem);
  }

  async function salvarConta() {
    setSalvandoConta(true);
    try {
      const nova = await api.put<ContaEmail>("/crm/disparo-email/conta", {
        email: email.trim(),
        senha: senha.trim(),
        host: host.trim() || null,
        port: porta ? Number(porta) : null,
      });
      setConta(nova);
      setTrocandoConta(false);
      setSenha("");
      notificar("success", `E-mail ${nova.email} conectado.`);
    } catch (e) {
      notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível conectar o e-mail.");
    } finally {
      setSalvandoConta(false);
    }
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
      if (e instanceof ApiRequestError && (e.codigo === "conta_invalida" || e.codigo === "conta_nao_configurada")) setTrocandoConta(true);
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
        mostrandoConta ? (
          <>
            <Button variant="secondary" onClick={conta ? () => setTrocandoConta(false) : onClose} disabled={salvandoConta}>
              {conta ? "Voltar" : "Cancelar"}
            </Button>
            <Button onClick={salvarConta} disabled={!podeSalvarConta}>
              <Mail className="size-4" /> {salvandoConta ? "Testando…" : "Conectar e-mail"}
            </Button>
          </>
        ) : (
          <>
            <Button variant="secondary" onClick={onClose} disabled={enviando}>
              Cancelar
            </Button>
            <Button onClick={enviar} disabled={!podeEnviar || conta === undefined}>
              <Send className="size-4" /> {enviando ? "Enviando…" : previa ? `Enviar para ${previa.destinatarios}` : "Enviar"}
            </Button>
          </>
        )
      }
    >
      {conta === undefined ? (
        <p className="text-sm text-[var(--fg-muted)]">Carregando…</p>
      ) : mostrandoConta ? (
        <div className="space-y-3">
          <p className="text-sm text-[var(--fg-muted)]">
            Os e-mails saem do <strong className="text-[var(--fg)]">seu próprio e-mail</strong>. Informe o endereço e uma <strong className="text-[var(--fg)]">senha de app</strong> (no Gmail:
            Conta Google → Segurança → Verificação em duas etapas → Senhas de app; não use a senha normal). A senha fica guardada protegida e só é usada para enviar.
          </p>
          <label className="block text-sm font-medium text-[var(--fg)]">
            Seu e-mail
            <Input className="mt-1" type="email" autoComplete="off" value={email} onChange={(e) => setEmail(e.target.value)} placeholder="voce@gmail.com" />
          </label>
          <label className="block text-sm font-medium text-[var(--fg)]">
            Senha de app
            <Input className="mt-1" type="password" autoComplete="new-password" value={senha} onChange={(e) => setSenha(e.target.value)} />
          </label>
          <details className="text-sm text-[var(--fg-muted)]">
            <summary className="cursor-pointer">E-mail de outro provedor/domínio próprio</summary>
            <div className="mt-2 grid grid-cols-3 gap-2">
              <Input className="col-span-2" value={host} onChange={(e) => setHost(e.target.value)} placeholder="Servidor SMTP (smtp.seudominio.com.br)" />
              <Input value={porta} inputMode="numeric" onChange={(e) => setPorta(e.target.value.replace(/\D/g, ""))} placeholder="Porta (587)" />
            </div>
          </details>
        </div>
      ) : (
        <div className="space-y-3">
          <p className="text-sm text-[var(--fg-muted)]">
            Enviando de <strong className="text-[var(--fg)]">{conta?.email}</strong> ·{" "}
            <button type="button" className="underline" onClick={() => setTrocandoConta(true)}>
              trocar e-mail
            </button>
          </p>
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
            Use {"{nome}"} para o primeiro nome do cliente. Cada e-mail leva no rodapé o seu nome, seu telefone e um botão "Chamar no WhatsApp" (se o seu telefone estiver no cadastro).
          </p>
        </div>
      )}
    </Modal>
  );
}
