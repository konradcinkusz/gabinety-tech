import { LinkButton } from "@/components/ui";
import { t } from "@/i18n/pl";

export default function NotFound() {
  return (
    <>
      <h1>{t("errors.notFoundTitle")}</h1>
      <p>{t("errors.notFoundBody")}</p>
      <LinkButton href="/">{t("nav.home")}</LinkButton>
    </>
  );
}
