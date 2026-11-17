import type { PractitionerProfile } from "./profileApi";
import { ProfileReviews } from "./ProfileReviews";
import styles from "./profile.module.css";
import { pf } from "./strings";

/** Reviews block: the rating summary is in the server HTML (from the profile); the paged list loads in the browser through the BFF. */
export function ReviewsSlot({ profile: p }: { profile: PractitionerProfile }) {
  const hasRating = p.rating && !p.isNew && p.rating.count > 0;
  return (
    <section data-slot="reviews" className={styles.section} aria-labelledby="reviews">
      <h2 id="reviews">{pf("reviews.title")}</h2>
      <p>{hasRating ? pf("reviews.summary", { average: p.rating!.average.toFixed(1).replace(".", ","), count: p.rating!.count }) : pf("reviews.new")}</p>
      <ProfileReviews practitionerId={p.id} />
    </section>
  );
}
