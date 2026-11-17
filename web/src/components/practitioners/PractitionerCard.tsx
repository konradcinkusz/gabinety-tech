import Link from "next/link";
import { Badge, Card, PriceTag } from "@/components/ui";
import type { BadgeKind, PractitionerCard as CardData } from "@/lib/seo/publicApi";
import styles from "./practitioners.module.css";
import { tp, type TextKey } from "./strings";

const BADGE_TEXT: Record<BadgeKind, TextKey> = {
  email: "card.badgeEmail",
  github: "card.badgeGithub",
  nip: "card.badgeNip",
  approved: "card.badgeApproved",
};

export function PractitionerCard({ practitioner: p }: { practitioner: CardData }) {
  return (
    <Card className={styles.card}>
      <h2 className={styles.name}>
        <Link href={`/gabinet/${p.slug}`}>{p.displayName}</Link>
      </h2>
      <p className={styles.headline}>{p.headline}</p>
      <ul className={styles.meta}>
        {p.badges.map((b) => (
          <li key={b}><Badge variant={b === "approved" ? "success" : "neutral"}>{tp(BADGE_TEXT[b])}</Badge></li>
        ))}
        <li>
          {p.rating && !p.isNew
            ? <Badge>{tp("card.rating", { average: p.rating.average.toFixed(1).replace(".", ","), count: p.rating.count })}</Badge>
            : <Badge>{tp("card.isNew")}</Badge>}
        </li>
      </ul>
      <p className={styles.meta}>
        <span>{tp("card.from")} <PriceTag amount={p.fromPrice.amountMinor} className={styles.price} /></span>
        <span>{p.durations.map((d) => tp("filters.minutes", { n: d })).join(", ")}</span>
      </p>
    </Card>
  );
}
