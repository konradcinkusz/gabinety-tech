// Polish text of the slot picker; beside its component because the shared i18n/pl.ts is outside this task's files.
const text = {
  slots: {
    label: "Wolne terminy",
    loading: "Ładowanie wolnych terminów…",
    empty: "Brak wolnych terminów w najbliższym czasie. Zajrzyj później.",
    unavailable: "Nie możemy teraz pokazać terminów. Spróbuj ponownie za chwilę.",
    more: "Pokaż kolejne dni",
    retry: "Spróbuj ponownie",
    timeZone: "Godziny w czasie polskim (Europe/Warsaw).",
    book: "Zarezerwuj {time}",
  },
} as const;

export type SlotTextKey = `slots.${keyof typeof text.slots}`;

export function ss(key: SlotTextKey, params?: Record<string, string | number>): string {
  const raw = text.slots[key.slice("slots.".length) as keyof typeof text.slots] as string;
  return params ? raw.replace(/\{(\w+)\}/g, (whole, n: string) => (n in params ? String(params[n]) : whole)) : raw;
}
