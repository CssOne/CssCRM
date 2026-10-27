/*
 * Service worker das notificações push do CRM (aviso de lead novo). Roda no navegador mesmo com o
 * CRM fechado: recebe a mensagem do servidor (PushService) e mostra a notificação do sistema.
 * Clicar nela abre o CRM (ou foca a aba já aberta) no lead.
 */
self.addEventListener("install", () => self.skipWaiting());
self.addEventListener("activate", (event) => event.waitUntil(self.clients.claim()));

self.addEventListener("push", (event) => {
  let dados = {};
  try {
    dados = event.data ? event.data.json() : {};
  } catch {
    dados = { corpo: event.data ? event.data.text() : "" };
  }
  // Alertas da gestão (leads parados na distribuição, consultor que demora): com o CRM aberto, a aba toca o toque deles (a notificação do sistema não tem som próprio).
  const alertaDaGestao = typeof dados.tag === "string" && (dados.tag === "alerta-limite-distribuicao" || dados.tag.startsWith("espera-gestao-"));
  event.waitUntil(
    Promise.all([
      self.registration.showNotification(dados.titulo || "CSS Brasil CRM", {
        body: dados.corpo || "",
        icon: "/logo-css.png",
        badge: "/logo-css.png",
        tag: dados.tag || undefined,
        renotify: !!dados.tag,
        data: { url: dados.url || "/app/crm/leads/kanban" },
      }),
      alertaDaGestao
        ? self.clients.matchAll({ type: "window", includeUncontrolled: true }).then((janelas) => janelas.forEach((j) => j.postMessage({ tipo: "alerta-gestao" })))
        : Promise.resolve(),
    ])
  );
});

self.addEventListener("notificationclick", (event) => {
  event.notification.close();
  const destino = new URL(event.notification.data?.url || "/app/crm/leads/kanban", self.location.origin).href;
  event.waitUntil(
    self.clients.matchAll({ type: "window", includeUncontrolled: true }).then((janelas) => {
      const doCrm = janelas.find((j) => j.url.startsWith(self.location.origin));
      if (doCrm) {
        return doCrm.focus().then((j) => (j && "navigate" in j ? j.navigate(destino) : undefined));
      }
      return self.clients.openWindow(destino);
    })
  );
});
