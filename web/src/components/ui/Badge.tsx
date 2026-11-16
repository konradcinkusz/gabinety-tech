import type { HTMLAttributes } from "react";
import styles from "./Badge.module.css";

export type BadgeVariant = "neutral" | "success" | "warning" | "danger";

export function Badge({ variant = "neutral", className, ...rest }: HTMLAttributes<HTMLSpanElement> & { variant?: BadgeVariant }) {
  return <span {...rest} className={[styles.badge, styles[variant], className].filter(Boolean).join(" ")} />;
}
