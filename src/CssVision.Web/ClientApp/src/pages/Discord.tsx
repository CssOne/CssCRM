import { CheckCircle2, MessageCircle, RefreshCw, Send, Smartphone, Unlink, Users } from "lucide-react";
import { useCallback, useEffect, useRef, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { api, ApiRequestError, isAbortError } from "../lib/api";
import { useAuth } from "../context/AuthContext";
import { formatarDataHora } from "../lib/format";
import type { DiscordAvisosCanais, DiscordCanal, DiscordIniciar, DiscordSincronizacao, DiscordStatus, DiscordTeste } from "../lib/types";
import { Badge, Button, Card, Checkbox, ErrorState, Skeleton, useToast } from "../components/ui";

/**
 * Vínculo da conta do Discord com o CRM. Vinculada, a pessoa recebe os avisos do CRM (lead novo, pagamento em aberto, alertas...)
 * como mensagem direta no Discord — e, com o app instalado, no celular. O vínculo é por OAuth2 do próprio Discord: o CRM nunca vê
 * a senha dele.
 */
export function DiscordPage() {
  const { notificar } = useToast();
  const [params, setParams] = useSearchParams();
  const [status, setStatus] = useState<DiscordStatus | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [ocupado, setOcupado] = useState<null | "vincular" | "teste" | "desvincular" | "avisos">(null);

  const carregar = useCallback((sinal?: AbortSignal) => {
    api
      .get<DiscordStatus>("/crm/discord/status", sinal)
      .then((s) => {
        setStatus(s);
        setErro(null);
      })
      .catch((e) => {
        if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar o Discord.");
      });
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    carregar(controller.signal);
    return () => controller.abort();
  }, [carregar]);

  // Volta do Discord: o servidor redireciona para cá com ?resultado=ok|erro|cancelado. A trava evita avisar duas vezes: o efeito pode
  // rodar de novo antes de o endereço ser limpo (React StrictMode no desenvolvimento, ou uma nova renderização logo em seguida).
  const retornoTratado = useRef(false);
  useEffect(() => {
    const resultado = params.get("resultado");
    if (!resultado) {
      retornoTratado.current = false;
      return;
    }
    if (retornoTratado.current) return;
    retornoTratado.current = true;
    if (resultado === "ok") notificar("success", "Conta do Discord vinculada.");
    else if (resultado === "cancelado") notificar("info", "O vínculo foi cancelado.");
    else notificar("error", params.get("motivo") ?? "Não foi possível vincular a conta do Discord.");
    setParams({}, { replace: true });
    carregar();
  }, [params, setParams, notificar, carregar]);

  async function acao<T>(qual: NonNullable<typeof ocupado>, executar: () => Promise<T>): Promise<T | undefined> {
    setOcupado(qual);
    try {
      return await executar();
    } catch (e) {
      notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível concluir a ação.");
      return undefined;
    } finally {
      setOcupado(null);
    }
  }

  const vincular = () =>
    acao("vincular", async () => {
      const r = await api.post<DiscordIniciar>("/crm/discord/vinculo/iniciar");
      window.location.href = r.url; // vai ao Discord autorizar; ele volta para /api/crm/discord/callback
    });

  const testar = () =>
    acao("teste", async () => {
      const r = await api.post<DiscordTeste>("/crm/discord/teste");
      notificar(r.enviado ? "success" : "error", r.mensagem);
    });

  const desvincular = () =>
    acao("desvincular", async () => {
      await api.del("/crm/discord/vinculo");
      notificar("success", "Conta do Discord desvinculada.");
      carregar();
    });

  const alternarAvisos = (ativos: boolean) =>
    acao("avisos", async () => {
      setStatus(await api.put<DiscordStatus>("/crm/discord/avisos", { ativos }));
    });

  return (
    <div className="mx-auto max-w-2xl space-y-4">
      <div>
        <h1 className="flex items-center gap-2 text-xl font-bold text-[var(--fg)]">
          <MessageCircle className="size-5 text-[var(--brand)]" aria-hidden /> Discord
        </h1>
        <p className="text-sm text-[var(--fg-muted)]">
          Vincule sua conta do Discord para receber os avisos do CRM no celular: leads novos, pagamentos em aberto e alertas.
        </p>
      </div>

      {erro ? (
        <ErrorState message={erro} onRetry={() => carregar()} />
      ) : !status ? (
        <Skeleton className="h-40" />
      ) : !status.configurado ? (
        <Card className="space-y-2 p-5">
          <Badge variant="warning">Ainda não ativado</Badge>
          <p className="text-sm text-[var(--fg)]">
            A integração com o Discord ainda não foi configurada no servidor. Peça ao administrador para ativá-la; depois é só voltar aqui e vincular a sua conta.
          </p>
        </Card>
      ) : !status.vinculado ? (
        <Card className="space-y-4 p-5">
          <ol className="list-decimal space-y-1 pl-5 text-sm text-[var(--fg)]">
            <li>Instale o app do Discord no celular e entre com a sua conta (ou crie uma).</li>
            <li>Clique em <strong>Vincular Discord</strong> e autorize. Você entra no servidor da empresa automaticamente.</li>
            <li>Deixe as notificações do Discord ligadas no celular.</li>
          </ol>
          <Button onClick={vincular} loading={ocupado === "vincular"}>
            <MessageCircle className="size-4" /> Vincular Discord
          </Button>
        </Card>
      ) : (
        <Card className="space-y-4 p-5">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div>
              <p className="flex items-center gap-2 font-semibold text-[var(--fg)]">
                <CheckCircle2 className="size-4 text-[var(--success)]" aria-hidden /> {status.discordNome}
              </p>
              {status.vinculadoEm && <p className="text-xs text-[var(--fg-muted)]">Vinculado em {formatarDataHora(status.vinculadoEm)}</p>}
            </div>
            <Badge variant={status.noServidor ? "success" : "warning"}>{status.noServidor ? "No servidor da empresa" : "Fora do servidor"}</Badge>
          </div>

          {!status.noServidor && (
            <p className="text-xs text-[var(--fg-muted)]">
              O bot não conseguiu colocar sua conta no servidor da empresa. Os avisos diretos funcionam mesmo assim; para entrar nos grupos, peça o convite ao administrador.
            </p>
          )}

          <Checkbox
            label="Receber os avisos do CRM no Discord"
            checked={status.avisosAtivos}
            disabled={ocupado === "avisos"}
            onChange={(e) => alternarAvisos(e.target.checked)}
          />

          <p className="flex items-start gap-2 text-xs text-[var(--fg-muted)]">
            <Smartphone className="mt-px size-4 shrink-0" aria-hidden />
            Para aparecer no celular, o app do Discord precisa estar instalado com as notificações ligadas. Se o aviso de teste não chegar, no Discord vá em
            Configurações → Privacidade e permita mensagens diretas de membros do servidor.
          </p>

          <div className="flex flex-wrap gap-2">
            <Button variant="secondary" onClick={testar} loading={ocupado === "teste"}>
              <Send className="size-4" /> Enviar aviso de teste
            </Button>
            <Button variant="ghost" onClick={desvincular} loading={ocupado === "desvincular"}>
              <Unlink className="size-4" /> Desvincular
            </Button>
          </div>
        </Card>
      )}

      {status?.configurado && <GruposDoDiscord />}
    </div>
  );
}

/**
 * Administração dos grupos: um canal (e um cargo) no Discord para cada regional e grupo do CRM, mais "Geral" e "Gestão". Só quem tem
 * visão total vê. A sincronização cria o que falta e acerta os cargos de cada pessoa que vinculou a conta; pode ser repetida à vontade.
 */
function GruposDoDiscord() {
  const { temPapel } = useAuth();
  const { notificar } = useToast();
  const [canais, setCanais] = useState<DiscordCanal[] | null>(null);
  const [resultado, setResultado] = useState<DiscordSincronizacao | null>(null);
  const [sincronizando, setSincronizando] = useState(false);
  const [avisos, setAvisos] = useState<DiscordAvisosCanais | null>(null);
  const [salvandoAvisos, setSalvandoAvisos] = useState(false);
  const permitido = temPapel("Admin", "GestorMaster", "SupervisorComercial");

  const carregar = useCallback(() => {
    api.get<DiscordCanal[]>("/crm/discord/grupos").then(setCanais).catch(() => setCanais([]));
    api.get<DiscordAvisosCanais>("/crm/discord/grupos/avisos").then(setAvisos).catch(() => undefined);
  }, []);

  useEffect(() => {
    if (permitido) carregar();
  }, [permitido, carregar]);

  if (!permitido) return null;

  async function alterarAviso(mudanca: Partial<DiscordAvisosCanais>) {
    if (!avisos) return;
    setSalvandoAvisos(true);
    try {
      setAvisos(await api.put<DiscordAvisosCanais>("/crm/discord/grupos/avisos", { ...avisos, ...mudanca }));
    } catch (e) {
      notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível salvar os avisos.");
    } finally {
      setSalvandoAvisos(false);
    }
  }

  async function sincronizar() {
    setSincronizando(true);
    try {
      const r = await api.post<DiscordSincronizacao>("/crm/discord/grupos/sincronizar");
      setResultado(r);
      notificar(r.falhas.length > 0 ? "error" : "success", r.falhas.length > 0 ? "Sincronizado com falhas — veja abaixo." : "Grupos sincronizados.");
      carregar();
    } catch (e) {
      notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível sincronizar os grupos.");
    } finally {
      setSincronizando(false);
    }
  }

  return (
    <Card className="space-y-4 p-5">
      <div>
        <h2 className="flex items-center gap-2 font-semibold text-[var(--fg)]">
          <Users className="size-4 text-[var(--brand)]" aria-hidden /> Grupos no Discord
        </h2>
        <p className="text-xs text-[var(--fg-muted)]">
          Cada regional e cada grupo do CRM vira um canal no servidor, visível só para quem é do grupo. Os cargos de cada pessoa seguem o CRM. O bot precisa das
          permissões "Gerenciar canais", "Gerenciar cargos", "Gerenciar apelidos" e "Gerenciar mensagens" (fixar a boas-vindas), e o cargo dele deve ficar acima dos cargos "CRM · …".
        </p>
      </div>

      {canais && canais.length > 0 && (
        <ul className="flex flex-wrap gap-2">
          {canais.map((c) => (
            <li key={c.chave}>
              <Badge variant={c.ativo ? "success" : "neutral"}>{c.nome}</Badge>
            </li>
          ))}
        </ul>
      )}

      <Button variant="secondary" onClick={sincronizar} loading={sincronizando}>
        <RefreshCw className="size-4" /> {canais && canais.length > 0 ? "Sincronizar grupos" : "Criar grupos no Discord"}
      </Button>

      {avisos && canais && canais.length > 0 && (
        <div className="space-y-2 border-t border-[var(--border)] pt-4">
          <p className="text-sm font-semibold text-[var(--fg)]">Avisos automáticos nos canais das regionais</p>
          <p className="text-xs text-[var(--fg-muted)]">Saem no canal da regional, sem nome nem telefone de cliente: só o consultor, a regional e números.</p>
          <Checkbox
            label="Venda fechada (“Fulano fechou uma venda”)"
            checked={avisos.venda}
            disabled={salvandoAvisos}
            onChange={(e) => alterarAviso({ venda: e.target.checked })}
          />
          <Checkbox
            label="Meta do mês batida (uma vez por regional e mês)"
            checked={avisos.metaBatida}
            disabled={salvandoAvisos}
            onChange={(e) => alterarAviso({ metaBatida: e.target.checked })}
          />
          <Checkbox
            label="Resumo diário de leads parados (depois das 9h30)"
            checked={avisos.leadsParados}
            disabled={salvandoAvisos}
            onChange={(e) => alterarAviso({ leadsParados: e.target.checked })}
          />
        </div>
      )}

      {resultado && (
        <div className="space-y-1 text-sm text-[var(--fg)]">
          <p>
            {resultado.canaisCriados} canal(is), {resultado.canaisDeVozCriados} canal(is) de voz e {resultado.cargosCriados} cargo(s) criados · {resultado.membrosAtualizados} pessoa(s) com cargos ajustados
            {resultado.apelidosDefinidos > 0 && ` · ${resultado.apelidosDefinidos} apelido(s) ajustado(s) para o nome do CRM`}
            {resultado.membrosForaDoServidor > 0 && ` · ${resultado.membrosForaDoServidor} ainda fora do servidor`}
            {resultado.boasVindasPublicadas && " · mensagem de boas-vindas publicada no Geral"}.
          </p>
          {resultado.falhas.length > 0 && (
            <ul className="list-disc pl-5 text-xs text-[var(--danger)]">
              {resultado.falhas.slice(0, 8).map((f) => (
                <li key={f}>{f}</li>
              ))}
            </ul>
          )}
        </div>
      )}
    </Card>
  );
}
