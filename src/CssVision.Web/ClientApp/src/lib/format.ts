const moeda = new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL" });
const numero = new Intl.NumberFormat("pt-BR");
const dataHora = new Intl.DateTimeFormat("pt-BR", { dateStyle: "short", timeStyle: "short" });
const dataCurta = new Intl.DateTimeFormat("pt-BR", { dateStyle: "short" });

export function formatarMoeda(valor: number | null | undefined): string {
  return moeda.format(valor ?? 0);
}

export function formatarNumero(valor: number | null | undefined): string {
  return numero.format(valor ?? 0);
}

export function formatarPercentual(valor: number | null | undefined): string {
  return `${numero.format(valor ?? 0)}%`;
}

export function formatarData(valor: string | null | undefined): string {
  if (!valor) return "-";
  return dataCurta.format(new Date(valor));
}

export function formatarDataHora(valor: string | null | undefined): string {
  if (!valor) return "-";
  return dataHora.format(new Date(valor));
}

export function formatarTelefone(valor: string | null | undefined): string {
  if (!valor) return "-";
  const digitos = valor.replace(/\D/g, "");
  if (digitos.length === 11) return `(${digitos.slice(0, 2)}) ${digitos.slice(2, 7)}-${digitos.slice(7)}`;
  if (digitos.length === 10) return `(${digitos.slice(0, 2)}) ${digitos.slice(2, 6)}-${digitos.slice(6)}`;
  return valor;
}

export function formatarDocumento(valor: string | null | undefined): string {
  if (!valor) return "-";
  const digitos = valor.replace(/\D/g, "");
  if (digitos.length === 11) {
    return `${digitos.slice(0, 3)}.${digitos.slice(3, 6)}.${digitos.slice(6, 9)}-${digitos.slice(9)}`;
  }
  if (digitos.length === 14) {
    return `${digitos.slice(0, 2)}.${digitos.slice(2, 5)}.${digitos.slice(5, 8)}/${digitos.slice(8, 12)}-${digitos.slice(12)}`;
  }
  return valor;
}

export function diasRelativos(valor: string | null | undefined): string {
  if (!valor) return "-";
  const data = new Date(valor);
  const diffMs = data.getTime() - Date.now();
  const dias = Math.round(diffMs / 86_400_000);
  if (dias === 0) return "hoje";
  if (dias === 1) return "amanhã";
  if (dias === -1) return "ontem";
  return dias > 0 ? `em ${dias} dias` : `há ${Math.abs(dias)} dias`;
}
