import { useEffect, useRef, useState, type ChangeEvent, type FormEvent } from "react";
import { useNavigate } from "react-router-dom";
import { AlertTriangle, Eye, EyeOff, ImagePlus, KeyRound, Moon, ShieldCheck, ShieldOff, Sun, Trash2 } from "lucide-react";
import * as QRCode from "qrcode";
import { api, ApiRequestError, isAbortError, uploadFile } from "../../lib/api";
import type { Profile, TwoFactorEnableResult, TwoFactorSetup } from "../../lib/types";
import { useAuth } from "../../context/AuthContext";
import { useTheme } from "../../context/ThemeContext";
import { Avatar, Badge, Button, Card, ErrorState, Input, Label, Modal, Skeleton, useToast } from "../../components/ui";

export function ConfiguracoesPage() {
  const { refetchSessao, logout } = useAuth();
  const navigate = useNavigate();

  const [perfil, setPerfil] = useState<Profile | null>(null);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState<string | null>(null);
  const [recarregar, setRecarregar] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    setCarregando(true);
    setErro(null);
    api
      .get<Profile>("/account/profile", controller.signal)
      .then(setPerfil)
      .catch((e) => { if (!isAbortError(e)) setErro(e instanceof Error ? e.message : "Não foi possível carregar suas configurações."); })
      .finally(() => { if (!controller.signal.aborted) setCarregando(false); });
    return () => controller.abort();
  }, [recarregar]);

  if (carregando) {
    return (
      <div className="max-w-2xl space-y-4">
        <Skeleton className="h-8 w-48" />
        <Skeleton className="h-64" />
        <Skeleton className="h-64" />
      </div>
    );
  }

  if (erro || !perfil) {
    return <ErrorState message={erro ?? "Não foi possível carregar suas configurações."} onRetry={() => setRecarregar((n) => n + 1)} />;
  }

  return (
    <div className="max-w-2xl space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-[var(--fg)]">Configurações</h1>
        <p className="text-sm text-[var(--fg-muted)]">Seus dados, segurança e preferências de conta.</p>
      </div>

      <DadosPessoais
        perfil={perfil}
        onSalvo={async (novo) => {
          setPerfil(novo);
          await refetchSessao();
        }}
      />
      <Seguranca perfil={perfil} onDoisFatoresAlterado={(ativo) => setPerfil((p) => (p ? { ...p, doisFatoresAtivo: ativo } : p))} />
      <Aparencia />
      {perfil.podeExcluirPropriaConta && (
        <ZonaDeRisco
          onExcluido={async () => {
            await logout();
            navigate("/login", { replace: true });
          }}
        />
      )}
    </div>
  );
}

function DadosPessoais({ perfil, onSalvo }: { perfil: Profile; onSalvo: (perfil: Profile) => void }) {
  const { notificar } = useToast();
  const [nomeCompleto, setNomeCompleto] = useState(perfil.nomeCompleto);
  const [email, setEmail] = useState(perfil.email);
  const [telefone, setTelefone] = useState(perfil.telefone ?? "");
  const [salvando, setSalvando] = useState(false);

  async function salvar(e: FormEvent) {
    e.preventDefault();
    setSalvando(true);
    try {
      const atualizado = await api.put<Profile>("/account/profile", {
        nomeCompleto: nomeCompleto.trim(),
        email: email.trim(),
        telefone: telefone.trim() || null,
      });
      onSalvo(atualizado);
      notificar("success", "Dados atualizados com sucesso.");
    } catch (e) {
      notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível salvar seus dados.");
    } finally {
      setSalvando(false);
    }
  }

  return (
    <Card className="p-5">
      <h2 className="mb-4 text-sm font-semibold text-[var(--fg)]">Meus dados</h2>
      <FotoPerfil perfil={perfil} onAlterada={onSalvo} />
      <form onSubmit={salvar} className="space-y-4">
        <div>
          <Label htmlFor="perfil-nome">Nome completo</Label>
          <Input id="perfil-nome" required value={nomeCompleto} onChange={(e) => setNomeCompleto(e.target.value)} />
        </div>
        <div>
          <Label htmlFor="perfil-email">E-mail</Label>
          <Input id="perfil-email" type="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
        </div>
        <div>
          <Label htmlFor="perfil-telefone">Número de telefone</Label>
          <Input id="perfil-telefone" placeholder="(00) 00000-0000" value={telefone} onChange={(e) => setTelefone(e.target.value)} />
        </div>
        <div className="flex justify-end">
          <Button type="submit" loading={salvando}>
            Salvar dados
          </Button>
        </div>
      </form>
    </Card>
  );
}

function FotoPerfil({ perfil, onAlterada }: { perfil: Profile; onAlterada: (perfil: Profile) => void }) {
  const { notificar } = useToast();
  const [enviando, setEnviando] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  async function selecionarArquivo(e: ChangeEvent<HTMLInputElement>) {
    const arquivo = e.target.files?.[0];
    e.target.value = "";
    if (!arquivo) return;

    setEnviando(true);
    try {
      const atualizado = await uploadFile<Profile>("/account/photo", arquivo);
      onAlterada(atualizado);
      notificar("success", "Foto atualizada com sucesso.");
    } catch (err) {
      notificar("error", err instanceof ApiRequestError ? err.message : "Não foi possível enviar a foto.");
    } finally {
      setEnviando(false);
    }
  }

  async function removerFoto() {
    setEnviando(true);
    try {
      const atualizado = await api.del<Profile>("/account/photo");
      onAlterada(atualizado);
      notificar("success", "Foto removida.");
    } catch (err) {
      notificar("error", err instanceof ApiRequestError ? err.message : "Não foi possível remover a foto.");
    } finally {
      setEnviando(false);
    }
  }

  return (
    <div className="mb-5 flex items-center gap-4">
      <Avatar nome={perfil.nomeCompleto} fotoUrl={perfil.fotoUrl} className="size-16 text-xl" />
      <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
        <input ref={inputRef} type="file" accept="image/png,image/jpeg,image/webp" className="hidden" onChange={selecionarArquivo} />
        <Button type="button" variant="secondary" size="sm" loading={enviando} onClick={() => inputRef.current?.click()}>
          <ImagePlus className="size-4" /> Alterar foto
        </Button>
        {perfil.fotoUrl && (
          <Button type="button" variant="ghost" size="sm" disabled={enviando} onClick={removerFoto}>
            Remover foto
          </Button>
        )}
      </div>
    </div>
  );
}

function Seguranca({ perfil, onDoisFatoresAlterado }: { perfil: Profile; onDoisFatoresAlterado: (ativo: boolean) => void }) {
  const { notificar } = useToast();

  const [senhaAtual, setSenhaAtual] = useState("");
  const [novaSenha, setNovaSenha] = useState("");
  const [confirmarSenha, setConfirmarSenha] = useState("");
  const [mostrarSenhas, setMostrarSenhas] = useState(false);
  const [alterandoSenha, setAlterandoSenha] = useState(false);

  const [modal2fa, setModal2fa] = useState<"ativar" | "desativar" | null>(null);

  async function alterarSenha(e: FormEvent) {
    e.preventDefault();
    if (novaSenha !== confirmarSenha) {
      notificar("error", "A confirmação não confere com a nova senha.");
      return;
    }
    setAlterandoSenha(true);
    try {
      await api.post("/account/change-password", { senhaAtual, novaSenha });
      notificar("success", "Senha alterada com sucesso.");
      setSenhaAtual("");
      setNovaSenha("");
      setConfirmarSenha("");
    } catch (e) {
      notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível alterar a senha.");
    } finally {
      setAlterandoSenha(false);
    }
  }

  return (
    <Card className="p-5">
      <h2 className="mb-4 text-sm font-semibold text-[var(--fg)]">Segurança</h2>

      <form onSubmit={alterarSenha} className="space-y-4">
        <p className="text-sm font-medium text-[var(--fg)]">Alterar senha</p>
        <div>
          <Label htmlFor="senha-atual">Senha atual</Label>
          <div className="relative">
            <Input
              id="senha-atual"
              type={mostrarSenhas ? "text" : "password"}
              required
              autoComplete="current-password"
              value={senhaAtual}
              onChange={(e) => setSenhaAtual(e.target.value)}
              className="pr-10"
            />
            <button
              type="button"
              onClick={() => setMostrarSenhas((v) => !v)}
              aria-label={mostrarSenhas ? "Ocultar senhas" : "Mostrar senhas"}
              className="focus-ring absolute right-3 top-1/2 -translate-y-1/2 text-[var(--fg-muted)] hover:text-[var(--fg)] cursor-pointer"
            >
              {mostrarSenhas ? <EyeOff className="size-4" /> : <Eye className="size-4" />}
            </button>
          </div>
        </div>
        <div className="grid gap-4 sm:grid-cols-2">
          <div>
            <Label htmlFor="nova-senha">Nova senha</Label>
            <Input
              id="nova-senha"
              type={mostrarSenhas ? "text" : "password"}
              required
              minLength={8}
              autoComplete="new-password"
              value={novaSenha}
              onChange={(e) => setNovaSenha(e.target.value)}
            />
          </div>
          <div>
            <Label htmlFor="confirmar-senha">Confirmar nova senha</Label>
            <Input
              id="confirmar-senha"
              type={mostrarSenhas ? "text" : "password"}
              required
              minLength={8}
              autoComplete="new-password"
              value={confirmarSenha}
              onChange={(e) => setConfirmarSenha(e.target.value)}
            />
          </div>
        </div>
        <div className="flex justify-end">
          <Button type="submit" loading={alterandoSenha}>
            Alterar senha
          </Button>
        </div>
      </form>

      <div className="my-5 border-t border-[var(--border)]" />

      <div className="flex items-center justify-between gap-3">
        <div>
          <p className="text-sm font-medium text-[var(--fg)]">Verificação em duas etapas</p>
          <p className="text-sm text-[var(--fg-muted)]">Exige um código do seu celular, além da senha, para entrar na conta.</p>
        </div>
        <Badge variant={perfil.doisFatoresAtivo ? "success" : "neutral"}>{perfil.doisFatoresAtivo ? "Ativada" : "Desativada"}</Badge>
      </div>
      <div className="mt-3">
        {perfil.doisFatoresAtivo ? (
          <Button variant="secondary" onClick={() => setModal2fa("desativar")}>
            <ShieldOff className="size-4" /> Desativar verificação em duas etapas
          </Button>
        ) : (
          <Button variant="secondary" onClick={() => setModal2fa("ativar")}>
            <ShieldCheck className="size-4" /> Ativar verificação em duas etapas
          </Button>
        )}
      </div>

      <ModalAtivarDoisFatores
        open={modal2fa === "ativar"}
        onClose={() => setModal2fa(null)}
        onAtivado={() => {
          onDoisFatoresAlterado(true);
          setModal2fa(null);
        }}
      />
      <ModalDesativarDoisFatores
        open={modal2fa === "desativar"}
        onClose={() => setModal2fa(null)}
        onDesativado={() => {
          onDoisFatoresAlterado(false);
          setModal2fa(null);
        }}
      />
    </Card>
  );
}

function ModalAtivarDoisFatores({ open, onClose, onAtivado }: { open: boolean; onClose: () => void; onAtivado: () => void }) {
  const { notificar } = useToast();
  const [chaveManual, setChaveManual] = useState("");
  const [qrCodeDataUrl, setQrCodeDataUrl] = useState<string | null>(null);
  const [codigo, setCodigo] = useState("");
  const [ativando, setAtivando] = useState(false);
  const [codigosRecuperacao, setCodigosRecuperacao] = useState<string[] | null>(null);

  useEffect(() => {
    if (!open) {
      setChaveManual("");
      setQrCodeDataUrl(null);
      setCodigo("");
      setCodigosRecuperacao(null);
      return;
    }
    const controller = new AbortController();
    api
      .get<TwoFactorSetup>("/account/2fa/setup", controller.signal)
      .then(async (dados) => {
        setChaveManual(dados.chaveManual);
        setQrCodeDataUrl(await QRCode.toDataURL(dados.uriQrCode));
      })
      .catch((e) => { if (!isAbortError(e)) notificar("error", "Não foi possível gerar o código de configuração."); });
    return () => controller.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  async function confirmar(e: FormEvent) {
    e.preventDefault();
    setAtivando(true);
    try {
      const resultado = await api.post<TwoFactorEnableResult>("/account/2fa/enable", { codigo: codigo.trim() });
      setCodigosRecuperacao(resultado.codigosRecuperacao);
      notificar("success", "Verificação em duas etapas ativada.");
    } catch (e) {
      notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível ativar a verificação em duas etapas.");
    } finally {
      setAtivando(false);
    }
  }

  if (codigosRecuperacao) {
    return (
      <Modal open={open} onClose={onAtivado} title="Guarde seus códigos de recuperação" size="sm">
        <p className="mb-3 text-sm text-[var(--fg-muted)]">
          Use um destes códigos para entrar caso perca o acesso ao seu aplicativo autenticador. Cada um só funciona uma vez — guarde-os
          em um lugar seguro, eles não serão exibidos de novo.
        </p>
        <div className="mb-4 grid grid-cols-2 gap-2 rounded-lg bg-[var(--surface-hover)] p-3 font-mono text-sm text-[var(--fg)]">
          {codigosRecuperacao.map((c) => (
            <span key={c}>{c}</span>
          ))}
        </div>
        <Button className="w-full" onClick={onAtivado}>
          Já guardei meus códigos
        </Button>
      </Modal>
    );
  }

  return (
    <Modal open={open} onClose={onClose} title="Ativar verificação em duas etapas" size="sm">
      <form onSubmit={confirmar} className="space-y-4">
        <p className="text-sm text-[var(--fg-muted)]">
          Escaneie o QR code com um aplicativo autenticador (Google Authenticator, Authy, etc.) ou digite a chave manualmente.
        </p>
        <div className="flex justify-center">
          {qrCodeDataUrl ? (
            <img src={qrCodeDataUrl} alt="QR code para configurar a verificação em duas etapas" className="size-44 rounded-lg border border-[var(--border)]" />
          ) : (
            <Skeleton className="size-44" />
          )}
        </div>
        {chaveManual && (
          <p className="break-all rounded-lg bg-[var(--surface-hover)] p-2 text-center font-mono text-xs text-[var(--fg-muted)]">
            {chaveManual}
          </p>
        )}
        <div>
          <Label htmlFor="codigo-ativacao">Código de 6 dígitos do aplicativo</Label>
          <Input
            id="codigo-ativacao"
            required
            inputMode="numeric"
            autoComplete="one-time-code"
            value={codigo}
            onChange={(e) => setCodigo(e.target.value)}
            placeholder="000000"
          />
        </div>
        <Button type="submit" className="w-full" loading={ativando}>
          Confirmar e ativar
        </Button>
      </form>
    </Modal>
  );
}

function ModalDesativarDoisFatores({ open, onClose, onDesativado }: { open: boolean; onClose: () => void; onDesativado: () => void }) {
  const { notificar } = useToast();
  const [senha, setSenha] = useState("");
  const [enviando, setEnviando] = useState(false);

  useEffect(() => {
    if (!open) setSenha("");
  }, [open]);

  async function confirmar(e: FormEvent) {
    e.preventDefault();
    setEnviando(true);
    try {
      await api.post("/account/2fa/disable", { senha });
      notificar("success", "Verificação em duas etapas desativada.");
      onDesativado();
    } catch (e) {
      notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível desativar a verificação em duas etapas.");
    } finally {
      setEnviando(false);
    }
  }

  return (
    <Modal open={open} onClose={onClose} title="Desativar verificação em duas etapas" size="sm">
      <form onSubmit={confirmar} className="space-y-4">
        <p className="text-sm text-[var(--fg-muted)]">Confirme sua senha para desativar a verificação em duas etapas.</p>
        <div>
          <Label htmlFor="senha-desativar-2fa">Senha</Label>
          <Input
            id="senha-desativar-2fa"
            type="password"
            required
            autoComplete="current-password"
            value={senha}
            onChange={(e) => setSenha(e.target.value)}
          />
        </div>
        <Button type="submit" variant="danger" className="w-full" loading={enviando}>
          Desativar
        </Button>
      </form>
    </Modal>
  );
}

function Aparencia() {
  const { tema, definir } = useTheme();

  return (
    <Card className="p-5">
      <h2 className="mb-4 text-sm font-semibold text-[var(--fg)]">Aparência</h2>
      <div className="flex gap-3">
        <button
          type="button"
          onClick={() => definir("light")}
          className={`focus-ring flex flex-1 items-center gap-2 rounded-lg border px-4 py-3 text-sm font-medium cursor-pointer ${
            tema === "light" ? "border-[var(--brand)] bg-[var(--brand-soft)] text-[var(--brand)]" : "border-[var(--border)] text-[var(--fg)] hover:bg-[var(--surface-hover)]"
          }`}
        >
          <Sun className="size-4" /> Claro
        </button>
        <button
          type="button"
          onClick={() => definir("dark")}
          className={`focus-ring flex flex-1 items-center gap-2 rounded-lg border px-4 py-3 text-sm font-medium cursor-pointer ${
            tema === "dark" ? "border-[var(--brand)] bg-[var(--brand-soft)] text-[var(--brand)]" : "border-[var(--border)] text-[var(--fg)] hover:bg-[var(--surface-hover)]"
          }`}
        >
          <Moon className="size-4" /> Escuro
        </button>
      </div>
    </Card>
  );
}

function ZonaDeRisco({ onExcluido }: { onExcluido: () => void }) {
  const { notificar } = useToast();
  const [modalAberto, setModalAberto] = useState(false);
  const [senha, setSenha] = useState("");
  const [excluindo, setExcluindo] = useState(false);

  async function excluir(e: FormEvent) {
    e.preventDefault();
    setExcluindo(true);
    try {
      await api.post("/account/delete-account", { senha });
      notificar("success", "Sua conta foi excluída.");
      onExcluido();
    } catch (e) {
      notificar("error", e instanceof ApiRequestError ? e.message : "Não foi possível excluir sua conta.");
    } finally {
      setExcluindo(false);
    }
  }

  return (
    <Card className="border-[var(--danger)]/30 p-5">
      <h2 className="mb-1 text-sm font-semibold text-[var(--danger)]">Zona de risco</h2>
      <p className="mb-4 text-sm text-[var(--fg-muted)]">
        Excluir sua conta é uma ação permanente e não pode ser desfeita. Só é possível se não houver leads, oportunidades ou
        consultores vinculados a você.
      </p>
      <Button variant="danger" onClick={() => setModalAberto(true)}>
        <Trash2 className="size-4" /> Excluir minha conta
      </Button>

      <Modal open={modalAberto} onClose={() => setModalAberto(false)} title="Excluir minha conta" size="sm">
        <form onSubmit={excluir} className="space-y-4">
          <div className="flex items-start gap-2 rounded-lg bg-[var(--danger-soft)] p-3 text-sm text-[var(--danger)]">
            <AlertTriangle className="mt-0.5 size-4 shrink-0" />
            <p>Esta ação é irreversível. Sua conta e o acesso ao sistema serão excluídos permanentemente.</p>
          </div>
          <div>
            <Label htmlFor="senha-excluir-conta">Confirme sua senha</Label>
            <Input
              id="senha-excluir-conta"
              type="password"
              required
              autoComplete="current-password"
              value={senha}
              onChange={(e) => setSenha(e.target.value)}
            />
          </div>
          <Button type="submit" variant="danger" className="w-full" loading={excluindo}>
            <KeyRound className="size-4" /> Excluir permanentemente
          </Button>
        </form>
      </Modal>
    </Card>
  );
}
