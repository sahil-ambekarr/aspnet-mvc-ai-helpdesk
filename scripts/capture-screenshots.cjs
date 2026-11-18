// Captures README screenshots from the running app with Playwright.
// Usage: start the app (dotnet run, port 4110), then:
//   npm i playwright && npx playwright install chromium
//   node scripts/capture-screenshots.cjs
// AI calls go to whatever LLM_PROVIDER the app was started with; the script does not fake output.
const { chromium } = require("playwright");
const path = require("path");

const BASE = process.env.BASE_URL || "http://localhost:4110";
const OUT = path.join(__dirname, "..", "docs", "screenshots");
const AI_TIMEOUT = 6 * 60 * 1000; // CPU-only Ollama can be slow

async function login(page, email, password) {
  await page.goto(`${BASE}/Account/Login`);
  await page.fill("#Email", email);
  await page.fill("#Password", password);
  await Promise.all([page.waitForURL(`${BASE}/`), page.click("button[type=submit]")]);
}

async function waitForAi(page) {
  await page.waitForSelector("[data-ai-output]:not(.d-none), [data-ai-error]:not(.d-none)", { timeout: AI_TIMEOUT });
  const error = await page.locator("[data-ai-error]:not(.d-none)").count();
  const meta = await page.locator("[data-ai-meta]").innerText();
  console.log(error ? "AI error shown" : `AI ok: ${meta}`);
}

(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1360, height: 900 } });
  await login(page, "priya.agent@lakeview.example", "DemoAgent#2026");

  await page.screenshot({ path: path.join(OUT, "dashboard.png"), fullPage: true });
  await page.goto(`${BASE}/Tickets`);
  await page.screenshot({ path: path.join(OUT, "ticket-list.png"), fullPage: true });

  await page.goto(`${BASE}/Tickets/Details/1`);
  await page.click("[data-ai-action=summary]");
  await waitForAi(page);
  await page.screenshot({ path: path.join(OUT, "ticket-summary.png"), fullPage: true });

  await page.click("[data-ai-action=reply-draft]");
  await waitForAi(page);
  await page.click("[data-ai-insert]");
  await page.screenshot({ path: path.join(OUT, "ticket-reply-draft.png"), fullPage: true });

  await page.goto(`${BASE}/Kb`);
  await page.fill("#ai-question", "My VPN keeps dropping every hour when I work from home. What should I do?");
  await page.click("[data-ai-kb-form] button[type=submit]");
  await waitForAi(page);
  await page.screenshot({ path: path.join(OUT, "kb-answer.png"), fullPage: true });

  const mobile = await browser.newPage({ viewport: { width: 390, height: 844 } });
  await login(mobile, "dana@lakeview.example", "DemoUser#2026");
  await mobile.screenshot({ path: path.join(OUT, "mobile-requester-dashboard.png"), fullPage: true });

  await browser.close();
})().catch((e) => {
  console.error(e);
  process.exit(1);
});
