import { notFound } from "next/navigation";
import { MockCheckout } from "@/components/booking/MockCheckout";
import { isMockPayments } from "@/components/booking/lib";
import { bk } from "@/components/booking/strings";

export const dynamic = "force-dynamic"; // reads the environment per request
export const metadata = { title: bk("checkout.title"), robots: { index: false, follow: true } };

/** SPEC 6.5: exists only in the fake profile and with Payments Mock; anywhere else this is a 404. */
export default async function Page({ params }: { params: Promise<{ bookingId: string }> }) {
  if (!isMockPayments()) notFound();
  const { bookingId } = await params;
  return (
    <>
      <h1>{bk("checkout.title")}</h1>
      <MockCheckout bookingId={bookingId} />
    </>
  );
}
