import { PractitionerReviews } from "@/components/reviews/PractitionerReviews";
import { mr } from "@/components/reviews/strings";

export const metadata = { title: mr("page.listMetaTitle"), robots: { index: false, follow: true } };

export default function Page() {
  return (
    <>
      <h1>{mr("page.listTitle")}</h1>
      <PractitionerReviews />
    </>
  );
}
