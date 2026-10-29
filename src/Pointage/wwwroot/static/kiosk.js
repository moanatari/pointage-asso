"use strict";
// Borne de pointage : la page ne contient aucune donnée, tout passe par /api/borne (même origine,
// le cookie de liaison part tout seul). Sans bibliothèque, pour tourner sur n'importe quelle tablette.
(() => {
  const $ = id => document.getElementById(id);
  const UNAVAILABLE = "Le pointage est indisponible, signale-toi au responsable.";

  async function post(url, body) {
    return fetch(url, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) });
  }

  // Étape 0 : borne non liée, on échange le code contre le cookie puis on recharge.
  const pairForm = $("pair");
  if (pairForm) {
    const error = $("pair-error");
    pairForm.addEventListener("submit", async e => {
      e.preventDefault();
      try {
        const res = await post("/api/borne/pair", { code: $("pair-code").value.trim() });
        if (res.ok) { location.reload(); return; }
        error.textContent = "Code incorrect ou expiré. Demandez un nouveau code au responsable si besoin.";
      } catch {
        error.textContent = UNAVAILABLE;
      }
      error.hidden = false;
    });
    return;
  }

  // Un 401 veut dire que la borne a été révoquée : le rechargement ramène au formulaire de liaison.
  async function api(path, body) {
    const res = body === undefined ? await fetch("/api/borne/" + path) : await post("/api/borne/" + path, body);
    if (res.status === 401) { location.reload(); throw new Error("401"); }
    if (!res.ok) throw new Error(String(res.status));
    return res.json();
  }

  const q = $("q"), results = $("results"), error = $("error");
  const yes = $("yes"), no = $("no"), leave = $("leave");
  let config = { keyboard: false, departure: false };
  let selected = null, searchTimer, idleTimer, homeTimer;

  const show = id => { for (const s of document.querySelectorAll("section")) s.hidden = s.id !== id; };
  const fail = () => { error.hidden = false; };
  // Le libellé est « Prénom N. » : on retire l'initiale pour s'adresser à l'enfant par son prénom.
  const firstName = label => label.replace(/ \S\.$/, "");

  function home() {
    clearTimeout(homeTimer);
    clearTimeout(searchTimer);
    selected = null;
    q.value = "";
    results.replaceChildren();
    error.hidden = true;
    show("step-search");
  }

  function resetIdle() {
    clearTimeout(idleTimer);
    idleTimer = setTimeout(home, 30000);
  }
  for (const type of ["pointerdown", "keydown", "input"]) document.addEventListener(type, resetIdle);

  function search() {
    clearTimeout(searchTimer);
    searchTimer = setTimeout(async () => {
      const term = q.value.trim();
      if (term.length < 2) { results.replaceChildren(); return; }
      try {
        const members = await api("search?q=" + encodeURIComponent(term));
        if (q.value.trim() !== term) return; // réponse périmée : l'enfant a continué de taper
        error.hidden = true;
        results.replaceChildren(...members.map(m => {
          const b = document.createElement("button");
          b.className = "member";
          b.textContent = `${m.label} · ${m.code}`; // textContent : jamais d'injection HTML
          b.addEventListener("click", () => choose(m));
          const li = document.createElement("li");
          li.append(b);
          return li;
        }));
      } catch { fail(); }
    }, 250);
  }
  q.addEventListener("input", search);

  function choose(m) {
    selected = m;
    $("who").textContent = `${m.label} · ${m.code}`;
    leave.hidden = !(config.departure && m.hasPresenceToday);
    for (const b of [yes, no, leave]) b.disabled = false;
    show("step-confirm");
  }

  // Boutons désactivés dès le premier appui : un double appui n'envoie qu'une requête.
  async function act(fn) {
    for (const b of [yes, no, leave]) b.disabled = true;
    try {
      done(await fn(selected));
    } catch {
      home();
      fail();
    }
  }

  function done(message) {
    $("message").textContent = message;
    show("step-done");
    homeTimer = setTimeout(home, 5000);
  }

  yes.addEventListener("click", () => act(async m => {
    const r = await api("checkin", { memberId: m.id });
    return r.result === "created"
      ? `Merci ${firstName(m.label)}, ton arrivée est notée ! Va voir le responsable pour qu'il te valide.`
      : "Tu es déjà noté aujourd'hui.";
  }));
  leave.addEventListener("click", () => act(async m => {
    const r = await api("depart", { memberId: m.id });
    return r.ok ? `Au revoir ${firstName(m.label)}, ton départ est noté !` : "Ton départ n'a pas pu être noté, signale-toi au responsable.";
  }));
  no.addEventListener("click", home);

  function buildKeyboard() {
    const kb = $("keyboard");
    const key = (text, onPress, cls) => {
      const b = document.createElement("button");
      b.type = "button";
      b.textContent = text;
      if (cls) b.className = cls;
      b.addEventListener("click", () => { onPress(); search(); });
      kb.append(b);
    };
    for (const c of "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789") key(c, () => { q.value += c; });
    key("⌫ Effacer", () => { q.value = q.value.slice(0, -1); }, "erase");
    kb.hidden = false;
  }

  home();
  resetIdle();
  api("config").then(c => {
    config = c;
    if (config.keyboard) buildKeyboard();
  }).catch(fail);
})();
