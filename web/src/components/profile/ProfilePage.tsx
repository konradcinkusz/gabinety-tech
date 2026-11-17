import { Badge, Card, LinkButton, PriceTag } from "@/components/ui";
import { SlotPicker } from "@/components/slots/SlotPicker";
import { profileJsonLd, serializeJsonLd } from "./jsonLd";
import type { BadgeKind } from "../../lib/seo/publicApi";
import type { PractitionerProfile } from "./profileApi";
import styles from "./profile.module.css";
import { ReviewsSlot } from "./ReviewsSlot";
import { pf, type ProfileTextKey } from "./strings";

const BADGES: Record<BadgeKind, [ProfileTextKey, ProfileTextKey]> = {
  email: ["badge.email", "badge.emailInfo"],
  github: ["badge.github", "badge.githubInfo"],
  nip: ["badge.nip", "badge.nipInfo"],
  approved: ["badge.approved", "badge.approvedInfo"],
};
const dateFmt = new Intl.DateTimeFormat("pl-PL", { timeZone: "Europe/Warsaw", month: "long", year: "numeric" });

/** Server component: everything except the slot lists is in the HTML without scripts. */
export function ProfilePage({ profile: p, url }: { profile: PractitionerProfile; url: string }) {
  return (
    <>
      <script type="application/ld+json" dangerouslySetInnerHTML={{ __html: serializeJsonLd(profileJsonLd(p, url)) }} />
      <header className={styles.header}>
        <h1>{p.displayName}</h1>
        <p className={styles.headline}>{p.headline}</p>
        <ul className={styles.badges} aria-label={pf("page.badgesLabel")}>
          {p.badges.map((b) => (
            <li key={b}>
              <Badge variant={b === "approved" ? "success" : "neutral"} title={pf(BADGES[b][1])} aria-label={`${pf(BADGES[b][0])}. ${pf(BADGES[b][1])}`}>{pf(BADGES[b][0])}</Badge>
            </li>
          ))}
          {p.rating && !p.isNew ? (
            <li><Badge>{pf("page.rating", { average: p.rating.average.toFixed(1).replace(".", ","), count: p.rating.count })}</Badge></li>
          ) : (
            <li><Badge>{pf("page.isNew")}</Badge></li>
          )}
        </ul>
        <ul className={styles.meta}>
          <li>{pf("page.completed", { count: p.completedCount })}</li>
          <li>{pf("page.memberSince", { date: dateFmt.format(new Date(p.memberSince)) })}</li>
        </ul>
        {(p.portfolioUrl || p.githubLogin) && (
          <div className={styles.links}>
            {p.portfolioUrl && <a href={p.portfolioUrl} rel="noopener nofollow ugc">{pf("page.portfolio")}</a>}
            {p.githubLogin && <a href={`https://github.com/${encodeURIComponent(p.githubLogin)}`} rel="noopener nofollow">{pf("page.github")}</a>}
          </div>
        )}
      </header>

      <section className={styles.section} aria-labelledby="about">
        <h2 id="about">{pf("page.aboutTitle")}</h2>
        <p className={styles.bio}>{p.bio}</p>
      </section>

      <section className={styles.section} aria-labelledby="offerings">
        <h2 id="offerings">{pf("page.offeringsTitle")}</h2>
        {p.offerings.length === 0 ? (
          <p>{pf("page.noOfferings")}</p>
        ) : (
          <ul className={styles.offerings}>
            {p.offerings.map((o) => (
              <li key={o.id}>
                <Card className={styles.offering}>
                  <h3 className={styles.offeringTitle}>{o.title}</h3>
                  <p className={styles.description}>{o.description}</p>
                  <p className={styles.meta}>
                    <span>{pf("page.duration", { n: o.durationMinutes })}</span>
                    <PriceTag amount={o.price.amountMinor} className={styles.price} />
                  </p>
                  {/* Works without JavaScript; the picker below adds concrete start times when scripts run. */}
                  <LinkButton href={`/rezerwacja/${o.id}`} aria-label={pf("page.bookOffering", { title: o.title })}>{pf("page.book")}</LinkButton>
                  <SlotPicker practitionerId={p.id} offeringId={o.id} />
                </Card>
              </li>
            ))}
          </ul>
        )}
      </section>

      <ReviewsSlot />
    </>
  );
}
