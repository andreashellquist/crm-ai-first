# CRM AI-First

An AI-first CRM: every core workflow (triage, enrichment, drafting, summarization,
next-best-action) is designed around an LLM doing the work, with humans reviewing
and steering rather than doing manual data entry.

See [`CLAUDE.md`](./CLAUDE.md) for the chosen stack and conventions,
[`docs/PRODUCT_SCOPE.md`](./docs/PRODUCT_SCOPE.md) for the full functional/
non-functional scope and phased roadmap, and
[`.claude/agents/`](./.claude/agents) / [`.claude/skills/`](./.claude/skills) for
the domain and technical experts available in Claude Code.

## Status

Phase 0 walking skeleton: sign in → create a contact → see/move a deal on the
pipeline board, backed by a split Next.js frontend / ASP.NET Core backend (see
`CLAUDE.md`'s "Chosen stack"). Phase 1 has started: a Postgres-backed job queue
takes deal scoring (the first AI feature) off the request path. Auth is a
dev-only email+password login for now — see `auth-security-expert` for adding
real OAuth providers. Most of Phase 0-4 in `docs/PRODUCT_SCOPE.md` is still
ahead.

## Getting started

Requires Node 22+, pnpm, .NET 10 SDK, and a local PostgreSQL 16 server.

```bash
cp .env.example .env   # adjust API_BASE_URL if needed

# Backend
cd backend/CrmApi
dotnet run seed         # applies EF Core migrations + creates a demo
                         # workspace + demo@example.com / password123
dotnet run               # serves the API on http://localhost:5194

# Frontend (separate terminal, repo root)
pnpm install
pnpm dev
```

Then open http://localhost:3000 and sign in with the seeded demo credentials.

AI features (e.g. deal scoring) run through a Postgres-backed job queue, not
inline in the request — `backend/CrmApi/Services/JobWorker.cs` runs in-process
as an ASP.NET Core `BackgroundService`, so it's already polling as soon as the
API is running; there's no separate worker process to start locally. Set
`Anthropic:ApiKey` in `backend/CrmApi/appsettings.Development.json` for scoring
to actually succeed; without it, jobs retry with backoff and then fail visibly
in the UI, which is itself a tested path (see
`backend/CrmApi.Tests/DealScoringServiceTests.cs`). This persistent-loop shape
doesn't fit a serverless deployment target for the *API* itself — see
CLAUDE.md's "Background work" note.

## Testing

```bash
# Backend — requires a running Postgres reachable via the connection string
# in backend/CrmApi/appsettings.Test.json (or override with TEST_DATABASE_URL)
cd backend && dotnet test CrmApi.slnx

# Frontend
pnpm lint
npx tsc --noEmit
```

CI (`.github/workflows/ci.yml`) runs both on every push/PR against a Postgres
service container — see `qa-test-engineer` for the test project's structure
and conventions.

### AI eval harness

`backend/CrmApi.Eval` runs a small fixed set of representative deal-scoring
scenarios (hot lead, stalled deal, early-stage-but-promising, recent
objection) against the **real** Claude API and prints each score/rationale
for human review, plus a soft PASS/WARN against an expected score band per
scenario. Deliberately not part of `dotnet test`/CI — it costs real API
calls and its real output (rationale quality) needs a human read, not just
an assertion. Run it before merging any change to the scoring prompt or tool
schema:

```bash
ANTHROPIC_API_KEY=sk-... dotnet run --project backend/CrmApi.Eval
```

Seeds and tears down its own throwaway workspace in `crm_dotnet_eval`
(created automatically on first run; override with `EVAL_DATABASE_URL`).
