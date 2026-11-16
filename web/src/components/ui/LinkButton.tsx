import Link from "next/link";
import type { ComponentProps } from "react";
import styles from "./Button.module.css";
import type { ButtonVariant } from "./Button";

export interface LinkButtonProps extends ComponentProps<typeof Link> {
  variant?: ButtonVariant;
}

export function LinkButton({ variant = "primary", className, ...rest }: LinkButtonProps) {
  return <Link {...rest} className={[styles.link, styles[variant], className].filter(Boolean).join(" ")} />;
}
