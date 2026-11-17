"use client";
import Link from "next/link";
import { useState } from "react";
import { Alert, Button, Card, Field, TextArea } from "@/components/ui";
import { bookingsHref } from "@/components/bookings/routes";
import { createReviewsApi, type Failure, type ReviewsApi } from "./api";
import { failureKey, isPublished, RATING_MAX, RATING_MIN, TEXT_MAX, TEXT_MIN, toSignIn } from "./lib";
import styles from "./reviews.module.css";
import { mr } from "./strings";

const defaultApi = createReviewsApi();
type Done = { kind: "form" } | { kind: "sent"; published: boolean } | { kind: "already" } | { kind: "notReviewable" };
const RATINGS = Array.from({ length: RATING_MAX - RATING_MIN + 1 }, (_, i) => RATING_MIN + i);

/**
 * Write a review for one booking. The page does not decide whether the booking is reviewable (the server's `canReview` led here and Reputation
 * decides again); it validates only the shape (rating 1 to 5, text 20 to 2000) and maps the server's refusals to Polish by code. The confirmation
 * shows the state the API returned: only an explicit `Published` is announced as published, `Held` is announced as waiting for a check.
 * The text is user free text: it is never logged and never shown back.
 */
export function ReviewForm({ bookingId, api = defaultApi }: { bookingId: string; api?: ReviewsApi }) {
  const [rating, setRating] = useState<number | null>(null);
  const [text, setText] = useState("");
  const [local, setLocal] = useState<{ rating?: string; text?: string }>({});
  const [failure, setFailure] = useState<Failure | null>(null);
  const [pending, setPending] = useState(false);
  const [done, setDone] = useState<Done>({ kind: "form" });

  async function submit() {
    const trimmed = text.trim();
    const errors: { rating?: string; text?: string } = {};
    if (rating === null) errors.rating = mr("form.ratingRequired");
    if (trimmed.length < TEXT_MIN) errors.text = mr("form.textTooShort");
    else if (trimmed.length > TEXT_MAX) errors.text = mr("form.textTooLong");
    setLocal(errors);
    setFailure(null);
    if (errors.rating || errors.text || rating === null) return;
    setPending(true);
    const r = await api.create(bookingId, rating, trimmed);
    setPending(false);
    if (r.ok) return setDone({ kind: "sent", published: isPublished(String(r.value.status)) });
    if (r.status === 401) return toSignIn();
    if (r.code === "already_reviewed") return setDone({ kind: "already" });
    if (r.code === "not_reviewable") return setDone({ kind: "notReviewable" });
    setFailure(r);
  }

  const back = <Link href={bookingsHref}>{mr("form.back")}</Link>;
  if (done.kind === "sent")
    return (
      <Card>
        <h2>{mr("done.title")}</h2>
        <Alert variant={done.published ? "success" : "info"}>{mr(done.published ? "done.published" : "done.held")}</Alert>
        {back}
      </Card>
    );
  if (done.kind === "already" || done.kind === "notReviewable")
    return (
      <Card>
        <h2>{mr(done.kind === "already" ? "already.title" : "notReviewable.title")}</h2>
        <p>{mr(done.kind === "already" ? "already.body" : "notReviewable.body")}</p>
        {back}
      </Card>
    );

  const textError = local.text ?? (failure && "text" in failure.errors ? mr("form.textInvalid") : undefined);
  return (
    <div className={styles.layout}>
      <Card>
        <form className={styles.form} onSubmit={(e) => { e.preventDefault(); void submit(); }} noValidate>
          <p>{mr("form.intro")}</p>
          <fieldset className={styles.rating}>
            <legend>{mr("form.ratingLegend")}</legend>
            <div className={styles.stars}>
              {RATINGS.map((n) => (
                <label key={n} className={styles.star}>
                  <input type="radio" name="rating" value={n} checked={rating === n} onChange={() => setRating(n)} />
                  {mr("form.ratingOption", { n })}
                </label>
              ))}
            </div>
            {local.rating && <p role="alert" className={styles.error}>{local.rating}</p>}
          </fieldset>
          <Field label={mr("form.textLabel")} hint={mr("form.textHint")} error={textError}>
            {(p) => <TextArea {...p} rows={6} maxLength={TEXT_MAX} showCount value={text} onChange={(e) => setText(e.target.value)} />}
          </Field>
          {failure && !("text" in failure.errors) && <Alert variant="danger">{mr(failureKey(failure))}</Alert>}
          <div className={styles.actions}>
            <Button type="submit" loading={pending}>{mr("form.submit")}</Button>
            {back}
          </div>
        </form>
      </Card>
    </div>
  );
}
