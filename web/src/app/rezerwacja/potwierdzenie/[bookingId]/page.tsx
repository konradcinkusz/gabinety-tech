import { Confirmation } from "@/components/booking/Confirmation";
import { bk } from "@/components/booking/strings";

export const metadata = { title: bk("confirmation.title"), robots: { index: false, follow: true } };

export default async function Page({ params }: { params: Promise<{ bookingId: string }> }) {
  const { bookingId } = await params;
  return (
    <>
      <h1>{bk("confirmation.title")}</h1>
      <Confirmation bookingId={bookingId} />
    </>
  );
}
