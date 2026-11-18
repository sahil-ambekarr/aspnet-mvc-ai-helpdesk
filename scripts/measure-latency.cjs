// Measures end-to-end latency of the three assistant features through the app's JSON API.
// Requests run one at a time. Usage: start the app (port 4110), then
//   node scripts/measure-latency.cjs > docs/eval/latency-results.json
const { request } = require("playwright");

const BASE = process.env.BASE_URL || "http://localhost:4110";
const TICKETS = [1, 3, 5, 14, 29];
const QUESTIONS = [
  "My VPN keeps dropping every hour when I work from home. What should I do?",
  "I forgot my password and my account is locked",
  "How do I give visitors Wi-Fi access?",
  "Colleagues hear an echo in my video calls",
  "What is the parking policy for the new office?", // not covered by the KB
];

async function main() {
  const api = await request.newContext({ baseURL: BASE, timeout: 10 * 60 * 1000 });
  const login = await (await api.get("/Account/Login")).text();
  const token = (html) => /name="__RequestVerificationToken" type="hidden" value="([^"]+)"/.exec(html)[1];
  await api.post("/Account/Login", {
    form: { Email: "priya.agent@lakeview.example", Password: "DemoAgent#2026", __RequestVerificationToken: token(login) },
  });
  const csrf = token(await (await api.get("/Kb")).text());

  const rows = [];
  async function call(feature, target, url, data) {
    const started = Date.now();
    const res = await api.post(url, { headers: { RequestVerificationToken: csrf }, data });
    const wallMs = Date.now() - started;
    const body = await res.json().catch(() => ({}));
    const row = { feature, target, status: res.status(), wallMs, serverLatencyMs: body.latencyMs, provider: body.provider, model: body.model, found: body.found, interactionId: body.interactionId };
    rows.push(row);
    console.error(JSON.stringify(row));
  }

  for (const id of TICKETS) {
    await call("TicketSummary", `ticket ${id}`, `/api/assistant/tickets/${id}/summary`);
    await call("ReplyDraft", `ticket ${id}`, `/api/assistant/tickets/${id}/reply-draft`);
  }
  for (const q of QUESTIONS) {
    await call("KbAnswer", q, "/api/assistant/kb-answer", { question: q });
  }
  console.log(JSON.stringify({ measuredAt: new Date().toISOString(), base: BASE, rows }, null, 2));
  await api.dispose();
}

main().catch((e) => { console.error(e); process.exit(1); });
