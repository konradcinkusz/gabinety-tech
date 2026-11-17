import { MyBookings } from "@/components/bookings/MyBookings";
import { STATUSES } from "@/components/bookings/lib";
import { mb } from "@/components/bookings/strings";

export const metadata = { title: mb("page.metaTitle"), robots: { index: false, follow: true } };

export default async function Page({ searchParams }: { searchParams: Promise<Record<string, string | string[] | undefined>> }) {
  const sp = await searchParams;
  const one = (v: string | string[] | undefined) => (Array.isArray(v) ? v[0] : v);
  const status = one(sp.status);
  return (
    <>
      <h1>{mb("page.title")}</h1>
      <MyBookings
        initialRole={one(sp.role) === "practitioner" ? "practitioner" : "client"}
        initialStatus={status && (STATUSES as readonly string[]).includes(status) ? status : ""}
      />
    </>
  );
}
