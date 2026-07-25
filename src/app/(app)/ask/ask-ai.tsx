"use client";

import { useEffect, useRef, useState } from "react";
import Link from "next/link";
import { askQuestionAction, getAskJobStatusAction, type Citation } from "./actions";
import { Button } from "@/components/ui/button";

const POLL_INTERVAL_MS = 1200;
const MAX_POLLS = 25; // ~30s before giving up and telling the user to check back

const TYPE_LABELS: Record<string, string> = {
  call: "Call",
  email: "Email",
  meeting: "Meeting",
  note: "Note",
};

function citationHref(citation: Citation): string | null {
  // A citation's Activity may be linked to more than one of these — Deal is
  // the most specific "where this conversation happened", so it wins.
  if (citation.dealId) return `/pipeline/${citation.dealId}`;
  if (citation.contactId) return `/contacts/${citation.contactId}`;
  if (citation.companyId) return `/companies/${citation.companyId}`;
  return null;
}

// RAG over CRM history — see ai-features-architect: "for cross-record
// questions, retrieve Activities scoped to workspace + relevant
// Company/Contact via a filtered ... search." Grounded answers only, with
// citations linking back to the real activity they came from — never a
// free-floating claim with nothing to check it against.
export function AskAI() {
  const [question, setQuestion] = useState("");
  const [asking, setAsking] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [answer, setAnswer] = useState<{ answer: string; citations: Citation[] } | null>(null);
  const stopPolling = useRef(false);

  useEffect(() => () => {
    stopPolling.current = true;
  }, []);

  async function handleAsk() {
    if (!question.trim()) return;
    setAsking(true);
    setError(null);
    setAnswer(null);
    stopPolling.current = false;

    let jobId: string;
    try {
      ({ jobId } = await askQuestionAction(question));
    } catch {
      setAsking(false);
      setError("Could not ask that question — try again.");
      return;
    }

    for (let attempt = 0; attempt < MAX_POLLS && !stopPolling.current; attempt++) {
      await new Promise((resolve) => setTimeout(resolve, POLL_INTERVAL_MS));
      const result = await getAskJobStatusAction(jobId);

      if (result.status === "succeeded") {
        setAsking(false);
        setAnswer(result.result);
        return;
      }
      if (result.status === "failed") {
        setAsking(false);
        setError(result.error);
        return;
      }
    }

    if (!stopPolling.current) {
      setAsking(false);
      setError("Still working — check back in a moment.");
    }
  }

  return (
    <div className="max-w-2xl space-y-4">
      <div>
        <h1 className="text-xl font-semibold">Ask AI</h1>
        <p className="text-sm text-neutral-500">
          Ask a question about your CRM history — e.g. &ldquo;what have we discussed with Acme about pricing&rdquo;.
          Answers are grounded in your logged activities, with citations back to where they came from.
        </p>
      </div>

      <div className="space-y-2">
        <textarea
          className="w-full rounded border border-neutral-300 px-3 py-2 text-sm"
          rows={3}
          placeholder="Ask a question about a deal, contact, or company…"
          value={question}
          onChange={(event) => setQuestion(event.target.value)}
          disabled={asking}
        />
        <Button type="button" disabled={asking || !question.trim()} onClick={handleAsk}>
          {asking ? "Thinking…" : "Ask"}
        </Button>
      </div>

      {error ? <p className="text-sm text-red-600">{error}</p> : null}

      {answer ? (
        <div className="space-y-3 rounded border border-indigo-200 bg-indigo-50 p-4">
          <div className="flex items-center gap-1 text-xs font-medium text-indigo-700">
            <span aria-hidden>✦</span> AI answer — grounded in your logged activity
          </div>
          <p className="text-sm text-indigo-950">{answer.answer}</p>

          {answer.citations.length > 0 ? (
            <div className="space-y-2 border-t border-indigo-200 pt-3">
              <h2 className="text-xs font-semibold uppercase text-indigo-700">Sources</h2>
              <ul className="space-y-1">
                {answer.citations.map((citation) => {
                  const href = citationHref(citation);
                  const content = (
                    <>
                      <span className="font-medium">{TYPE_LABELS[citation.type] ?? citation.type}</span>
                      {" · "}
                      <span className="text-neutral-600">{new Date(citation.createdAt).toLocaleDateString()}</span>
                      {citation.snippet ? <span className="ml-1 text-neutral-700">— {citation.snippet}</span> : null}
                    </>
                  );
                  return (
                    <li key={citation.activityId} className="text-xs">
                      {href ? (
                        <Link href={href} className="underline hover:text-indigo-950">
                          {content}
                        </Link>
                      ) : (
                        content
                      )}
                    </li>
                  );
                })}
              </ul>
            </div>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}
