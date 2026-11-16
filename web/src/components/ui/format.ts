import { t } from "@/i18n/pl";

const NBSP = " ";

/** Formats integer grosz as `250,00 zł`. Integer arithmetic only; thousands and the currency are separated by U+00A0. */
export function formatPln(grosz: number): string {
  if (!Number.isSafeInteger(grosz) || grosz < 0) {
    throw new RangeError("amount must be a non-negative integer number of grosz");
  }
  const whole = Math.floor(grosz / 100);
  const frac = grosz % 100;
  const groups = String(whole).replace(/\B(?=(\d{3})+(?!\d))/g, NBSP);
  return `${groups},${String(frac).padStart(2, "0")}${NBSP}${t("currency.pln")}`;
}
