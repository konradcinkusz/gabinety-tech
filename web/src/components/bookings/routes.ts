// The one place that knows where the reviews flow lives (built by T-07.09 under web/src/app/moje/opinie).
export const bookingsHref = "/moje/rezerwacje";
export const reviewHref = (bookingId: string): string => `/moje/opinie/${encodeURIComponent(bookingId)}`;
export const bookingHref = (bookingId: string, role: Role): string => `${bookingsHref}/${encodeURIComponent(bookingId)}?role=${role}`;
export type Role = "client" | "practitioner";
