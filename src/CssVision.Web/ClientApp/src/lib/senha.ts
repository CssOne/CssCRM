const MINUSCULAS = "abcdefghijkmnopqrstuvwxyz";
const MAIUSCULAS = "ABCDEFGHJKLMNPQRSTUVWXYZ";
const DIGITOS = "23456789";
const SIMBOLOS = "!@#$%&*?";

/** Mínimo que o servidor exige na senha (igual a Password.RequiredLength do back-end). */
export const TAMANHO_MINIMO_DA_SENHA = 10;

/** Inteiro aleatório de 0 a limite-1, sem viés (descarta os valores que quebrariam a divisão igual). */
function indiceAleatorio(limite: number): number {
  const teto = Math.floor(0x1_0000_0000 / limite) * limite;
  const buffer = new Uint32Array(1);
  do crypto.getRandomValues(buffer);
  while (buffer[0] >= teto);
  return buffer[0] % limite;
}

const sortear = (alfabeto: string) => alfabeto[indiceAleatorio(alfabeto.length)];

/** Senha aleatória forte (sem caracteres que se confundem, como 0/O e 1/l), com maiúscula, minúscula, dígito e símbolo. */
export function gerarSenha(tamanho = 16): string {
  const todos = MINUSCULAS + MAIUSCULAS + DIGITOS + SIMBOLOS;
  const caracteres = [sortear(MINUSCULAS), sortear(MAIUSCULAS), sortear(DIGITOS), sortear(SIMBOLOS)];
  while (caracteres.length < Math.max(tamanho, TAMANHO_MINIMO_DA_SENHA)) caracteres.push(sortear(todos));
  // Embaralha para os quatro obrigatórios não ficarem sempre no começo.
  for (let i = caracteres.length - 1; i > 0; i--) {
    const j = indiceAleatorio(i + 1);
    [caracteres[i], caracteres[j]] = [caracteres[j], caracteres[i]];
  }
  return caracteres.join("");
}
