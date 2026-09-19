import { useEffect, useState, type FormEvent } from "react";
import { api } from "../../lib/api";
import type { Grupo, PagedResult, Regional, UserCreateRequest, UserSummary, UserUpdateRequest } from "../../lib/types";
import { Button, Checkbox, FieldError, Input, Label, Select } from "../ui";

const PAPEL_LABEL: Record<string, string> = {
  Admin: "Administrador",
  GestorMaster: "Gestor master",
  GestorComercial: "Gestor comercial",
  Comercial: "Consultor comercial",
  Marketing: "Marketing",
};

export interface UserFormValues {
  nomeCompleto: string;
  email: string;
  senha: string;
  telefone: string;
  papel: string;
  regionalId: string;
  gestorComercialId: string;
  grupoId: string;
  limiteMensalLeads: string;
  ativo: boolean;
}

function valoresVazios(papelFixo?: string): UserFormValues {
  return {
    nomeCompleto: "",
    email: "",
    senha: "",
    telefone: "",
    papel: papelFixo ?? "Comercial",
    regionalId: "",
    gestorComercialId: "",
    grupoId: "",
    limiteMensalLeads: "",
    ativo: true,
  };
}

export function paraFormValues(usuario: UserSummary): UserFormValues {
  return {
    nomeCompleto: usuario.nomeCompleto,
    email: usuario.email,
    senha: "",
    telefone: usuario.telefone ?? "",
    papel: usuario.papeis[0] ?? "Comercial",
    regionalId: usuario.regionalId ?? "",
    gestorComercialId: usuario.gestorComercialId ?? "",
    grupoId: usuario.grupoId ?? "",
    limiteMensalLeads: usuario.limiteMensalLeads != null ? String(usuario.limiteMensalLeads) : "",
    ativo: usuario.ativo,
  };
}

export function paraCriarRequest(v: UserFormValues): UserCreateRequest {
  return {
    nomeCompleto: v.nomeCompleto.trim(),
    email: v.email.trim(),
    senha: v.senha,
    telefone: v.telefone || null,
    papel: v.papel,
    regionalId: v.regionalId || null,
    gestorComercialId: v.gestorComercialId || null,
    grupoId: v.papel === "Comercial" ? v.grupoId || null : null,
    limiteMensalLeads: v.limiteMensalLeads ? Number(v.limiteMensalLeads) : null,
  };
}

export function paraAtualizarRequest(v: UserFormValues): UserUpdateRequest {
  return {
    nomeCompleto: v.nomeCompleto.trim(),
    telefone: v.telefone || null,
    papel: v.papel,
    regionalId: v.regionalId || null,
    gestorComercialId: v.gestorComercialId || null,
    grupoId: v.papel === "Comercial" ? v.grupoId || null : null,
    limiteMensalLeads: v.limiteMensalLeads ? Number(v.limiteMensalLeads) : null,
    ativo: v.ativo,
  };
}

/**
 * Um GestorComercial só cadastra/edita consultores da própria regional — nesse caso o backend
 * ignora regional/gestor enviados e força os seus próprios, então aqui nem exibimos esses campos.
 */
export function UserForm({
  modoEdicao,
  podeGerenciarTudo,
  valoresIniciais,
  salvando,
  onSubmit,
  onCancel,
}: {
  modoEdicao: boolean;
  podeGerenciarTudo: boolean;
  valoresIniciais?: UserFormValues;
  salvando: boolean;
  onSubmit: (valores: UserFormValues) => void;
  onCancel: () => void;
}) {
  const [valores, setValores] = useState<UserFormValues>(valoresIniciais ?? valoresVazios(podeGerenciarTudo ? undefined : "Comercial"));
  const [erros, setErros] = useState<Record<string, string>>({});
  const [regionais, setRegionais] = useState<Regional[]>([]);
  const [gestores, setGestores] = useState<UserSummary[]>([]);
  const [grupos, setGrupos] = useState<Grupo[]>([]);

  useEffect(() => {
    if (!podeGerenciarTudo) return;
    api.get<Regional[]>("/crm/settings/regionals").then((lista) => setRegionais(lista.filter((r) => r.ativa)));
    api
      .get<PagedResult<UserSummary>>("/crm/users?papel=GestorComercial&tamanhoPagina=100")
      .then((res) => setGestores(res.itens));
  }, [podeGerenciarTudo]);

  useEffect(() => {
    if (podeGerenciarTudo) {
      if (!valores.regionalId) {
        setGrupos([]);
        return;
      }
      api.get<Grupo[]>(`/crm/settings/groups?regionalId=${valores.regionalId}`).then((lista) => setGrupos(lista.filter((g) => g.ativo)));
    } else {
      api.get<Grupo[]>("/crm/settings/groups").then((lista) => setGrupos(lista.filter((g) => g.ativo)));
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [podeGerenciarTudo, valores.regionalId]);

  function set<K extends keyof UserFormValues>(campo: K, valor: UserFormValues[K]) {
    setValores((v) => ({ ...v, [campo]: valor }));
  }

  function setRegional(regionalId: string) {
    setValores((v) => ({ ...v, regionalId, grupoId: "" }));
  }

  function validar(): boolean {
    const novosErros: Record<string, string> = {};
    if (!valores.nomeCompleto.trim()) novosErros.nome = "Informe o nome completo.";
    if (!valores.email.trim() || !/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(valores.email)) novosErros.email = "E-mail inválido.";
    if (!modoEdicao && valores.senha.length < 8) novosErros.senha = "A senha deve ter ao menos 8 caracteres.";
    if (podeGerenciarTudo && (valores.papel === "Comercial" || valores.papel === "GestorComercial") && !valores.regionalId) {
      novosErros.regional = "Selecione a regional.";
    }
    setErros(novosErros);
    return Object.keys(novosErros).length === 0;
  }

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (validar()) onSubmit(valores);
  }

  const exibirRegionalEGestor = podeGerenciarTudo && (valores.papel === "Comercial" || valores.papel === "GestorComercial");
  const exibirLimite = valores.papel === "Comercial";

  return (
    <form onSubmit={handleSubmit} className="space-y-4" noValidate>
      <div className="grid gap-4 sm:grid-cols-2">
        <div className="sm:col-span-2">
          <Label htmlFor="user-nome" required>
            Nome completo
          </Label>
          <Input id="user-nome" value={valores.nomeCompleto} onChange={(e) => set("nomeCompleto", e.target.value)} required />
          <FieldError>{erros.nome}</FieldError>
        </div>

        <div>
          <Label htmlFor="user-email" required>
            E-mail
          </Label>
          <Input id="user-email" type="email" value={valores.email} onChange={(e) => set("email", e.target.value)} disabled={modoEdicao} required />
          <FieldError>{erros.email}</FieldError>
        </div>

        <div>
          <Label htmlFor="user-telefone">Telefone</Label>
          <Input id="user-telefone" value={valores.telefone} onChange={(e) => set("telefone", e.target.value)} />
        </div>

        {!modoEdicao && (
          <div className="sm:col-span-2">
            <Label htmlFor="user-senha" required>
              Senha inicial
            </Label>
            <Input id="user-senha" type="password" value={valores.senha} onChange={(e) => set("senha", e.target.value)} required />
            <FieldError>{erros.senha}</FieldError>
          </div>
        )}

        {podeGerenciarTudo ? (
          <div>
            <Label htmlFor="user-papel">Papel</Label>
            <Select id="user-papel" value={valores.papel} onChange={(e) => set("papel", e.target.value)}>
              {Object.entries(PAPEL_LABEL).map(([valor, rotulo]) => (
                <option key={valor} value={valor}>
                  {rotulo}
                </option>
              ))}
            </Select>
          </div>
        ) : (
          <div>
            <Label>Papel</Label>
            <p className="flex h-10 items-center text-sm text-[var(--fg-muted)]">Consultor comercial (da sua regional)</p>
          </div>
        )}

        {exibirRegionalEGestor && (
          <div>
            <Label htmlFor="user-regional" required>
              Regional
            </Label>
            <Select id="user-regional" value={valores.regionalId} onChange={(e) => setRegional(e.target.value)}>
              <option value="">Selecione...</option>
              {regionais.map((r) => (
                <option key={r.id} value={r.id}>
                  {r.nome}
                </option>
              ))}
            </Select>
            <FieldError>{erros.regional}</FieldError>
          </div>
        )}

        {podeGerenciarTudo && valores.papel === "Comercial" && (
          <div>
            <Label htmlFor="user-gestor">Gestor comercial responsável</Label>
            <Select id="user-gestor" value={valores.gestorComercialId} onChange={(e) => set("gestorComercialId", e.target.value)}>
              <option value="">Nenhum</option>
              {gestores.map((g) => (
                <option key={g.id} value={g.id}>
                  {g.nomeCompleto}
                </option>
              ))}
            </Select>
          </div>
        )}

        {valores.papel === "Comercial" && (
          <div>
            <Label htmlFor="user-grupo">Grupo</Label>
            <Select
              id="user-grupo"
              value={valores.grupoId}
              onChange={(e) => set("grupoId", e.target.value)}
              disabled={podeGerenciarTudo && !valores.regionalId}
            >
              <option value="">Sem grupo</option>
              {grupos.map((g) => (
                <option key={g.id} value={g.id}>
                  {g.nome}
                </option>
              ))}
            </Select>
            {podeGerenciarTudo && !valores.regionalId && (
              <p className="mt-1 text-xs text-[var(--fg-muted)]">Selecione a regional para ver os grupos disponíveis.</p>
            )}
          </div>
        )}

        {exibirLimite && (
          <div>
            <Label htmlFor="user-limite">Limite mensal de leads (opcional)</Label>
            <Input id="user-limite" type="number" min={0} value={valores.limiteMensalLeads} onChange={(e) => set("limiteMensalLeads", e.target.value)} />
          </div>
        )}

        {modoEdicao && (
          <div className="sm:col-span-2">
            <Checkbox label="Usuário ativo (desmarque para bloquear o acesso)" checked={valores.ativo} onChange={(e) => set("ativo", e.target.checked)} />
          </div>
        )}
      </div>

      <div className="flex justify-end gap-2 pt-2">
        <Button type="button" variant="secondary" onClick={onCancel} disabled={salvando}>
          Cancelar
        </Button>
        <Button type="submit" loading={salvando}>
          Salvar
        </Button>
      </div>
    </form>
  );
}

export { PAPEL_LABEL };
