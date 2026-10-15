/** Validações de formulário no padrão brasileiro, com a mensagem específica de cada erro (o servidor aplica as mesmas regras). */

const soDigitos = (v: string | null | undefined) => (v ?? "").replace(/\D/g, "");

function cpfValido(d: string): boolean {
  if (d.length !== 11 || /^(\d)\1+$/.test(d)) return false;
  for (const t of [9, 10]) {
    let soma = 0;
    for (let i = 0; i < t; i++) soma += Number(d[i]) * (t + 1 - i);
    const dv = ((soma * 10) % 11) % 10;
    if (dv !== Number(d[t])) return false;
  }
  return true;
}

function cnpjValido(d: string): boolean {
  if (d.length !== 14 || /^(\d)\1+$/.test(d)) return false;
  for (const t of [12, 13]) {
    let soma = 0;
    let peso = t - 7;
    for (let i = 0; i < t; i++) {
      soma += Number(d[i]) * peso--;
      if (peso < 2) peso = 9;
    }
    const dv = soma % 11 < 2 ? 0 : 11 - (soma % 11);
    if (dv !== Number(d[t])) return false;
  }
  return true;
}

/** Mensagem de erro do CPF/CNPJ, ou null quando está vazio (opcional) ou válido. */
export function erroDocumento(valor: string | null | undefined): string | null {
  const d = soDigitos(valor);
  if (d.length === 0) return null;
  if (d.length === 11) return cpfValido(d) ? null : "CPF inválido: confira os números digitados.";
  if (d.length === 14) return cnpjValido(d) ? null : "CNPJ inválido: confira os números digitados.";
  if (d.length < 11) return `CPF incompleto: faltam ${11 - d.length} dígito(s).`;
  if (d.length < 14) return `CNPJ incompleto: faltam ${14 - d.length} dígito(s).`;
  return "CPF/CNPJ com dígitos a mais: um CPF tem 11 e um CNPJ tem 14.";
}

/** Mensagem de erro do telefone (obrigatório), ou null quando válido. Aceita DDD + 8 ou 9 dígitos, com ou sem o 55. */
export function erroTelefone(valor: string | null | undefined): string | null {
  let d = soDigitos(valor);
  if (d.length === 0) return "Informe o telefone do cliente.";
  if ((d.length === 12 || d.length === 13) && d.startsWith("55")) d = d.slice(2);
  if (d.length < 10) return "Telefone incompleto: informe o DDD e o número (ex.: (11) 91234-5678).";
  if (d.length > 11) return "Telefone com dígitos a mais: use DDD + número (10 ou 11 dígitos).";
  if (d.length === 11 && d[2] !== "9") return "Celular inválido: com 11 dígitos o número deve começar com 9 depois do DDD.";
  if (/^(\d)\1+$/.test(d)) return "Telefone inválido: confira os números digitados.";
  return null;
}
