import { TextInput } from "@/components/ui";
import { pp } from "./strings";
import type { InputHTMLAttributes } from "react";

/** A text input with the same "n / max" counter the TextArea shows. */
export function CountedInput({ value, maxLength, ...rest }: InputHTMLAttributes<HTMLInputElement> & { value: string; maxLength: number }) {
  return (
    <>
      <TextInput {...rest} value={value} maxLength={maxLength} />
      <span aria-hidden="true">{pp("common.counter", { n: value.length, max: maxLength })}</span>
    </>
  );
}
