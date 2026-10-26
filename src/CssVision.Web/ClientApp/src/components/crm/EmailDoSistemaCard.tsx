import { CheckCircle2, MailWarning, Send } from "lucide-react";
import { useEffect, useState, type FormEvent } from "react";
import { ApiRequestError, api } from "../../lib/api";
import type { EmailSistema } from "../../lib/types";
import { Badge, Button, Card, Input, useToast } from "../ui";
import { EVENTO_EMAIL_SISTEMA_MUDOU } from "./AlertaEmailDoSistema";

/** Servidores conhecidos: ao escolher o provedor, host e porta já vêm preenchidos. */
const PROVEDORES: { rotulo: string; host: string; porta: number }[] = [
  { rotulo: "Gmail / Google Workspace", host: "smtp.gmail.com", porta: 587 },
  { rotulo: "Outlook / Microsoft 365", host: "smtp.office365.com", porta: 587 },
  { rotulo: "Amazon SES (us-east-1)", host: "email-smtp.us-east-1.amazonaws.com", porta: 587 },
];

/**
 * Administração → E-mail do sistema: o e-mail que o CRM usa para mandar o link de "Esqueci a senha". Guardado no CRM (senha protegida), testa o login
 * antes de salvar e tem um botão para mandar um e-mail de teste para o próprio administrador. O que for salvo aqui vale antes do que está no servidor.
 */
export function EmailDoSistemaCard({ aoMudar }: { aoMudar?: (e: EmailSistema) => void }) {
  const { notificar } = useToast();
  const [atual, setAtual] = useState<EmailSistema | null>(null);
  const [host, setHost] = useState("");
  const [porta, setPorta] = useState("587");
  const [usuario, setUsuario] = useState("");
  const [senha, setSenha] = useState("");
  const [de, setDe] = useState("");
  const [nome, setNome] = useState("CSS Brasil CRM");
  const [salvando, setSalvando] = useState(false);
  const [testando, setTestando] = useState(false);
  const [erro, setErro] = useState<string | null>(null);

  function preencher(e: EmailSistema) {
    setAtual(e);
    // Avisa a faixa "não configurado" do topo (no Shell) para ela sumir/voltar na hora.
    window.dispatchEvent(new CustomEvent(EVENTO_EMAIL_SISTEMA_MUDOU));
    setHost(e.host ?? "");
    setPorta(String(e.port ?? 587));
    setUsuario(e.userName ?? "");
    setDe(e.fromEmail ?? "");
    setNome(e.fromName || "CSS Brasil CRM");
    setSenha("");
  }

  useEffect(() => {
    api.get<EmailSistema>("/crm/email-sistema").then(preencher).catch(() => setErro("Não foi possível carregar a configuração do e-mail."));
  }, []);

  async function salvar(e: FormEvent) {
    e.preventDefault();
    setErro(null);
    setSalvando(true);
    try {
      const novo = await api.put<EmailSistema>("/crm/email-sistema", { host, port: Number(porta), userName: usuario, senha: senha || null, fromEmail: de, fromName: nome });
      preencher(novo);
      aoMudar?.(novo);
      notificar("success", "E-mail do sistema salvo. Mande um e-mail de teste para conferir.");
    } catch (ex) {
      setErro(ex instanceof ApiRequestError ? ex.message : "Não foi possível salvar o e-mail do sistema.");
    } finally {
      setSalvando(false);
    }
  }

  async function testar() {
    setTestando(true);
    setErro(null);
    try {
      const r = await api.post<{ enviado: boolean; destinatario: string }>("/crm/email-sistema/teste", {});
      if (r.enviado) notificar("success", `E-mail de teste enviado para ${r.destinatario}. Confira a caixa de entrada e o spam.`);
      else setErro("O servidor não conseguiu enviar o e-mail de teste. Confira os dados abaixo e salve de novo.");
    } catch (ex) {
      setErro(ex instanceof ApiRequestError ? ex.message : "Não foi possível enviar o e-mail de teste.");
    } finally {
      setTestando(false);
    }
  }

  async function remover() {
    try {
      await api.del("/crm/email-sistema");
      const novo = await api.get<EmailSistema>("/crm/email-sistema");
      preencher(novo);
      aoMudar?.(novo);
      notificar("success", "Configuração do painel removida.");
    } catch {
      notificar("error", "Não foi possível remover a configuração.");
    }
  }

  return (
    <Card className="!rounded-2xl p-5">
      <div className="mb-4 flex flex-wrap items-start justify-between gap-3">
        <div className="flex items-center gap-3">
          <span className="flex size-9 items-center justify-center rounded-xl bg-[var(--surface-hover)] text-[var(--fg-muted)]">
            {atual?.configurado ? <CheckCircle2 className="size-5 text-[var(--success)]" aria-hidden /> : <MailWarning className="size-5 text-[var(--warning)]" aria-hidden />}
          </span>
          <div>
            <h2 className="text-[15px] font-bold leading-tight text-[var(--fg)]">E-mail do sistema</h2>
            <p className="text-xs text-[var(--fg-muted)]">É por ele que sai o link de "Esqueci a senha". Sem isso, ninguém recebe o e-mail de redefinição.</p>
          </div>
        </div>
        {atual && (
          <Badge variant={atual.configurado ? "success" : "warning"}>
            {atual.origem === "painel" ? "Configurado aqui" : atual.origem === "servidor" ? "Configurado no servidor" : "Não configurado"}
          </Badge>
        )}
      </div>

      {atual?.configurado && (
        <div role="status" className="mb-4 flex items-start gap-2.5 rounded-xl border border-[color-mix(in_srgb,var(--success)_35%,transparent)] bg-[var(--success-soft)] px-3.5 py-3 text-sm text-[var(--fg)]">
          <CheckCircle2 className="mt-0.5 size-4 shrink-0 text-[var(--success)]" aria-hidden />
          <p>
            <strong>O e-mail do sistema já está configurado.</strong> O link de "Esqueci a senha" sai por <strong>{atual.host}</strong> com o remetente{" "}
            <strong>{atual.fromEmail}</strong>
            {atual.origem === "servidor" ? " (configuração do servidor)" : ""}. Para conferir, use "Enviar e-mail de teste para mim".
          </p>
        </div>
      )}

      <form onSubmit={salvar} className="grid gap-3 sm:grid-cols-2" noValidate>
        <label className="text-sm font-medium text-[var(--fg)] sm:col-span-2">
          Provedor
          <select
            className="focus-ring mt-1 h-9 w-full rounded-lg border border-[var(--border)] bg-[var(--field)] px-3 text-sm text-[var(--fg)]"
            value=""
            onChange={(e) => {
              const p = PROVEDORES.find((x) => x.host === e.target.value);
              if (p) {
                setHost(p.host);
                setPorta(String(p.porta));
              }
            }}
          >
            <option value="">Escolher para preencher servidor e porta…</option>
            {PROVEDORES.map((p) => (
              <option key={p.host} value={p.host}>
                {p.rotulo}
              </option>
            ))}
          </select>
        </label>
        <label className="text-sm font-medium text-[var(--fg)]">
          Servidor SMTP
          <Input className="mt-1" value={host} onChange={(e) => setHost(e.target.value)} placeholder="smtp.gmail.com" autoComplete="off" />
        </label>
        <label className="text-sm font-medium text-[var(--fg)]">
          Porta
          <Input className="mt-1" inputMode="numeric" value={porta} onChange={(e) => setPorta(e.target.value.replace(/\D/g, ""))} placeholder="587" autoComplete="off" />
        </label>
        <label className="text-sm font-medium text-[var(--fg)]">
          Usuário
          <Input className="mt-1" value={usuario} onChange={(e) => setUsuario(e.target.value)} placeholder="crm@suaempresa.com.br" autoComplete="off" />
        </label>
        <label className="text-sm font-medium text-[var(--fg)]">
          Senha {atual?.origem === "painel" && <span className="font-normal text-[var(--fg-muted)]">(em branco mantém a atual)</span>}
          <Input className="mt-1" type="password" value={senha} onChange={(e) => setSenha(e.target.value)} placeholder={atual?.origem === "painel" ? "••••••••" : "Senha de app"} autoComplete="new-password" />
        </label>
        <label className="text-sm font-medium text-[var(--fg)]">
          E-mail remetente
          <Input className="mt-1" type="email" value={de} onChange={(e) => setDe(e.target.value)} placeholder="crm@suaempresa.com.br" autoComplete="off" />
        </label>
        <label className="text-sm font-medium text-[var(--fg)]">
          Nome do remetente
          <Input className="mt-1" value={nome} onChange={(e) => setNome(e.target.value)} placeholder="CSS Brasil CRM" autoComplete="off" />
        </label>

        {erro && (
          <p role="alert" className="rounded-lg bg-[var(--danger-soft)] px-3 py-2 text-sm text-[var(--danger)] sm:col-span-2">
            {erro}
          </p>
        )}

        <div className="flex flex-wrap items-center gap-2 sm:col-span-2">
          <Button type="submit" loading={salvando}>
            Testar login e salvar
          </Button>
          <Button type="button" variant="secondary" loading={testando} disabled={!atual?.configurado} onClick={testar}>
            <Send className="size-4" aria-hidden /> Enviar e-mail de teste para mim
          </Button>
          {atual?.origem === "painel" && (
            <button type="button" onClick={remover} className="focus-ring cursor-pointer text-sm font-medium text-[var(--fg-muted)] hover:text-[var(--danger)]">
              Remover e usar a do servidor
            </button>
          )}
        </div>
      </form>
      <p className="mt-3 text-[11px] text-[var(--fg-muted)]">No Gmail e no Outlook use uma "senha de app" (não a senha normal da conta). A senha fica guardada protegida e nunca volta para a tela.</p>
    </Card>
  );
}
