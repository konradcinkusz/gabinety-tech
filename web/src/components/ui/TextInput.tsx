import type { InputHTMLAttributes } from "react";
import styles from "./controls.module.css";

export function TextInput({ className, type = "text", ...rest }: InputHTMLAttributes<HTMLInputElement>) {
  return <input {...rest} type={type} className={[styles.control, className].filter(Boolean).join(" ")} />;
}
