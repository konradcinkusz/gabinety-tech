// Shape of the Reputation admin suspicious-pairs API (T-10.03), declared by hand: the generated types of T-07.01 do not know /api/v1/admin.
// A pair is a client and a practitioner with more than two paid bookings and only five-star reviews. The list carries ids and counts, never review text.
export type SuspiciousPair = {
  pairKey: string;
  clientUserId: string;
  practitionerId: string;
  bookings: number;
  reviews: number;
};
export type SuspiciousPairPage = { items: SuspiciousPair[]; nextCursor?: string | null };

export const PAGE_LIMIT = 20;
