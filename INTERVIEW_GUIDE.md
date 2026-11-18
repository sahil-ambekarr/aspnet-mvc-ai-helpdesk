# Interview guide: aspnet-mvc-ai-helpdesk

A reference sheet for demos and interviews.

## 1. How it works, in simple words

- It is a normal ASP.NET Core 8 MVC helpdesk: Identity login, two roles (Agent, Requester), tickets with comments, a knowledge base, and a dashboard. EF Core talks to SQL Server in production and SQLite locally and in tests.
- An "AI assistant" card sits on the ticket and KB pages. It calls a small JSON API (`/api/assistant/*`) with the antiforgery token in a header.
- The API calls `AssistantService`, which builds a prompt, sends it to whichever `ILlmProvider` is configured (Ollama, Claude or a deterministic mock), times the call and writes a row to the `AiInteractions` audit table.
- KB answers are retrieval first: BM25 keyword search picks the top 3 articles. If nothing scores above the threshold, the app says "not found" and never calls the model. Otherwise the model must answer only from those articles and cite them, and the UI links only the cited ones.
- Ticket summary and reply draft are agent-only. The draft goes into the reply box for the agent to edit; nothing is ever sent automatically.

## 2. Key files to read first, in order

1. `src/Helpdesk.Web/Program.cs`: DI, Identity, the fallback "must be signed in" policy, antiforgery header, routes.
2. `src/Helpdesk.Web/Services/Ai/LlmContracts.cs`: `ILlmProvider`, request/result records, options.
3. `src/Helpdesk.Web/Services/Ai/LlmServiceCollectionExtensions.cs`: provider selection, typed HttpClients, resilience pipeline (total timeout, retry with backoff, per-attempt timeout).
4. `src/Helpdesk.Web/Services/Ai/AssistantService.cs`: the three features and the audit logging.
5. `src/Helpdesk.Web/Services/Ai/PromptBuilder.cs`: prompts, delimiters for untrusted text, output cleanup.
6. `src/Helpdesk.Web/Services/Kb/KbSearchService.cs`: BM25 ranking.
7. `src/Helpdesk.Web/Services/TicketService.cs`: `VisibleTo()` is the single place where requester isolation happens.
8. `src/Helpdesk.Web/Controllers/Api/AssistantController.cs` and `wwwroot/js/assistant.js`: the widget round trip.
9. `tests/Helpdesk.Tests/IntegrationTests.cs`: the whole stack booted with WebApplicationFactory.

## 3. Likely questions and answers

**Why an `ILlmProvider` interface instead of calling Ollama directly?**
The business code should not care which model runs. Swapping is a config change (`LLM_PROVIDER`). It also gives tests and CI a deterministic `MockProvider`, so 71 tests run with no model, key or network.

**How do you stop a requester from reading someone else's ticket?**
Every ticket read goes through `TicketService.VisibleTo(user)`, which adds `RequesterId == userId` for non-agents. Details, comments and the AI endpoints all use it. A miss returns 404, not 403, so ticket ids cannot be probed. The integration test `Requester_cannot_see_another_requesters_ticket` checks this through real HTTP with real login cookies.

**What about prompt injection? Ticket text is written by users.**
Three layers. Untrusted text sits inside `<ticket_data>` / `<question>` / `<kb_articles>` blocks, and any tag in the text that looks like one of those delimiters is rewritten so it cannot close its block. Each system prompt says the delimited text is data and must not be followed. Most importantly, the blast radius is small: the model has no tools, cannot change data, and its output only reaches the agent as a draft. Ticket #19 in the seed data is a deliberate injection attempt for demos. Delimiting reduces the risk; it does not remove it.

**Why BM25 and not embeddings?**
12 articles, keyword-heavy IT topics, and I wanted an explainable, deterministic ranker that is easy to unit test. It is about 150 lines with no extra service. Embeddings (`nomic-embed-text` is available locally) are on the roadmap for when the KB grows or questions get more paraphrased.

**How are timeouts and retries handled?**
`Microsoft.Extensions.Http.Resilience` on the typed HttpClient: an outer timeout for the whole call (300 s by default, because CPU-only Ollama is slow), exponential backoff with jitter for transient failures (5xx, 408, 429, network errors), and a per-attempt timeout. The Anthropic SDK gets the same HttpClient with its own retries off, so nothing retries twice. Tests cover retry-then-succeed, give-up-after-N, and the attempt timeout.

**What is in the audit table and why?**
Type, ticket id, provider, model, latency, input/output tokens when the provider reports them, success/error, a clipped copy of input and output, user and time. It is written for failures too. It answers "who used the AI on which ticket and what did it say", and it is where the latency numbers in the README come from.

**Why SQLite locally and SQL Server in production? Is that risky?**
It keeps the repo runnable with only the .NET SDK. The risk is provider differences. I hit one: `string.Contains` becomes a case-sensitive `instr()` on SQLite, so search uses `EF.Functions.Like` with escaping instead. Migrations are generated for SQL Server; SQLite uses `EnsureCreated`. SQL Server itself was not run on this machine, so that path is not verified.

**What did the first runs with llama3.2 show?**
The 3B model wrapped KB answers in `<answer>` tags, greeted with a placeholder (`<Dana Whitfield>`), and invented a client version number in a reply draft. I tightened the prompts (explicit first name, "do not invent version numbers"), strip stray tags, and only link articles the answer actually cites. It still makes small factual slips, which is why every output is a draft the agent reviews.

**How is the Claude provider tested without an API key?**
It uses the official Anthropic .NET SDK with the app's typed HttpClient injected, so a fake `HttpMessageHandler` can return canned Messages API JSON. Tests check the request path, model, max tokens, system prompt, auth header, usage parsing, error mapping and the refusal stop reason. It has not been run against the live API.

**Why a JSON API plus vanilla JS instead of a full SPA?**
This is an existing-MVC-app scenario: add AI without rewriting the front end. One partial view, one 130-line script, no build step. Output is set with `textContent`, so model output cannot inject HTML.

## 4. Three small improvements to make next

1. **Streaming responses.** Ollama and the Anthropic SDK both stream. Start in `OllamaProvider.CompleteAsync` (set `stream: true` and read NDJSON lines), add an `IAsyncEnumerable<string>` method to `ILlmProvider`, and switch the widget to `fetch` with a `ReadableStream` reader.
2. **Rate limit the assistant API per user.** Use `builder.Services.AddRateLimiter` in `Program.cs` with a fixed-window policy partitioned by user id, and put `[EnableRateLimiting("assistant")]` on `AssistantController`. Add an integration test that expects 429.
3. **Feedback on AI output.** Add `Helpful` (bool?) to `AiInteraction`, a `POST /api/assistant/interactions/{id}/feedback` action, and thumbs up/down buttons in `_AssistantPanel.cshtml` (the interaction id is already returned). Show the helpful rate on the dashboard next to the usage counts.
