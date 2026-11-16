"use client";

import { Button } from "@/components/ui";
import { t } from "@/i18n/pl";

export default function ErrorPage({ retry }: { error: Error & { digest?: string }; retry: () => void; reset?: () => void }) {
  return (
    <>
      <h1>{t("errors.genericTitle")}</h1>
      <p>{t("errors.genericBody")}</p>
      <Button onClick={() => retry()}>{t("errors.retry")}</Button>
    </>
  );
}
