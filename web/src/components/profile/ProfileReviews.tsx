"use client";
import { useCallback, useEffect, useState } from "react";
import { Alert, Button, Card, Spinner } from "@/components/ui";
import { createReviewsApi, type ReviewsApi, type ReviewView } from "../reviews/api";
import styles from "./profile.module.css";
import { pf } from "./strings";

const defaultApi = createReviewsApi();
const day = (iso: string): string => new Intl.DateTimeFormat("pl-PL", { dateStyle: "long", timeZone: "Europe/Warsaw" }).format(new Date(iso));

/**
 * Published reviews of one practitioner, newest first, with the owner's reply, read through the BFF (never Reputation directly).
 * Public page: no sign-in, no reply form. Review and reply text are React text children; no HTML is built from them.
 */
export function ProfileReviews({ practitionerId, api = defaultApi }: { practitionerId: string; api?: ReviewsApi }) {
  const [items, setItems] = useState<ReviewView[]>([]);
  const [next, setNext] = useState<string | null>(null);
  const [status, setStatus] = useState<"loading" | "ready" | "failed">("loading");

  const apply = useCallback((cursor: string | undefined, r: Awaited<ReturnType<ReviewsApi["list"]>>) => {
    if (!r.ok) return setStatus("failed");
    setItems((old) => (cursor ? [...old, ...r.value.items] : r.value.items));
    setNext(r.value.nextCursor ?? null);
    setStatus("ready");
  }, []);

  const again = (cursor: string | undefined) => {
    setStatus("loading");
    void api.list(practitionerId, cursor).then((r) => apply(cursor, r));
  };

  // Initial load: the state starts as "loading", so nothing is set before the answer arrives.
  useEffect(() => {
    let alive = true;
    void api.list(practitionerId, undefined).then((r) => alive && apply(undefined, r));
    return () => { alive = false; };
  }, [api, practitionerId, apply]);

  return (
    <div>
      {status === "failed" && (
        <div>
          <Alert variant="warning">{pf("reviews.unavailable")}</Alert>
          <Button variant="secondary" onClick={() => again(next ?? undefined)}>{pf("reviews.retry")}</Button>
        </div>
      )}
      {status === "loading" && <p><Spinner /> {pf("reviews.loading")}</p>}
      {status === "ready" && items.length === 0 && <p>{pf("reviews.empty")}</p>}
      <ul className={styles.reviews}>
        {items.map((r) => (
          <li key={r.id}>
            <Card>
              <p className={styles.meta}>{pf("reviews.reviewer")} · {pf("reviews.rating", { n: r.rating })} · {day(r.createdAt)}</p>
              <p className={styles.description}>{r.text}</p>
              {r.reply && (
                <div className={styles.reviewReply}>
                  <strong>{pf("reviews.replyHeading")}</strong>
                  <p className={styles.description}>{r.reply.text}</p>
                </div>
              )}
            </Card>
          </li>
        ))}
      </ul>
      {status === "ready" && next && <Button variant="secondary" onClick={() => again(next ?? undefined)}>{pf("reviews.more")}</Button>}
    </div>
  );
}
