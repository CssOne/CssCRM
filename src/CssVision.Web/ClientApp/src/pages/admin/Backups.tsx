import { AlertTriangle, CheckCircle2, Database, Download, Loader2, ShieldCheck, XCircle } from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import { api, ApiRequestError, downloadUrl, isAbortError } from "../../lib/api";
import { formatarDataHora } from "../../lib/format";
import { Badge, Button, Card, ErrorState, Skeleton, useToast } from "../../components/ui";

interface Backup {
  id: string;
  status: "EmAndamento" | "Concluido" | "Falhou" | "Removido";
  origem: "Automatico" | "Manual";
  criadoEm: string;
  concluidoEm?: string | null;
  tamanhoBytes: number;
  nomeArquivo?: string | null;
  leads: number;
  oportunidades: number;
  usuarios: number;
  erro?: string | null;
}

function tamanho(bytes: number) {
  if (bytes >= 1024 * 1024 * 1024) return `${(bytes / 1024 / 1024 / 1024).toFixed(1).replace(".", ",")} GB`;
  if (bytes >= 1024 * 1024) return `${(bytes / 1024 / 1024).toFixed(1).replace(".", ",")} MB`;
  return `${Math.max(1, Math.round(bytes / 1024))} KB`;
}

function horasDesde(data: string) {
  return (Date.now() - new Date(data).getTime()) / 36e5;
}

/**
 * Backups do banco (só administradores): um automático por dia de madrugada, "Gerar backup agora"
 * e download de cada cópia — para guardar fora da AWS (ver BackupService no back-end).
 */
export function BackupsPage() {
  const { notificar } = useToast();
  const [backups, setBackups] = useState<Backup[] | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [gerando, setGerando] = useState(false);
  const [recarregar, setRecarregar] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    api
      .get<Backup[]>("/admin/backups", controller.signal)
      .then((lista) => {
        setBackups(lista);
        setErro(null);
      })
      .catch((e) => {
        if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar os backups.");
      });
    return () => controller.abort();
  }, [recarregar]);

  const gerarAgora = useCallback(async () => {
    setGerando(true);
    try {
      await api.post("/admin/backups", {});
      notificar("success", "Backup gerado com sucesso.");
    } catch (e) {
      notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível gerar o backup.");
    } finally {
      setGerando(false);
      setRecarregar((n) => n + 1);
    }
  }, [notificar]);

  if (erro) return <ErrorState message={erro} onRetry={() => setRecarregar((n) => n + 1)} />;

  const ultimo = backups?.find((b) => b.status === "Concluido");
  const atrasado = !ultimo || horasDesde(ultimo.criadoEm) > 26;

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="flex items-center gap-2 text-xl font-semibold text-[var(--fg)]">
            <Database className="size-5" /> Backups do banco de dados
          </h1>
          <p className="text-sm text-[var(--fg-muted)]">
            Cópia completa do CRM (leads, vendas, usuários, histórico). Um backup automático é feito todo dia às 03:00; os 30 mais recentes ficam
            guardados.
          </p>
        </div>
        <Button onClick={gerarAgora} loading={gerando}>
          <Database className="size-4" /> Gerar backup agora
        </Button>
      </div>

      {backups && (
        <Card className={`flex items-start gap-3 p-4 ${atrasado ? "border-[var(--warning)]" : ""}`}>
          {atrasado ? (
            <AlertTriangle className="mt-0.5 size-5 shrink-0 text-[var(--warning)]" />
          ) : (
            <ShieldCheck className="mt-0.5 size-5 shrink-0 text-[var(--success)]" />
          )}
          <div className="text-sm">
            <p className="font-semibold text-[var(--fg)]">
              {ultimo ? `Último backup: ${formatarDataHora(ultimo.criadoEm)}` : "Nenhum backup concluído ainda"}
              {ultimo && ` · ${ultimo.leads.toLocaleString("pt-BR")} leads · ${ultimo.oportunidades.toLocaleString("pt-BR")} vendas`}
            </p>
            <p className="text-[var(--fg-muted)]">
              Os backups ficam guardados na AWS, fora do banco. Para não depender só dela, <strong>baixe um backup de vez em quando</strong> (por
              exemplo, uma vez por semana) e guarde em outro lugar, como o Google Drive ou um HD externo.
            </p>
          </div>
        </Card>
      )}

      <Card className="overflow-x-auto p-4">
        {!backups ? (
          <div className="space-y-2">
            {Array.from({ length: 4 }).map((_, i) => (
              <Skeleton key={i} className="h-10" />
            ))}
          </div>
        ) : backups.length === 0 ? (
          <p className="py-6 text-center text-sm text-[var(--fg-muted)]">Nenhum backup ainda. Clique em "Gerar backup agora".</p>
        ) : (
          <table className="w-full min-w-[760px] text-sm">
            <thead>
              <tr className="border-b border-[var(--border)] text-left text-xs text-[var(--fg-muted)]">
                <th className="pb-2 font-medium">Data</th>
                <th className="pb-2 font-medium">Tipo</th>
                <th className="pb-2 font-medium">Situação</th>
                <th className="pb-2 text-right font-medium">Leads</th>
                <th className="pb-2 text-right font-medium">Vendas</th>
                <th className="pb-2 text-right font-medium">Tamanho</th>
                <th className="pb-2" />
              </tr>
            </thead>
            <tbody>
              {backups.map((b) => (
                <tr key={b.id} className="border-b border-[var(--border)] last:border-0">
                  <td className="py-2.5 text-[var(--fg)]">{formatarDataHora(b.criadoEm)}</td>
                  <td className="py-2.5 text-[var(--fg-muted)]">{b.origem === "Automatico" ? "Automático" : "Manual"}</td>
                  <td className="py-2.5">
                    {b.status === "Concluido" && (
                      <Badge variant="success">
                        <CheckCircle2 className="mr-1 inline size-3" /> Concluído
                      </Badge>
                    )}
                    {b.status === "EmAndamento" && (
                      <Badge variant="info">
                        <Loader2 className="mr-1 inline size-3 animate-spin" /> Gerando
                      </Badge>
                    )}
                    {b.status === "Falhou" && (
                      <span title={b.erro ?? undefined}>
                        <Badge variant="danger">
                          <XCircle className="mr-1 inline size-3" /> Falhou
                        </Badge>
                      </span>
                    )}
                  </td>
                  <td className="py-2.5 text-right text-[var(--fg-muted)]">{b.status === "Falhou" ? "—" : b.leads.toLocaleString("pt-BR")}</td>
                  <td className="py-2.5 text-right text-[var(--fg-muted)]">{b.status === "Falhou" ? "—" : b.oportunidades.toLocaleString("pt-BR")}</td>
                  <td className="py-2.5 text-right text-[var(--fg-muted)]">{b.status === "Concluido" ? tamanho(b.tamanhoBytes) : "—"}</td>
                  <td className="py-2.5 text-right">
                    {b.status === "Concluido" && (
                      <a
                        href={downloadUrl(`/admin/backups/${b.id}/download`)}
                        className="inline-flex items-center gap-1 rounded-md px-2 py-1 text-xs font-medium text-[var(--brand)] hover:bg-[var(--surface-hover)]"
                      >
                        <Download className="size-3.5" /> Baixar
                      </a>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Card>
    </div>
  );
}
