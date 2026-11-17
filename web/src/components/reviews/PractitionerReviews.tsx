"use client";
import { useCallback, useEffect, useState } from "react";
import { Alert, Button, Card, Spinner } from "@/components/ui";
import { createReviewsApi, type Failure, type ReviewsApi, type ReviewView } from "./api";
import { failureKey, ratingLabel, REPLY_ENABLED, toSignIn } from "./lib";
import { ReplyForm } from "./ReplyForm";
import styles from "./reviews.module.css";
import { mr } from "./strings";

const defaultApi = createReviewsApi();
type State = { kind: "loading" } | { kind: "noProfile" } | { kind: "error"; failure: Failure } | { kind: "ready"; practitionerId: string };

const day = (iso: string): string => new Intl.DateTimeFormat("pl-PL", { dateStyle: "long", timeZone: "Europe/Warsaw" }).format(new Date(iso));

/**
 * The signed-in practitioner's own published reviews, newest first. Every review and reply is rendered as a React text child: no HTML is ever built
 * from it. The reply form appears only for a review without a reply and only while `replyEnabled` (REPLY_ENABLED, see lib.ts) is on.
 */
export function PractitionerReviews({ api = defaultApi, replyEnabled = REPLY_ENABLED }: { api?: ReviewsApi; replyEnabled?: boolean }) {
  const [state, setState] = useState<State>({ kind: "loading" });
  const [items, setItems] = useState<ReviewView[]>([]);
  const [next, setNext] = useState<string | null>(null);
  const [more, setMore] = useState<Failure | null>(null);

  const load = useCallback(async (practitionerId: string, cursor: string | undefined, replace: boolean) => {
    const r = await api.list(practitionerId, cursor);
    if (r.ok) {
      setItems((old) => (replace ? r.value.items : [...old, ...r.value.items]));
      setNext(r.value.nextCursor ?? null);
      setMore(null);
      return true;
    }
    if (r.status === 401) toSignIn();
    else setMore(r);
    return false;
  }, [api]);

  useEffect(() => {
    let live = true;
    void (async () => {
      const me = await api.me();
      if (!live) return;
      if (!me.ok) return me.status === 401 ? toSignIn() : setState({ kind: "error", failure: me });
      if (!me.value.practitionerId) return setState({ kind: "noProfile" });
      const id = me.value.practitionerId;
      setState({ kind: "ready", practitionerId: id });
      await load(id, undefined, true);
    })();
    return () => { live = false; };
  }, [api, load]);

  const replied = (id: string, text: string) =>
    setItems((old) => old.map((r) => (r.id === id ? { ...r, reply: { text, repliedAt: new Date().toISOString() } } : r)));

  if (state.kind === "loading") return <p><Spinner /> {mr("list.loading")}</p>;
  if (state.kind === "noProfile") return <Alert variant="info">{mr("list.noProfile")}</Alert>;
  if (state.kind === "error") return <Alert variant="danger">{mr(failureKey(state.failure))}</Alert>;
  return (
    <div className={styles.layout}>
      {more && <Alert variant="danger">{mr(failureKey(more))}</Alert>}
      {items.length === 0 && !more && <p>{mr("list.empty")}</p>}
      <ul className={styles.list}>
        {items.map((r) => (
          <li key={r.id}>
            <Card>
              <p className={styles.meta}>{mr("list.reviewer")} · {ratingLabel(r.rating)} · {day(r.createdAt)}</p>
              <p className={styles.text}>{r.text}</p>
              {r.reply ? (
                <div className={styles.reply}>
                  <strong>{mr("list.replyHeading")}</strong>
                  <p className={styles.text}>{r.reply.text}</p>
                </div>
              ) : (
                replyEnabled && <ReplyForm reviewId={r.id} api={api} onReplied={(t) => replied(r.id, t)} />
              )}
            </Card>
          </li>
        ))}
      </ul>
      {next && state.kind === "ready" && <Button variant="secondary" onClick={() => void load(state.practitionerId, next, false)}>{mr("list.more")}</Button>}
    </div>
  );
}
