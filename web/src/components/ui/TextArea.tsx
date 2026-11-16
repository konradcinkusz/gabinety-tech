"use client";

import { useState, type TextareaHTMLAttributes } from "react";
import { t } from "@/i18n/pl";
import styles from "./controls.module.css";

export interface TextAreaProps extends TextareaHTMLAttributes<HTMLTextAreaElement> {
  /** Show a "n / maxLength" counter; requires maxLength. */
  showCount?: boolean;
}

export function TextArea({ showCount, className, onChange, defaultValue, value, ...rest }: TextAreaProps) {
  const [length, setLength] = useState(String(value ?? defaultValue ?? "").length);
  const shown = value !== undefined ? String(value).length : length;
  return (
    <>
      <textarea
        {...rest}
        value={value}
        defaultValue={defaultValue}
        className={[styles.control, className].filter(Boolean).join(" ")}
        onChange={(e) => {
          setLength(e.target.value.length);
          onChange?.(e);
        }}
      />
      {showCount && rest.maxLength !== undefined ? (
        <span className={styles.counter} aria-hidden="true">
          {t("common.characterCount", { count: shown, max: rest.maxLength })}
        </span>
      ) : null}
    </>
  );
}
