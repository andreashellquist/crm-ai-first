---
name: lead-deal-scoring
description: Prompt and data-shape pattern for AI-driven lead/deal scoring (0-100 score plus a human-readable rationale via structured tool output). Load this when implementing or changing the scoring feature, so scores stay explainable and grounded in real CRM signals instead of a black-box number.
---

# Lead / deal scoring

## Principle

A score with no rationale won't be trusted by sales reps and can't be debugged
when it's wrong. Always return score + rationale + the signals it was based on,
via structured tool output — never a bare number parsed from prose. See
`ai-features-architect` for the broader AI-feature principles this follows.

## Input signals to assemble before calling the model

- **Engagement recency**: days since last inbound/outbound Activity.
- **Engagement volume**: Activity count in the last 30/90 days.
- **Deal specifics** (deal scoring only): `amountCents`, days in current stage vs.
  that stage's typical duration, `forecastCategory`.
- **Firmographic fit** (lead scoring): company size/domain if known — only if
  populated; don't penalize a score for missing optional fields.
- **Recent activity text**: last 3-5 Activity bodies (truncated), which is what
  lets the model produce a rationale that references what actually happened
  rather than only the numeric signals.

## Output contract (tool schema)

```ts
const ScoreResult = z.object({
  score: z.number().min(0).max(100),
  rationale: z.string().describe("1-2 sentences a sales rep would find useful"),
  signals: z.array(z.string()).describe("Short bullet-style factors that drove the score"),
});
```

## Prompt shape

- System prompt fixes the rubric explicitly (what high vs. low means for *this*
  score type — lead vs. deal scoring are different rubrics, don't share one
  prompt for both) so scores are comparable across records rather than each call
  reinventing scale.
- Feed the assembled signals as structured data (not just prose) alongside the
  recent activity text, so the model isn't left to infer engagement recency from
  reading dates in free text.
- Model choice: Haiku is normally sufficient here — this is a high-volume,
  narrowly-scoped classification task, not open-ended reasoning; reserve
  Sonnet/Opus for cases where rationale quality clearly suffers on Haiku.

## Recompute triggers

Recompute a score on new relevant Activity or stage change, not on a fixed
schedule alone — a score that's stale by days after a hot email exchange defeats
the point. Cache the last score + rationale on the record so the UI has
something to show instantly while a recompute (if triggered) runs in the
background.

## Testing

Per `qa-test-engineer`: mock the LLM response in unit/integration tests (assert
the signals-assembly logic is correct and the tool-output contract is enforced);
keep an eval set of a few representative contacts/deals (cold lead, hot lead,
stalled deal, about-to-close deal) to sanity-check rationale quality whenever the
prompt changes.
