---
name: pipeline-kanban-board
description: Implementation pattern for the deal-pipeline Kanban board (columns = Stages, cards = Deals, drag-and-drop to change stage) — optimistic updates, keyboard-accessible fallback, and revert-on-failure. Load this when building or modifying the pipeline board UI. Pairs with frontend-engineer (UI conventions) and backend-api-engineer (the stage-change server action).
---

# Pipeline Kanban board

## Structure

- One column per `Stage` in the active `Pipeline`, ordered by `Stage.order`.
- One card per `Deal` in that stage, showing company/contact, amount, and age-
  in-stage (a useful staleness signal for sales).
- Column header shows stage name, deal count, and summed `amountCents` —
  reps use this as a lightweight forecast view without leaving the board.

## Drag-and-drop with optimistic update

1. On drop, immediately move the card client-side (`useOptimistic` or local
   state) — don't wait for the server round trip before the card visually moves.
2. Fire the stage-change Server Action (see `ai-tool-calling-pattern` for the
   equivalent AI-invoked path — same underlying mutation, human-invoked here).
3. On success, reconcile with the server response (in case stage `probability`/
   `forecastCategory` side effects changed something else displayed on the card).
4. On failure, revert the card to its original column and surface a toast —
   never leave the board showing a state the server didn't accept, since sales
   reps will trust what's on screen.

## Accessibility fallback

Drag-and-drop must not be the *only* way to move a deal. Every card gets a
"Move to stage…" menu (keyboard/screen-reader operable) that triggers the same
Server Action as a drop would — implement the move as one shared function called
by both the drop handler and the menu action, not two parallel code paths that
can drift.

## Performance

- Paginate/virtualize columns with a large number of deals rather than rendering
  every card in the DOM — a stage with hundreds of open deals is a realistic
  case for an active sales team.
- Debounce/coalesce column count+sum recalculation on rapid successive drags
  rather than refetching the whole board per drop.

## AI touchpoints on this view

- A deal that's been in a stage unusually long (relative to that stage's typical
  duration) is a natural place to surface an AI-suggested next action inline on
  the card — keep it as a dismissible suggestion affordance, not an automatic
  action, consistent with `ai-features-architect`'s review-gated default.
