import type { HTMLAttributes } from "react";
import styles from "./Alert.module.css";

export type AlertVariant = "info" | "success" | "warning" | "danger";

export function Alert({ variant = "info", className, ...rest }: HTMLAttributes<HTMLDivElement> & { variant?: AlertVariant }) {
  return (
    <div
      {...rest}
      role={variant === "danger" ? "alert" : "status"}
      className={[styles.alert, styles[variant], className].filter(Boolean).join(" ")}
    />
  );
}
