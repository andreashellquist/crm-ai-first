# CRM AI-First

An AI-first CRM: every core workflow (triage, enrichment, drafting, summarization,
next-best-action) is designed around an LLM doing the work, with humans reviewing
and steering rather than doing manual data entry.

This repository is at the very start of its life. The first things checked in are
not application code but the **expert agents and skills** that will guide how the
app gets built — see [`CLAUDE.md`](./CLAUDE.md) for the chosen stack and conventions,
and [`.claude/agents/`](./.claude/agents) / [`.claude/skills/`](./.claude/skills) for
the domain and technical experts available in Claude Code.

## Status

Pre-scaffold. No application code yet — stack and conventions are decided
(see `CLAUDE.md`), and the next step is generating the initial Next.js app skeleton.
