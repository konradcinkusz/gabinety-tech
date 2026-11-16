import type { HTMLAttributes } from "react";
import { formatPln } from "./format";

export function PriceTag({ amount, ...rest }: { amount: number } & HTMLAttributes<HTMLSpanElement>) {
  return <span {...rest}>{formatPln(amount)}</span>;
}
