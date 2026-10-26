// Guarda contra os defeitos de celular que já aconteceram. Roda no CI (npm run verificar:mobile) e falha com a explicação de como corrigir.
//
// 1. Janela/painel/alerta com "fixed inset-0" desenhado dentro da tela (sem Portal): se algum elemento acima tiver animação com transform, filter ou
//    backdrop-filter (.entra, .card-anim, .ui-surface no tema vidro...), o "fixed" passa a ser relativo a ele e a janela aparece fora da tela.
// 2. Regras globais de celular que não podem ser apagadas do index.css (campos com 16 px, altura dinâmica, sem balanço lateral).
import { readFileSync, readdirSync, statSync } from "node:fs";
import { join, relative, sep } from "node:path";

const raiz = new URL("../src/", import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, "$1");
const erros = [];

function arquivos(dir) {
  return readdirSync(dir).flatMap((nome) => {
    const caminho = join(dir, nome);
    return statSync(caminho).isDirectory() ? arquivos(caminho) : [caminho];
  });
}

// Arquivos que podem ter "fixed inset-0": os que já desenham por Portal ou são camadas da própria estrutura da tela.
const permitidos = new Set(["components/ui.tsx", "components/crm/PainelLead.tsx", "components/Shell.tsx", "pages/Chat.tsx"]);

for (const arq of arquivos(raiz).filter((a) => a.endsWith(".tsx"))) {
  const rel = relative(raiz, arq).split(sep).join("/");
  const texto = readFileSync(arq, "utf8");
  if (/\bfixed\s+inset-(0|x-0|y-0)\b/.test(texto) && !permitidos.has(rel)) {
    erros.push(`${rel}: usa "fixed inset-0" fora do Portal. Use <Modal>, <Drawer> ou <ConfirmDialog> (components/ui.tsx), ou envolva em <Portal>.`);
  }
}

const ui = readFileSync(join(raiz, "components/ui.tsx"), "utf8");
if (!/createPortal\(children, document\.body\)/.test(ui)) erros.push("components/ui.tsx: o Portal (createPortal no body) sumiu. Modal e Drawer precisam dele.");
const modal = ui.slice(ui.indexOf("export function Modal"), ui.indexOf("// --- Drawer lateral ---"));
const drawer = ui.slice(ui.indexOf("export function Drawer"), ui.indexOf("// --- Confirmação de ação crítica ---"));
if (!modal.includes("<Portal>")) erros.push("components/ui.tsx: Modal precisa renderizar dentro de <Portal>.");
if (!drawer.includes("<Portal>")) erros.push("components/ui.tsx: Drawer precisa renderizar dentro de <Portal>.");
if (!readFileSync(join(raiz, "components/crm/PainelLead.tsx"), "utf8").includes("<Portal>")) erros.push("components/crm/PainelLead.tsx: o painel do lead precisa renderizar dentro de <Portal>.");

const css = readFileSync(join(raiz, "index.css"), "utf8");
const exigidos = [
  [/@media \(pointer: coarse\)[\s\S]{0,200}font-size: 16px/, "campos com 16 px nos aparelhos de toque (sem isso o iPhone dá zoom ao tocar num campo)"],
  [/\.min-h-screen\s*\{[^}]*100dvh/, "min-h-screen com dvh (a barra de endereço do celular muda a altura da tela)"],
  [/overflow-x: clip/, "overflow-x: clip no html/body (nada faz a página balançar para o lado)"],
  [/\.modal-painel\s*\{[^}]*85dvh/, "altura das janelas em dvh"],
];
for (const [regra, descricao] of exigidos) if (!regra.test(css)) erros.push(`index.css: faltou a regra de celular — ${descricao}.`);

if (erros.length > 0) {
  console.error("Verificação de celular falhou:\n - " + erros.join("\n - "));
  process.exit(1);
}
console.log("Verificação de celular: ok.");
