import { Card } from "@/components/ui";
import type { SuspiciousPair } from "./api";
import { sp } from "./strings";

/** Ids and counts as plain text: React escapes them, and there is no review text to show. */
export function PairList({ items }: { items: SuspiciousPair[] }) {
  if (items.length === 0) return <p>{sp("empty")}</p>;
  return (
    <ul>
      {items.map((p) => (
        <li key={p.pairKey}>
          <Card>
            <dl>
              <dt>{sp("fact.client")}</dt>
              <dd>{p.clientUserId}</dd>
              <dt>{sp("fact.practitioner")}</dt>
              <dd>{p.practitionerId}</dd>
              <dt>{sp("fact.bookings")}</dt>
              <dd>{p.bookings}</dd>
              <dt>{sp("fact.reviews")}</dt>
              <dd>{p.reviews}</dd>
            </dl>
          </Card>
        </li>
      ))}
    </ul>
  );
}
