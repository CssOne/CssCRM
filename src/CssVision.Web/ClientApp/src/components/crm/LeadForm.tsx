import { useState, type FormEvent } from "react";
import { TipoPessoa, type LeadListItem } from "../../lib/types";
import { Button, Checkbox, FieldError, Input, Label, Select, Textarea } from "../ui";
import { LeadPicker } from "./LeadPicker";

export interface LeadFormValues {
  nomeOuRazaoSocial: string;
  tipoPessoa: TipoPessoa;
  documento: string;
  telefone: string;
  whatsApp: string;
  email: string;
  cidade: string;
  estado: string;
  regional: string;
  origem: string;
  campanha: string;
  produtoInteresse: string;
  gclid: string;
  utmMedium: string;
  utmSource: string;
  utmTerm: string;
  metaClickId: string;
  metaFormId: string;
  metaLeadId: string;
  indicadoPorLeadId: string;
  indicadoPorLeadNome: string;
  tipoIndicacao: string;
  tags: string;
  observacoes: string;
  consentimentoContato: boolean;
}

export const leadFormVazio: LeadFormValues = {
  nomeOuRazaoSocial: "",
  tipoPessoa: TipoPessoa.Fisica,
  documento: "",
  telefone: "",
  whatsApp: "",
  email: "",
  cidade: "",
  estado: "",
  regional: "",
  origem: "",
  campanha: "",
  produtoInteresse: "",
  gclid: "",
  utmMedium: "",
  utmSource: "",
  utmTerm: "",
  metaClickId: "",
  metaFormId: "",
  metaLeadId: "",
  indicadoPorLeadId: "",
  indicadoPorLeadNome: "",
  tipoIndicacao: "",
  tags: "",
  observacoes: "",
  consentimentoContato: false,
};

export function LeadForm({
  valoresIniciais,
  salvando,
  onSubmit,
  onCancel,
  idPrefix = "lead",
}: {
  valoresIniciais?: LeadFormValues;
  salvando: boolean;
  onSubmit: (valores: LeadFormValues) => void;
  onCancel: () => void;
  idPrefix?: string;
}) {
  const [valores, setValores] = useState<LeadFormValues>(valoresIniciais ?? leadFormVazio);
  const [erros, setErros] = useState<Record<string, string>>({});

  function set<K extends keyof LeadFormValues>(campo: K, valor: LeadFormValues[K]) {
    setValores((v) => ({ ...v, [campo]: valor }));
  }

  function validar(): boolean {
    const novosErros: Record<string, string> = {};
    if (!valores.nomeOuRazaoSocial.trim()) novosErros.nome = "Informe o nome ou razão social.";
    if (valores.email && !/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(valores.email)) novosErros.email = "E-mail inválido.";
    if (valores.estado && valores.estado.length !== 2) novosErros.estado = "Use a sigla do estado (ex: SP).";
    setErros(novosErros);
    return Object.keys(novosErros).length === 0;
  }

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (validar()) onSubmit(valores);
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-4" noValidate>
      <div className="grid gap-4 sm:grid-cols-2">
        <div className="sm:col-span-2">
          <Label htmlFor={`${idPrefix}-nome`} required>
            Nome ou razão social
          </Label>
          <Input
            id={`${idPrefix}-nome`}
            value={valores.nomeOuRazaoSocial}
            onChange={(e) => set("nomeOuRazaoSocial", e.target.value)}
            required
          />
          <FieldError>{erros.nome}</FieldError>
        </div>

        <div>
          <Label htmlFor={`${idPrefix}-tipo`}>Tipo de pessoa</Label>
          <Select
            id={`${idPrefix}-tipo`}
            value={valores.tipoPessoa}
            onChange={(e) => set("tipoPessoa", Number(e.target.value) as TipoPessoa)}
          >
            <option value={TipoPessoa.Fisica}>Física</option>
            <option value={TipoPessoa.Juridica}>Jurídica</option>
          </Select>
        </div>

        <div>
          <Label htmlFor={`${idPrefix}-doc`}>{valores.tipoPessoa === TipoPessoa.Fisica ? "CPF" : "CNPJ"}</Label>
          <Input id={`${idPrefix}-doc`} value={valores.documento} onChange={(e) => set("documento", e.target.value)} />
        </div>

        <div>
          <Label htmlFor={`${idPrefix}-tel`}>Telefone</Label>
          <Input id={`${idPrefix}-tel`} value={valores.telefone} onChange={(e) => set("telefone", e.target.value)} />
        </div>

        <div>
          <Label htmlFor={`${idPrefix}-whats`}>WhatsApp</Label>
          <Input id={`${idPrefix}-whats`} value={valores.whatsApp} onChange={(e) => set("whatsApp", e.target.value)} />
        </div>

        <div className="sm:col-span-2">
          <Label htmlFor={`${idPrefix}-email`}>E-mail</Label>
          <Input id={`${idPrefix}-email`} type="email" value={valores.email} onChange={(e) => set("email", e.target.value)} />
          <FieldError>{erros.email}</FieldError>
        </div>

        <div>
          <Label htmlFor={`${idPrefix}-cidade`}>Cidade</Label>
          <Input id={`${idPrefix}-cidade`} value={valores.cidade} onChange={(e) => set("cidade", e.target.value)} />
        </div>

        <div>
          <Label htmlFor={`${idPrefix}-estado`}>Estado (UF)</Label>
          <Input id={`${idPrefix}-estado`} maxLength={2} value={valores.estado} onChange={(e) => set("estado", e.target.value.toUpperCase())} />
          <FieldError>{erros.estado}</FieldError>
        </div>

        <div>
          <Label htmlFor={`${idPrefix}-regional`}>Regional</Label>
          <Input id={`${idPrefix}-regional`} value={valores.regional} onChange={(e) => set("regional", e.target.value)} />
        </div>

        <div>
          <Label htmlFor={`${idPrefix}-origem`}>Origem</Label>
          <Input id={`${idPrefix}-origem`} value={valores.origem} onChange={(e) => set("origem", e.target.value)} placeholder="Site, indicação..." />
        </div>

        <div>
          <Label htmlFor={`${idPrefix}-campanha`}>Campanha</Label>
          <Input id={`${idPrefix}-campanha`} value={valores.campanha} onChange={(e) => set("campanha", e.target.value)} />
        </div>

        <div>
          <Label htmlFor={`${idPrefix}-produto`}>Produto/serviço de interesse</Label>
          <Input id={`${idPrefix}-produto`} value={valores.produtoInteresse} onChange={(e) => set("produtoInteresse", e.target.value)} />
        </div>

        <div className="sm:col-span-2">
          <Label htmlFor={`${idPrefix}-tags`}>Tags (separadas por vírgula)</Label>
          <Input id={`${idPrefix}-tags`} value={valores.tags} onChange={(e) => set("tags", e.target.value)} />
        </div>

        <div className="sm:col-span-2">
          <Label htmlFor={`${idPrefix}-obs`}>Observações</Label>
          <Textarea id={`${idPrefix}-obs`} value={valores.observacoes} onChange={(e) => set("observacoes", e.target.value)} />
        </div>
      </div>

      <div className="border-t border-[var(--border)] pt-4">
        <h3 className="mb-3 text-sm font-semibold text-[var(--fg)]">Indicação</h3>
        <div className="grid gap-4 sm:grid-cols-2">
          <div className="sm:col-span-2">
            <Label>Indicado por</Label>
            {!valores.indicadoPorLeadId ? (
              <LeadPicker onSelecionar={(lead: LeadListItem) => { set("indicadoPorLeadId", lead.id); set("indicadoPorLeadNome", lead.nomeOuRazaoSocial); }} />
            ) : (
              <div className="flex items-center justify-between rounded-lg bg-[var(--surface-hover)] px-3 py-2 text-sm">
                <span className="text-[var(--fg)]">{valores.indicadoPorLeadNome}</span>
                <button
                  type="button"
                  className="focus-ring text-xs text-[var(--fg-muted)] hover:text-[var(--fg)]"
                  onClick={() => { set("indicadoPorLeadId", ""); set("indicadoPorLeadNome", ""); }}
                >
                  Remover
                </button>
              </div>
            )}
          </div>
          <div>
            <Label htmlFor={`${idPrefix}-tipo-indicacao`}>Tipo de indicação</Label>
            <Input id={`${idPrefix}-tipo-indicacao`} value={valores.tipoIndicacao} onChange={(e) => set("tipoIndicacao", e.target.value)} />
          </div>
        </div>
      </div>

      <div className="border-t border-[var(--border)] pt-4">
        <h3 className="mb-3 text-sm font-semibold text-[var(--fg)]">Marketing e rastreamento</h3>
        <div className="grid gap-4 sm:grid-cols-2">
          <div>
            <Label htmlFor={`${idPrefix}-gclid`}>GCLID (Google Ads)</Label>
            <Input id={`${idPrefix}-gclid`} value={valores.gclid} onChange={(e) => set("gclid", e.target.value)} />
          </div>
          <div>
            <Label htmlFor={`${idPrefix}-utm-source`}>UTM Source</Label>
            <Input id={`${idPrefix}-utm-source`} value={valores.utmSource} onChange={(e) => set("utmSource", e.target.value)} />
          </div>
          <div>
            <Label htmlFor={`${idPrefix}-utm-medium`}>UTM Medium</Label>
            <Input id={`${idPrefix}-utm-medium`} value={valores.utmMedium} onChange={(e) => set("utmMedium", e.target.value)} />
          </div>
          <div>
            <Label htmlFor={`${idPrefix}-utm-term`}>UTM Term</Label>
            <Input id={`${idPrefix}-utm-term`} value={valores.utmTerm} onChange={(e) => set("utmTerm", e.target.value)} />
          </div>
          <div>
            <Label htmlFor={`${idPrefix}-meta-click`}>Meta Click ID</Label>
            <Input id={`${idPrefix}-meta-click`} value={valores.metaClickId} onChange={(e) => set("metaClickId", e.target.value)} />
          </div>
          <div>
            <Label htmlFor={`${idPrefix}-meta-form`}>Meta Form ID</Label>
            <Input id={`${idPrefix}-meta-form`} value={valores.metaFormId} onChange={(e) => set("metaFormId", e.target.value)} />
          </div>
          <div>
            <Label htmlFor={`${idPrefix}-meta-lead`}>Meta Lead ID</Label>
            <Input id={`${idPrefix}-meta-lead`} value={valores.metaLeadId} onChange={(e) => set("metaLeadId", e.target.value)} />
          </div>
        </div>
      </div>

      <div className="border-t border-[var(--border)] pt-4">
        <Checkbox
          label="O lead consentiu em ser contatado (LGPD)"
          checked={valores.consentimentoContato}
          onChange={(e) => set("consentimentoContato", e.target.checked)}
        />
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
