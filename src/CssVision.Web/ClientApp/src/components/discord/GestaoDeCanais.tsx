import { Check, Pencil, Plus, X } from "lucide-react";
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
                {c.extra && <Badge variant="info">extra · vê: {nomeDoGrupo(c.acessoChave)}</Badge>}
                <button
                  type="button"
                  onClick={() => setEditando({ chave: c.chave, nome: c.nome })}
                  className="ml-auto rounded p-1 text-[var(--fg-muted)] hover:bg-[var(--surface-hover)] hover:text-[var(--fg)]"
                  aria-label={`Renomear ${c.nome}`}
                  title="Renomear"
                >
                  <Pencil className="size-4" />
                </button>
              </>
            )}
          </li>
        ))}
      </ul>
      <Button variant="secondary" size="sm" onClick={() => setCriando(true)}>
        <Plus className="size-4" /> Criar canal
      </Button>
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
  const [criando, setCriando] = useState(false);
  const acessoEscolhido = acesso || grupos[0]?.chave || "";

  async function criar() {
    if (!nome.trim() || !acessoEscolhido || criando) return;
    setCriando(true);
    try {
      await api.post("/crm/discord/grupos/canais", { nome: nome.trim(), acessoChave: acessoEscolhido, topico: topico.trim() || null });
      setNome("");
      setTopico("");
      setAcesso("");
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
        <div className="space-y-1">
          <label htmlFor="canal-nome" className="text-sm font-medium text-[var(--fg)]">
            Nome do canal
          </label>
          <Input id="canal-nome" value={nome} onChange={(e) => setNome(e.target.value)} maxLength={60} autoFocus placeholder="Ex.: Avisos da gestão" />
        </div>
        <div className="space-y-1">
          <label htmlFor="canal-topico" className="text-sm font-medium text-[var(--fg)]">
            Tópico (opcional)
          </label>
          <Input id="canal-topico" value={topico} onChange={(e) => setTopico(e.target.value)} maxLength={1024} placeholder="Para que serve o canal" />
        </div>
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
