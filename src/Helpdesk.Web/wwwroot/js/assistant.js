// AI assistant widget: calls /api/assistant/* and renders results as plain text.
(function () {
  "use strict";

  const panel = document.getElementById("ai-assistant");
  if (!panel) return;

  const ticketId = panel.dataset.ticketId;
  const token = panel.querySelector("input[name='__RequestVerificationToken']").value;
  const el = (name) => panel.querySelector(`[data-ai-${name}]`);
  const result = el("result");
  const output = el("output");
  const sources = el("sources");
  const meta = el("meta");
  const error = el("error");
  const loading = el("loading");
  const title = el("title");
  const insert = el("insert");
  const buttons = panel.querySelectorAll("button");

  // CPU-only Ollama can be slow; stop waiting after 5 minutes (matches the server-side timeout).
  const CLIENT_TIMEOUT_MS = 5 * 60 * 1000;

  function show(node, visible) { node.classList.toggle("d-none", !visible); }

  function reset(label) {
    show(result, true);
    title.textContent = label;
    output.textContent = "";
    sources.replaceChildren();
    meta.textContent = "";
    error.textContent = "";
    [output, sources, error, insert].forEach((n) => show(n, false));
    show(loading, true);
    buttons.forEach((b) => (b.disabled = true));
  }

  function done() {
    show(loading, false);
    buttons.forEach((b) => (b.disabled = false));
  }

  async function call(url, body) {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), CLIENT_TIMEOUT_MS);
    try {
      const res = await fetch(url, {
        method: "POST",
        headers: { "Content-Type": "application/json", "RequestVerificationToken": token },
        body: body ? JSON.stringify(body) : null,
        credentials: "same-origin",
        signal: controller.signal,
      });
      const data = await res.json().catch(() => ({}));
      if (!res.ok) {
        throw new Error(data.title || `Request failed (HTTP ${res.status}).`);
      }
      return data;
    } finally {
      clearTimeout(timer);
    }
  }

  function render(data) {
    output.textContent = data.text;
    show(output, true);
    if (data.type === "KbAnswer" && data.found && data.sources.length) {
      const label = document.createElement("li");
      label.className = "list-unstyled ms-n3 text-muted";
      label.textContent = "Sources used:";
      sources.appendChild(label);
    } else if (data.type === "ReplyDraft" && data.sources.length) {
      const label = document.createElement("li");
      label.className = "list-unstyled ms-n3 text-muted";
      label.textContent = "Related articles:";
      sources.appendChild(label);
    }
    data.sources.forEach((s, i) => {
      const li = document.createElement("li");
      const a = document.createElement("a");
      a.href = s.url;
      a.textContent = data.type === "KbAnswer" ? `[${i + 1}] ${s.title}` : s.title;
      a.target = "_blank";
      a.rel = "noopener";
      li.appendChild(a);
      sources.appendChild(li);
    });
    show(sources, data.sources.length > 0);
    const seconds = (data.latencyMs / 1000).toFixed(1);
    meta.textContent = data.latencyMs > 0
      ? `${data.model} via ${data.provider} \u00b7 ${seconds} s \u00b7 logged as #${data.interactionId}`
      : "No matching article, so the model was not called.";
    show(insert, data.type === "ReplyDraft");
  }

  async function run(label, url, body) {
    reset(label);
    try {
      render(await call(url, body));
    } catch (e) {
      error.textContent = e.name === "AbortError" ? "The assistant took too long to respond." : e.message;
      show(error, true);
    } finally {
      done();
    }
  }

  panel.querySelectorAll("[data-ai-action]").forEach((btn) => {
    btn.addEventListener("click", () => {
      const action = btn.dataset.aiAction;
      const label = action === "summary" ? "Status summary" : "Suggested reply (edit before posting)";
      run(label, `/api/assistant/tickets/${ticketId}/${action}`);
    });
  });

  panel.querySelector("[data-ai-kb-form]").addEventListener("submit", (e) => {
    e.preventDefault();
    const input = panel.querySelector("#ai-question");
    const question = input.value.trim();
    if (question.length < 3) {
      input.classList.add("is-invalid");
      return;
    }
    input.classList.remove("is-invalid");
    run("Knowledge base answer", "/api/assistant/kb-answer", { question });
  });

  insert.addEventListener("click", () => {
    const reply = document.getElementById("reply-body");
    if (!reply) return;
    reply.value = output.textContent;
    reply.focus();
    reply.scrollIntoView({ behavior: "smooth", block: "center" });
  });
})();
