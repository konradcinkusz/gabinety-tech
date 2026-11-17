import { BookingDetail } from "@/components/bookings/BookingDetail";
import { mb } from "@/components/bookings/strings";

export const metadata = { title: mb("page.detailMetaTitle"), robots: { index: false, follow: true } };

export default async function Page({ params, searchParams }: { params: Promise<{ id: string }>; searchParams: Promise<{ role?: string | string[] }> }) {
  const [{ id }, { role }] = await Promise.all([params, searchParams]);
  return (
    <>
      <h1>{mb("page.detailTitle")}</h1>
      <BookingDetail bookingId={id} role={role === "practitioner" ? "practitioner" : "client"} />
    </>
  );
}
