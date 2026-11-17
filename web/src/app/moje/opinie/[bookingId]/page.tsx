import { ReviewForm } from "@/components/reviews/ReviewForm";
import { mr } from "@/components/reviews/strings";

export const metadata = { title: mr("page.formMetaTitle"), robots: { index: false, follow: true } };

export default async function Page({ params }: { params: Promise<{ bookingId: string }> }) {
  const { bookingId } = await params;
  return (
    <>
      <h1>{mr("page.formTitle")}</h1>
      <ReviewForm bookingId={bookingId} />
    </>
  );
}
