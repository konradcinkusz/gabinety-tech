"use client";
import { useState } from "react";
import { Alert, Button, Field, TextArea } from "@/components/ui";
import { createReviewsApi, type ReviewsApi } from "./api";
import { failureKey, REPLY_MAX, REPLY_MIN, toSignIn } from "./lib";
import styles from "./reviews.module.css";
import { mr } from "./strings";

const defaultApi = createReviewsApi();

/** The practitioner's one reply to one review. After a success, or a 409 `already_replied`, the form is replaced and never comes back. */
export function ReplyForm({ reviewId, onReplied, api = defaultApi }: { reviewId: string; onReplied: (text: string) => void; api?: ReviewsApi }) {
  const [text, setText] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  const [already, setAlready] = useState(false);
  const [sent, setSent] = useState(false);

  async function submit() {
    const trimmed = text.trim();
    if (trimmed.length < REPLY_MIN) return setError(mr("reply.tooShort"));
    setError(null);
    setPending(true);
    const r = await api.reply(reviewId, trimmed);
    setPending(false);
    if (r.ok) {
      setSent(true);
      return onReplied(trimmed);
    }
    if (r.status === 401) return toSignIn();
    if (r.code === "already_replied") return setAlready(true);
    setError(mr(failureKey(r)));
  }

  
  if (sent) return null;
  if (already) return <p>{mr("reply.already")}</p>;
  return (
    <form className={styles.form} onSubmit={(e) => { e.preventDefault(); void submit(); }} noValidate>
      <Field label={mr("reply.label")} hint={mr("reply.hint")} error={error ?? undefined}>
        {(p) => <TextArea {...p} rows={3} maxLength={REPLY_MAX} showCount value={text} onChange={(e) => setText(e.target.value)} />}
      </Field>
      <Button type="submit" loading={pending}>{mr("reply.submit")}</Button>
    </form>
  );
}
