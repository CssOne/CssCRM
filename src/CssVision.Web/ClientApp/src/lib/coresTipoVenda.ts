// Cor de cada tipo de venda (Canal de Aquisição) na barra da Meta comercial do mês; tipos fora da lista pegam as de reserva, sempre a mesma para o mesmo nome.
const CORES_TIPO: Record<string, string> = {
  "lead": "#3f63c7",
  "indicação": "#16a34a",
  "indicação lead": "#0d9488",
  "indicação pessoal": "#f59e0b",
  "pessoal": "#f59e0b",
  "parceria": "#8b5cf6",
  "ação externa": "#06b6d4",
  "css": "#ec4899",
  "contemplando sonhos": "#f97316",
  "sem tipo": "#94a3b8",
};
const CORES_RESERVA = ["#84cc16", "#a855f7", "#14b8a6", "#eab308", "#6366f1"];

export function corDoTipo(tipo: string) {
  const chave = tipo.trim().toLowerCase();
  if (CORES_TIPO[chave]) return CORES_TIPO[chave];
  let soma = 0;
  for (const c of chave) soma += c.charCodeAt(0);
  return CORES_RESERVA[soma % CORES_RESERVA.length];
}
