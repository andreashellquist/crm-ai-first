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
pipeline board, backed by Postgres/Prisma. Auth is a dev-only email+password
Credentials login for now — see `auth-security-expert` for adding real OAuth
providers. Most of Phase 0-4 in `docs/PRODUCT_SCOPE.md` is still ahead.

## Getting started

Requires Node 22+, pnpm, and a local PostgreSQL 16 server.

```bash
cp .env.example .env   # adjust DATABASE_URL if needed
pnpm install
pnpm db:migrate         # applies prisma/migrations
pnpm db:seed             # creates a demo workspace + demo@example.com / password123
pnpm dev
```

Then open http://localhost:3000 and sign in with the seeded demo credentials.

AI features (e.g. deal scoring) run through a Postgres-backed job queue, not
inline in the request — run the worker alongside the app to process them:

```bash
pnpm worker
```

Set `ANTHROPIC_API_KEY` in `.env` for scoring to actually succeed; without it,
jobs retry with backoff and then fail visibly in the UI, which is itself a
tested path (see `src/lib/ai/score-deal.ts`). `pnpm worker` is a persistent
loop for local dev / a dedicated process — it does not run on Vercel's
serverless functions. A production deployment there needs a Vercel
Cron-triggered API route calling `processPendingJobs()` once per invocation
instead (see `src/lib/jobs/worker.ts`).
