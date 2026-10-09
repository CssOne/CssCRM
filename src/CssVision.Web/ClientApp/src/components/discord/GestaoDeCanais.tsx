import { Archive, ArchiveRestore, Check, Pencil, Plus, Trash2, Unplug, Volume2, X } from "lucide-react";
import { useState } from "react";
import { api, ApiRequestError } from "../../lib/api";
import type { DiscordCanal } from "../../lib/types";
import { Badge, Button, Input, Modal, useToast } from "../ui";

/**
 * Canais do Discord que o CRM gerencia: renomear (no Discord e no CRM) e criar canais extras, visíveis só para quem está no grupo escolhido.
 * O Discord limita a troca de nome de um canal a 2 vezes a cada 10 minutos; se passar disso, o CRM mostra o aviso e nada muda.
 */
export function GestaoDeCanais({ canais, aoMudar }: { canais: DiscordCanal[]; aoMudar: () => void }) {
  const { notificar } = useToast();
  const [editando, setEditando] = useState<{ chave: string; nome: string } | null>(null);
  const [salvando, setSalvando] = useState(false);
  const [criando, setCriando] = useState(false);
  const [apagando, setApagando] = useState<DiscordCanal | null>(null);
  const [confirmacao, setConfirmacao] = useState("");
  const [apagandoEmAndamento, setApagandoEmAndamento] = useState(false);

  async function arquivar(c: DiscordCanal) {
    try {
      await api.put(`/crm/discord/grupos/canais/${encodeURIComponent(c.chave)}/arquivado`, { arquivar: c.ativo });
      aoMudar();
      notificar("success", c.ativo ? "Canal arquivado: some do chat do CRM, mas continua no Discord." : "Canal de volta ao chat.");
    } catch (e) {
      notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível arquivar o canal.");
    }
  }

  async function apagar() {
    if (!apagando || apagandoEmAndamento) return;
    setApagandoEmAndamento(true);
    try {
      await api.del(`/crm/discord/grupos/canais/${encodeURIComponent(apagando.chave)}?confirmarNome=${encodeURIComponent(confirmacao.trim())}`);
      notificar("success", "Canal apagado no Discord e no CRM.");
      setApagando(null);
      setConfirmacao("");
      aoMudar();
    } catch (e) {
      notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível apagar o canal.");
    } finally {
      setApagandoEmAndamento(false);
    }
  }

  async function religar(c: DiscordCanal) {
    try {
      await api.put(`/crm/discord/grupos/canais/${encodeURIComponent(c.chave)}/religar`);
      aoMudar();
      notificar("success", "Canal religado. Clique em “Sincronizar grupos” para recriá-lo no Discord.");
    } catch (e) {
      notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível religar o canal.");
    }
  }

  async function renomear() {
    if (!editando || !editando.nome.trim() || salvando) return;
    setSalvando(true);
    try {
      await api.put(`/crm/discord/grupos/canais/${encodeURIComponent(editando.chave)}`, { nome: editando.nome.trim() });
      setEditando(null);
      aoMudar();
      notificar("success", "Canal renomeado no Discord e no CRM.");
    } catch (e) {
      notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível renomear o canal.");
    } finally {
      setSalvando(false);
    }
  }

  const nomeDoGrupo = (chave?: string | null) => canais.find((c) => c.chave === chave)?.nome ?? "—";

  return (
    <div className="space-y-2">
      <ul className="divide-y divide-[var(--border)] rounded-lg border border-[var(--border)]">
        {canais.map((c) => (
          <li key={c.chave} className="flex flex-wrap items-center gap-2 px-3 py-2 text-sm">
            {editando?.chave === c.chave ? (
              <form
                className="flex flex-1 items-center gap-2"
                onSubmit={(e) => {
                  e.preventDefault();
                  void renomear();
                }}
              >
                <Input
                  value={editando.nome}
                  onChange={(e) => setEditando({ chave: c.chave, nome: e.target.value })}
                  maxLength={60}
                  autoFocus
                  aria-label={`Novo nome do canal ${c.nome}`}
                  onKeyDown={(e) => e.key === "Escape" && setEditando(null)}
                />
                <Button type="submit" size="sm" loading={salvando} disabled={!editando.nome.trim() || editando.nome.trim() === c.nome} aria-label="Salvar nome">
                  <Check className="size-4" />
                </Button>
                <Button type="button" size="sm" variant="ghost" onClick={() => setEditando(null)} disabled={salvando} aria-label="Cancelar">
                  <X className="size-4" />
                </Button>
              </form>
            ) : (
              <>
                <span className="font-medium text-[var(--fg)]">{c.nome}</span>
                {!c.ativo && <Badge variant="neutral">inativo</Badge>}
                {c.voz && <Volume2 className="size-4 text-[var(--fg-muted)]" aria-label="canal de voz" />}
                {c.desligado && <Badge variant="warning">sem canal no Discord</Badge>}
                {c.extra && <Badge variant="info">extra · vê: {nomeDoGrupo(c.acessoChave)}</Badge>}
                <span className="ml-auto flex items-center gap-1">
                  {c.desligado && (
                    <button
                      type="button"
                      onClick={() => void religar(c)}
                      className="flex items-center gap-1 rounded p-1 text-xs text-[var(--brand)] hover:bg-[var(--surface-hover)]"
                      aria-label={`Religar ${c.nome}`}
                      title="Permite recriar o canal na próxima sincronização"
                    >
                      <Unplug className="size-4" /> Religar
                    </button>
                  )}
                  {!c.desligado && (
                  <button
                    type="button"
                    onClick={() => setEditando({ chave: c.chave, nome: c.nome })}
                    className="rounded p-1 text-[var(--fg-muted)] hover:bg-[var(--surface-hover)] hover:text-[var(--fg)]"
                    aria-label={`Renomear ${c.nome}`}
                    title="Renomear"
                  >
                    <Pencil className="size-4" />
                  </button>
                  )}
                  {c.extra && !c.voz && (
                    <button
                      type="button"
                      onClick={() => void arquivar(c)}
                      className="rounded p-1 text-[var(--fg-muted)] hover:bg-[var(--surface-hover)] hover:text-[var(--fg)]"
                      aria-label={c.ativo ? `Arquivar ${c.nome}` : `Desarquivar ${c.nome}`}
                      title={c.ativo ? "Arquivar (some do chat do CRM; continua no Discord)" : "Mostrar de novo no chat"}
                    >
                      {c.ativo ? <Archive className="size-4" /> : <ArchiveRestore className="size-4" />}
                    </button>
                  )}
                  {c.chave !== "conversas" && !c.desligado && (
                    <button
                      type="button"
                      onClick={() => {
                        setApagando(c);
                        setConfirmacao("");
                      }}
                      className="rounded p-1 text-[var(--fg-muted)] hover:bg-[var(--surface-hover)] hover:text-[var(--danger)]"
                      aria-label={`Apagar ${c.nome}`}
                      title="Apagar (irreversível)"
                    >
                      <Trash2 className="size-4" />
                    </button>
                  )}
                </span>
              </>
            )}
          </li>
        ))}
      </ul>
      <Button variant="secondary" size="sm" onClick={() => setCriando(true)}>
        <Plus className="size-4" /> Criar canal
      </Button>
      <Modal
        open={!!apagando}
        onClose={() => setApagando(null)}
        title="Apagar canal"
        size="sm"
        footer={
          <>
            <Button variant="ghost" onClick={() => setApagando(null)} disabled={apagandoEmAndamento}>
              Cancelar
            </Button>
            <Button variant="danger" onClick={() => void apagar()} loading={apagandoEmAndamento} disabled={confirmacao.trim().toLowerCase() !== apagando?.nome.toLowerCase()}>
              Apagar para sempre
            </Button>
          </>
        }
      >
        <div className="space-y-2">
          <p className="text-sm text-[var(--fg)]">
            Isto apaga o canal <strong>{apagando?.nome}</strong> no Discord (texto e voz), com todas as mensagens. <strong>Não dá para desfazer.</strong>
          </p>
          {!apagando?.extra && (
            <p className="text-sm text-[var(--fg-muted)]">
              É um canal de grupo: o grupo e o cargo continuam, e ele só volta se você clicar em Religar e depois em “Sincronizar grupos”, já vazio.
            </p>
          )}
          <label htmlFor="apagar-confirmacao" className="text-sm font-medium text-[var(--fg)]">
            Digite o nome do canal para confirmar
          </label>
          <Input id="apagar-confirmacao" value={confirmacao} onChange={(e) => setConfirmacao(e.target.value)} autoFocus placeholder={apagando?.nome} />
          <p className="text-xs text-[var(--fg-muted)]">Se só quiser tirar o canal do chat do CRM, use Arquivar.</p>
        </div>
      </Modal>
      <NovoCanal aberto={criando} canais={canais} aoFechar={() => setCriando(false)} aoCriado={aoMudar} />
    </div>
  );
}

function NovoCanal({ aberto, canais, aoFechar, aoCriado }: { aberto: boolean; canais: DiscordCanal[]; aoFechar: () => void; aoCriado: () => void }) {
  const { notificar } = useToast();
  const grupos = canais.filter((c) => !c.extra && c.ativo);
  const [nome, setNome] = useState("");
  const [topico, setTopico] = useState("");
  const [acesso, setAcesso] = useState("");
  const [voz, setVoz] = useState(false);
  const [criando, setCriando] = useState(false);
  const acessoEscolhido = acesso || grupos[0]?.chave || "";

  async function criar() {
    if (!nome.trim() || !acessoEscolhido || criando) return;
    setCriando(true);
    try {
      await api.post("/crm/discord/grupos/canais", { nome: nome.trim(), acessoChave: acessoEscolhido, topico: voz ? null : topico.trim() || null, voz });
      setNome("");
      setTopico("");
      setAcesso("");
      setVoz(false);
      aoCriado();
      aoFechar();
      notificar("success", "Canal criado no Discord. Ele já aparece no chat de quem está no grupo.");
    } catch (e) {
      notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível criar o canal.");
    } finally {
      setCriando(false);
    }
  }

  return (
    <Modal
      open={aberto}
      onClose={aoFechar}
      title="Criar canal"
      size="sm"
      footer={
        <>
          <Button variant="ghost" onClick={aoFechar} disabled={criando}>
            Cancelar
          </Button>
          <Button onClick={() => void criar()} loading={criando} disabled={!nome.trim() || !acessoEscolhido}>
            Criar canal
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <div className="flex gap-4 text-sm text-[var(--fg)]" role="radiogroup" aria-label="Tipo de canal">
          <label className="flex cursor-pointer items-center gap-1.5">
            <input type="radio" name="tipo-canal" checked={!voz} onChange={() => setVoz(false)} /> Texto (aparece no chat)
          </label>
          <label className="flex cursor-pointer items-center gap-1.5">
            <input type="radio" name="tipo-canal" checked={voz} onChange={() => setVoz(true)} /> Voz (só no Discord)
          </label>
        </div>
        <div className="space-y-1">
          <label htmlFor="canal-nome" className="text-sm font-medium text-[var(--fg)]">
            Nome do canal
          </label>
          <Input id="canal-nome" value={nome} onChange={(e) => setNome(e.target.value)} maxLength={60} autoFocus placeholder="Ex.: Avisos da gestão" />
        </div>
        {!voz && (
          <div className="space-y-1">
            <label htmlFor="canal-topico" className="text-sm font-medium text-[var(--fg)]">
              Tópico (opcional)
            </label>
            <Input id="canal-topico" value={topico} onChange={(e) => setTopico(e.target.value)} maxLength={1024} placeholder="Para que serve o canal" />
          </div>
        )}
        <div className="space-y-1">
          <label htmlFor="canal-acesso" className="text-sm font-medium text-[var(--fg)]">
            Quem vê o canal
          </label>
          <select
            id="canal-acesso"
            value={acessoEscolhido}
            onChange={(e) => setAcesso(e.target.value)}
            className="w-full rounded-lg border border-[var(--border)] bg-[var(--bg)] px-3 py-2 text-sm text-[var(--fg)]"
          >
            {grupos.map((g) => (
              <option key={g.chave} value={g.chave}>
                {g.nome}
              </option>
            ))}
          </select>
          <p className="text-xs text-[var(--fg-muted)]">Só as pessoas desse grupo veem o canal, no Discord e no chat do CRM.</p>
        </div>
      </div>
    </Modal>
  );
}
