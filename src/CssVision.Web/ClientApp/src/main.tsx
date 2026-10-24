import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
import App from "./App.tsx";
import { AuthProvider } from "./context/AuthContext.tsx";
import { ThemeProvider } from "./context/ThemeContext.tsx";
import { ToastProvider } from "./components/ui.tsx";
import "./index.css";

// Enquanto uma área rola, a barra de rolagem dela aparece (mesmo sem o mouse em cima) e some uns instantes depois de parar.
const temposRolagem = new WeakMap<Element, number>();
document.addEventListener(
  "scroll",
  (e) => {
    const alvo = e.target instanceof Element ? e.target : document.documentElement;
    alvo.setAttribute("data-rolando", "");
    window.clearTimeout(temposRolagem.get(alvo));
    temposRolagem.set(alvo, window.setTimeout(() => alvo.removeAttribute("data-rolando"), 900));
  },
  { capture: true, passive: true }
);

// Clique em botões e controles parecidos: dispara a animação de "aperto" (.clique) e a tira depois.
document.addEventListener(
  "click",
  (e) => {
    const alvo = e.target instanceof Element ? e.target.closest<HTMLElement>('button, [role="button"], [role="tab"], [role="radio"], [role="switch"]') : null;
    if (!alvo || (alvo as HTMLButtonElement).disabled || alvo.getAttribute("aria-disabled") === "true" || alvo.classList.contains("sr-only")) return;
    alvo.classList.remove("clique");
    void alvo.offsetWidth; // reinicia se clicar de novo rápido
    alvo.classList.add("clique");
    window.setTimeout(() => alvo.classList.remove("clique"), 520);
  },
  { passive: true }
);

// Troca de aba em qualquer tela: o conteúdo logo abaixo da barra de abas entra deslizando do lado da aba escolhida (da direita se for uma aba mais adiante, da
// esquerda se for uma anterior). Barras de abas que não controlam um conteúdo logo abaixo marcam data-sem-animacao.
document.addEventListener(
  "click",
  (e) => {
    const aba = e.target instanceof Element ? e.target.closest<HTMLElement>('[role="tab"]') : null;
    const lista = aba?.closest<HTMLElement>('[role="tablist"]');
    if (!aba || !lista || lista.hasAttribute("data-sem-animacao") || aba.getAttribute("aria-selected") === "true") return;
    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;
    const abas = [...lista.querySelectorAll<HTMLElement>('[role="tab"]')];
    const lado = abas.indexOf(aba) >= abas.findIndex((a) => a.getAttribute("aria-selected") === "true") ? 1 : -1;
    // Espera o React trocar o conteúdo (dois quadros) para animar o que está lá depois da troca.
    requestAnimationFrame(() =>
      requestAnimationFrame(() => {
        const alvo = (lista.nextElementSibling ?? lista.parentElement?.nextElementSibling) as (HTMLElement & { __animacaoDeAba?: Animation }) | null;
        if (!alvo || typeof alvo.animate !== "function") return;
        alvo.__animacaoDeAba?.cancel();
        alvo.__animacaoDeAba = alvo.animate(
          [{ opacity: 0, transform: `translateX(${lado * 22}px)` }, { opacity: 1, transform: "translateX(0)" }],
          { duration: 340, easing: "cubic-bezier(0.22, 1, 0.36, 1)" }
        );
      })
    );
  },
  { capture: true, passive: true }
);

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <ThemeProvider>
      <ToastProvider>
        <BrowserRouter>
          <AuthProvider>
            <App />
          </AuthProvider>
        </BrowserRouter>
      </ToastProvider>
    </ThemeProvider>
  </StrictMode>
);
