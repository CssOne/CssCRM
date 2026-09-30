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
  event.waitUntil(
    self.registration.showNotification(dados.titulo || "CSS Brasil CRM", {
      body: dados.corpo || "",
      icon: "/logo-css.png",
      badge: "/logo-css.png",
      tag: dados.tag || undefined,
      renotify: !!dados.tag,
      data: { url: dados.url || "/app/crm/leads/kanban" },
    })
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
