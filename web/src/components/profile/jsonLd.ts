import type { PractitionerProfile } from "./profileApi";
import { safeHttpsUrl } from "./safeUrl";

/** SPEC 6.5: aggregateRating only when the review count is at least 3. */
export const MIN_REVIEWS_FOR_RATING = 3;

export function profileJsonLd(p: PractitionerProfile, url: string): Record<string, unknown> {
  const portfolio = safeHttpsUrl(p.portfolioUrl);
  const ld: Record<string, unknown> = {
    "@context": "https://schema.org",
    "@type": "ProfessionalService",
    name: p.displayName,
    description: p.headline,
    url,
    ...(portfolio || p.githubLogin ? { sameAs: [portfolio, p.githubLogin && `https://github.com/${p.githubLogin}`].filter(Boolean) } : {}),
    makesOffer: p.offerings.map((o) => ({
      "@type": "Offer",
      name: o.title,
      description: o.description,
      price: (o.price.amountMinor / 100).toFixed(2),
      priceCurrency: o.price.currency,
    })),
  };
  if (p.rating && p.rating.count >= MIN_REVIEWS_FOR_RATING) {
    ld["aggregateRating"] = { "@type": "AggregateRating", ratingValue: p.rating.average, reviewCount: p.rating.count, bestRating: 5, worstRating: 1 };
  }
  return ld;
}

/** JSON for a script element: `<` is escaped so no text can close the tag. */
export const serializeJsonLd = (ld: Record<string, unknown>): string => JSON.stringify(ld).replace(/</g, "\\u003c");
