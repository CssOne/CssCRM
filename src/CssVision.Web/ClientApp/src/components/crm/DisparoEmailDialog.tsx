import { History, Mail, Send, Sparkles } from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import { api, ApiRequestError } from "../../lib/api";
import { formatarDataHora } from "../../lib/format";
import { sugestaoParaEtapa } from "../../lib/sugestoesEmail";
import type { LeadStage } from "../../lib/types";
import { Badge, Button, Input, Modal, Select, Textarea, useToast } from "../ui";

interface Previa {
  destinatarios: number;
  limite: number;
  descadastrados: number;
}

type StatusDisparo = "Pendente" | "Enviando" | "Concluido" | "Falhou";

interface Disparo {
  id: string;
  etapaNome: string;
  assunto: string;
  status: StatusDisparo;
  total: number;
  enviados: number;
  falhas: number;
  erro?: string | null;
  criadoEm: string;
  concluidoEm?: string | null;
}

interface ContaEmail {
  email: string;
  host: string;
  port: number;
}

const STATUS_ROTULO: Record<StatusDisparo, string> = { Pendente: "Na fila", Enviando: "Enviando", Concluido: "Concluído", Falhou: "Falhou" };
const STATUS_COR: Record<StatusDisparo, "neutral" | "info" | "success" | "danger"> = { Pendente: "neutral", Enviando: "info", Concluido: "success", Falhou: "danger" };
const emAndamento = (d: Disparo) => d.status === "Pendente" || d.status === "Enviando";

/** O consultor conecta o próprio e-mail (senha de app), dispara uma mensagem para os clientes dele que estão em uma etapa e acompanha o envio no histórico. */
export function DisparoEmailDialog({ open, onClose, etapas }: { open: boolean; onClose: () => void; etapas: LeadStage[] }) {
  const { notificar } = useToast();
  const opcoes = etapas.filter((e) => e.id && e.ativa);
  const [aba, setAba] = useState<"novo" | "historico">("novo");
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
  const [historico, setHistorico] = useState<Disparo[] | null>(null);

  const carregarHistorico = useCallback(() => {
    api
      .get<Disparo[]>("/crm/disparo-email/historico")
      .then(setHistorico)
      .catch(() => setHistorico((atual) => atual ?? []));
  }, []);

  useEffect(() => {
    if (!open) return;
    setAba("novo");
    setConta(undefined);
    setTrocandoConta(false);
    api
      .get<ContaEmail | undefined>("/crm/disparo-email/conta")
      .then((c) => setConta(c ?? null))
      .catch(() => setConta(null));
    carregarHistorico();
  }, [open, carregarHistorico]);

  // Enquanto houver disparo na fila ou enviando, o histórico se atualiza sozinho.
  const temDisparoAtivo = !!historico?.some(emAndamento);
  useEffect(() => {
    if (!open || !temDisparoAtivo) return;
    const timer = setInterval(carregarHistorico, 3000);
    return () => clearInterval(timer);
  }, [open, temDisparoAtivo, carregarHistorico]);

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
      .catch(() => atual && setPrevia({ destinatarios: 0, limite: 0, descadastrados: 0 }));
    return () => {
      atual = false;
    };
  }, [etapaId]);

  const etapaEscolhida = opcoes.find((e) => e.id === etapaId);
  const excedeLimite = !!previa && previa.destinatarios > previa.limite && previa.limite > 0;
  const podeEnviar =
    !!etapaId && assunto.trim().length >= 3 && mensagem.trim().length >= 3 && !!previa && previa.destinatarios > 0 && !excedeLimite && !enviando && !temDisparoAtivo;
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
      const d = await api.post<Disparo>("/crm/disparo-email", { etapaId, assunto: assunto.trim(), mensagem: mensagem.trim() });
      notificar("success", `Disparo para ${d.total} cliente(s) na fila. Acompanhe pelo histórico.`);
      setAssunto("");
      setMensagem("");
      setEtapaId("");
      carregarHistorico();
      setAba("historico");
    } catch (e) {
      if (e instanceof ApiRequestError && (e.codigo === "conta_invalida" || e.codigo === "conta_nao_configurada")) setTrocandoConta(true);
      if (e instanceof ApiRequestError && e.codigo === "disparo_em_andamento") carregarHistorico();
      notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível enfileirar o disparo.");
    } finally {
      setEnviando(false);
    }
  }

  const abaBotao = (valor: typeof aba, rotulo: string, icone: React.ReactNode) => (
    <button
      type="button"
      role="tab"
      aria-selected={aba === valor}
      onClick={() => setAba(valor)}
      className={`focus-ring flex cursor-pointer items-center gap-1.5 border-b-2 px-3 py-2 text-sm font-medium transition-colors ${
        aba === valor ? "border-[var(--brand)] text-[var(--brand)]" : "border-transparent text-[var(--fg-muted)] hover:text-[var(--fg)]"
      }`}
    >
      {icone} {rotulo}
    </button>
  );

  const rodape =
    aba === "historico" ? (
      <Button variant="secondary" onClick={onClose}>
        Fechar
      </Button>
    ) : mostrandoConta ? (
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
    );

  return (
    <Modal open={open} onClose={onClose} title="Disparar e-mail para meus clientes" footer={rodape}>
      <div role="tablist" className="mb-3 flex border-b border-[var(--border)]">
        {abaBotao("novo", "Novo disparo", <Mail className="size-4" aria-hidden />)}
        {abaBotao("historico", `Histórico${temDisparoAtivo ? " •" : ""}`, <History className="size-4" aria-hidden />)}
      </div>

      {aba === "historico" ? (
        <Historico historico={historico} />
      ) : conta === undefined ? (
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
          {temDisparoAtivo && (
            <p role="status" className="rounded-lg bg-[var(--brand-soft)] px-3 py-2 text-sm text-[var(--brand)]">
              Você tem um disparo em andamento. Quando ele terminar, dá para começar outro — veja o andamento no Histórico.
            </p>
          )}
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
              {previa && previa.descadastrados > 0 && ` ${previa.descadastrados} cancelou(aram) a inscrição e fica(m) de fora.`}
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
            Use {"{nome}"} para o primeiro nome do cliente (vale no assunto também). Cada e-mail leva no rodapé o seu nome, seu telefone, um botão "Chamar no WhatsApp" (se o seu
            telefone estiver no cadastro) e o link para o cliente cancelar a inscrição. O envio é feito em segundo plano, em lotes.
          </p>
        </div>
      )}
    </Modal>
  );
}

function Historico({ historico }: { historico: Disparo[] | null }) {
  if (historico === null) return <p className="text-sm text-[var(--fg-muted)]">Carregando…</p>;
  if (historico.length === 0) return <p className="text-sm text-[var(--fg-muted)]">Você ainda não fez nenhum disparo.</p>;
  return (
    <ul className="space-y-2">
      {historico.map((d) => {
        const feitos = d.enviados + d.falhas;
        const percentual = d.total > 0 ? Math.round((feitos / d.total) * 100) : 0;
        return (
          <li key={d.id} className="rounded-xl border border-[var(--border)] p-3 text-sm">
            <div className="flex items-start justify-between gap-2">
              <p className="min-w-0 truncate font-medium text-[var(--fg)]" title={d.assunto}>
                {d.assunto}
              </p>
              <Badge variant={STATUS_COR[d.status]}>{STATUS_ROTULO[d.status]}</Badge>
            </div>
            <p className="mt-0.5 text-xs text-[var(--fg-muted)]">
              {d.etapaNome} · {formatarDataHora(d.criadoEm)}
            </p>
            {emAndamento(d) && (
              <div className="mt-2" role="progressbar" aria-valuenow={percentual} aria-valuemin={0} aria-valuemax={100} aria-label="Andamento do disparo">
                <div className="h-1.5 overflow-hidden rounded-full bg-[var(--surface-hover)]">
                  <div className="h-full rounded-full bg-[var(--brand)] transition-all" style={{ width: `${percentual}%` }} />
                </div>
              </div>
            )}
            <p className="mt-1.5 text-xs text-[var(--fg-muted)]">
              {d.enviados} de {d.total} enviado(s){d.falhas > 0 ? ` · ${d.falhas} com falha` : ""}
            </p>
            {d.erro && <p className="mt-1 text-xs text-[var(--danger)]">{d.erro}</p>}
          </li>
        );
      })}
    </ul>
  );
}
