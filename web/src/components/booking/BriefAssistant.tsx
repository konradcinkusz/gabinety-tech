"use client";
import { useRef, useState } from "react";
import { Alert, Button, TextArea } from "@/components/ui";
import { createBookingApi, type BookingApi, type BriefSuggestion } from "./api";
import styles from "./booking.module.css";
import { BRIEF_MIN } from "./lib";
import { bk } from "./strings";

type State =
  | { status: "idle" }
  | { status: "loading" }
  | { status: "ready"; suggestion: BriefSuggestion; proposal: string }
  | { status: "applied" }
  | { status: "failed"; note: "assist.unavailable" | "assist.rateLimited" | "assist.failed" };

const defaultApi = createBookingApi();

/**
 * Optional help with the brief. It only SUGGESTS: the proposal is an editable text, and nothing reaches the brief without the
 * "apply" click. Any failure leaves a short note and the rest of the form untouched. The text is never logged.
 */
export function BriefAssistant({ text, disabled, onApply, api = defaultApi }: { text: string; disabled: boolean; onApply: (proposal: string, problemSlug: string) => void; api?: BookingApi }) {
  const [state, setState] = useState<State>({ status: "idle" });
  const inflight = useRef(false);
  const tooShort = text.trim().length < BRIEF_MIN;

  async function ask() {
    if (inflight.current || tooShort) return;
    inflight.current = true;
    setState({ status: "loading" });
    const r = await api.suggest(text.trim());
    inflight.current = false;
    if (r.ok && r.value && typeof r.value.summary === "string") return setState({ status: "ready", suggestion: r.value, proposal: r.value.summary });
    setState({ status: "failed", note: r.ok ? "assist.failed" : r.status === 503 ? "assist.unavailable" : r.status === 429 ? "assist.rateLimited" : "assist.failed" });
  }

  return (
    <section className={styles.assist} aria-live="polite">
      <Button variant="secondary" onClick={() => void ask()} disabled={disabled || tooShort} loading={state.status === "loading"}>
        {state.status === "loading" ? bk("assist.asking") : bk("assist.ask")}
      </Button>
      <p className={styles.note}>{bk("assist.note")}</p>
      {state.status === "failed" && <Alert variant="info">{bk(state.note)}</Alert>}
      {state.status === "applied" && <Alert variant="success">{bk("assist.applied")}</Alert>}
      {state.status === "ready" && (
        <div className={styles.assist}>
          <label>
            {bk("assist.proposal")}
            <TextArea data-testid="proposal" rows={4} value={state.proposal} onChange={(e) => setState({ ...state, proposal: e.target.value })} />
          </label>
          <p className={styles.note}>{bk("assist.category", { slug: state.suggestion.problemSlug })}</p>
          {state.suggestion.clarifyingQuestions.length > 0 && (
            <>
              <p className={styles.note}>{bk("assist.questions")}</p>
              <ul className={styles.questions}>
                {state.suggestion.clarifyingQuestions.map((q) => <li key={q}>{q}</li>)}
              </ul>
            </>
          )}
          <Button variant="secondary" disabled={disabled || state.proposal.trim() === ""} onClick={() => { onApply(state.proposal.trim(), state.suggestion.problemSlug); setState({ status: "applied" }); }}>
            {bk("assist.apply")}
          </Button>
        </div>
      )}
    </section>
  );
}
