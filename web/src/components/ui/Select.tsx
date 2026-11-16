import type { SelectHTMLAttributes } from "react";
import styles from "./controls.module.css";

export function Select({ className, ...rest }: SelectHTMLAttributes<HTMLSelectElement>) {
  return <select {...rest} className={[styles.control, className].filter(Boolean).join(" ")} />;
}
