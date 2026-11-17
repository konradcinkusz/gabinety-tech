export function waitForProfile(): Promise<void>;
export function waitForSeeder(options?: {
  timeoutMs?: number; stableMs?: number; pollMs?: number;
  now?: () => number; list?: () => Promise<string | null>; offset?: (baseUrl: string) => Promise<number | null>;
}): Promise<void>;
export function seederRunning(psArgs: string): boolean;
