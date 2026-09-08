import { useRef, useState } from "react";
import { uploadFile } from "../../lib/api";
import type { LeadImportResult } from "../../lib/types";
import { Badge, Button, Modal } from "../ui";

export function ImportModal({ open, onClose, onImportado }: { open: boolean; onClose: () => void; onImportado: () => void }) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [arquivo, setArquivo] = useState<File | null>(null);
  const [enviando, setEnviando] = useState(false);
  const [resultado, setResultado] = useState<LeadImportResult | null>(null);
  const [erro, setErro] = useState<string | null>(null);

  function fechar() {
    setArquivo(null);
    setResultado(null);
    setErro(null);
    onClose();
  }

  async function enviar() {
    if (!arquivo) return;
    setEnviando(true);
    setErro(null);
    try {
      const res = await uploadFile<LeadImportResult>("/crm/leads/import", arquivo);
      setResultado(res);
      onImportado();
    } catch {
      setErro("Não foi possível importar a planilha. Verifique o formato do arquivo.");
    } finally {
      setEnviando(false);
    }
  }

  return (
    <Modal open={open} onClose={fechar} title="Importar leads por planilha">
      <div className="space-y-4">
        <p className="text-sm text-[var(--fg-muted)]">
          Envie um arquivo .xlsx com as colunas: Nome, Tipo (Física/Jurídica), CPF/CNPJ, Telefone, E-mail, Cidade, UF,
          Regional, Origem, E-mail do responsável.
        </p>

        <input
          ref={inputRef}
          type="file"
          accept=".xlsx"
          onChange={(e) => setArquivo(e.target.files?.[0] ?? null)}
          className="block w-full text-sm text-[var(--fg-muted)] file:mr-3 file:rounded-lg file:border-0 file:bg-[var(--brand-soft)] file:px-3 file:py-2 file:text-sm file:font-medium file:text-[var(--brand)]"
        />

        {erro && <p className="text-sm text-[var(--danger)]">{erro}</p>}

        {resultado && (
          <div className="space-y-2 rounded-lg border border-[var(--border)] p-3 text-sm">
            <div className="flex flex-wrap gap-2">
              <Badge variant="neutral">{resultado.totalLinhas} linha(s)</Badge>
              <Badge variant="success">{resultado.importados} importado(s)</Badge>
              <Badge variant="warning">{resultado.duplicados} duplicado(s)</Badge>
              <Badge variant="danger">{resultado.comErro} com erro</Badge>
            </div>
            {resultado.erros.length > 0 && (
              <ul className="max-h-32 list-disc space-y-1 overflow-y-auto pl-5 text-xs text-[var(--fg-muted)]">
                {resultado.erros.map((e, i) => (
                  <li key={i}>{e}</li>
                ))}
              </ul>
            )}
          </div>
        )}

        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={fechar}>
            {resultado ? "Fechar" : "Cancelar"}
          </Button>
          {!resultado && (
            <Button onClick={enviar} loading={enviando} disabled={!arquivo}>
              Importar
            </Button>
          )}
        </div>
      </div>
    </Modal>
  );
}
