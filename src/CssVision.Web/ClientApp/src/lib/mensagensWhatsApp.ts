/** Primeiro nome de uma pessoa ("Maria Souza" → "Maria"). */
const primeiroNome = (nome: string | null | undefined) => (nome ?? "").trim().split(/\s+/)[0] ?? "";

const semAcento = (texto: string) => texto.normalize("NFD").replace(/[̀-ͯ]/g, "").toLowerCase();

/**
 * Mensagem pronta que abre junto com a conversa do WhatsApp, conforme a etapa em que o cliente está (casada pelo nome, sem acento/maiúsculas).
 * O consultor pode editar antes de enviar — o WhatsApp só preenche a caixa de texto.
 */
export function mensagemWhatsApp(etapa: string | null | undefined, nomeCliente: string, nomeConsultor?: string | null): string {
  const cliente = primeiroNome(nomeCliente);
  const abertura = `Olá, ${cliente}! Aqui é ${primeiroNome(nomeConsultor) || "o seu consultor"} da CSS Brasil.`;
  const nome = semAcento(etapa ?? "");

  if (/cotac/.test(nome)) return `${abertura} Enviei a cotação da proteção do seu veículo. Ficou alguma dúvida? Posso ajustar coberturas para você.`;
  if (/venda concluida/.test(nome)) return `${abertura} Seja bem-vindo(a)! Sua proteção está ativa. Qualquer dúvida ou necessidade, pode me chamar por aqui.`;
  if (/perdido/.test(nome)) return `${abertura} Faz um tempo que conversamos sobre a proteção do seu veículo. Se algo mudou, posso montar uma nova proposta para você.`;
  if (/nao fazemos/.test(nome)) return `${abertura} Na época não conseguimos atender o seu veículo, mas podemos ter novidades. Posso verificar de novo para você?`;
  if (/em atendimento.*indicac/.test(nome)) return `${abertura} Recebi a sua indicação e queria saber se posso tirar alguma dúvida sobre a proteção do seu veículo.`;
  if (/em atendimento/.test(nome)) return `${abertura} Passando para saber se ficou alguma dúvida sobre a proteção do seu veículo. Posso te ajudar?`;
  return `${abertura} Posso te ajudar com a proteção do seu veículo?`;
}
