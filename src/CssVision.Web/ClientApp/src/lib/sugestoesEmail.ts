export interface SugestaoEmail {
  assunto: string;
  mensagem: string;
}

const ASSINATURA = "\n\nQualquer dúvida, é só responder este e-mail.\nUm abraço!";

/** Sugestões de e-mail por etapa do quadro de leads (casadas pelo nome, sem acento/maiúsculas). O consultor pode editar tudo antes de enviar. */
const SUGESTOES: { quando: RegExp; sugestao: SugestaoEmail }[] = [
  {
    quando: /em atendimento.*indicac/,
    sugestao: {
      assunto: "Obrigado pela indicação, {nome}!",
      mensagem: `Olá, {nome}!\n\nEstou cuidando do seu atendimento a partir da indicação que recebi e queria saber se posso tirar alguma dúvida sobre a proteção do seu veículo. Quando for melhor para conversarmos?${ASSINATURA}`,
    },
  },
  {
    quando: /em atendimento/,
    sugestao: {
      assunto: "{nome}, vamos continuar o seu atendimento?",
      mensagem: `Olá, {nome}!\n\nPassando para saber se ficou alguma dúvida sobre a proteção do seu veículo. Posso te ajudar com valores, coberturas ou o que mais precisar — é só me dizer o melhor horário para conversarmos.${ASSINATURA}`,
    },
  },
  {
    quando: /cotac/,
    sugestao: {
      assunto: "{nome}, sua cotação está te esperando",
      mensagem: `Olá, {nome}!\n\nEnviei a cotação da proteção do seu veículo e queria saber o que achou. Posso ajustar coberturas ou tirar qualquer dúvida para você fechar com tranquilidade. Vamos conversar?${ASSINATURA}`,
    },
  },
  {
    quando: /venda concluida/,
    sugestao: {
      assunto: "Bem-vindo à CSS Brasil, {nome}!",
      mensagem: `Olá, {nome}!\n\nSeja bem-vindo(a)! Sua proteção está ativa e agradeço muito a confiança. Se precisar de qualquer coisa — assistência, dúvidas ou indicar um amigo — pode contar comigo.${ASSINATURA}`,
    },
  },
  {
    quando: /perdido/,
    sugestao: {
      assunto: "{nome}, podemos rever sua proposta?",
      mensagem: `Olá, {nome}!\n\nFaz um tempo que conversamos sobre a proteção do seu veículo. Se algo mudou ou se quiser rever valores e condições, estou à disposição para montar uma nova proposta.${ASSINATURA}`,
    },
  },
  {
    quando: /nao fazemos/,
    sugestao: {
      assunto: "Novidades para o seu veículo, {nome}",
      mensagem: `Olá, {nome}!\n\nNa época não conseguimos atender o seu veículo, mas estamos sempre ampliando o que oferecemos. Se quiser, posso verificar de novo se já é possível te atender.${ASSINATURA}`,
    },
  },
];

const GENERICA: SugestaoEmail = {
  assunto: "{nome}, tudo bem?",
  mensagem: `Olá, {nome}!\n\nPassando para saber se posso ajudar com algo sobre a proteção do seu veículo. Fico à disposição.${ASSINATURA}`,
};

export function sugestaoParaEtapa(nomeEtapa: string): SugestaoEmail {
  const nome = nomeEtapa.normalize("NFD").replace(/[̀-ͯ]/g, "").toLowerCase();
  return SUGESTOES.find((s) => s.quando.test(nome))?.sugestao ?? GENERICA;
}
