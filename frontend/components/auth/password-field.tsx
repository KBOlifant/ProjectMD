"use client";

import { useState, type ComponentProps } from "react";
import { TextField } from "@/components/ui/text-field";

type PasswordFieldProps = Omit<ComponentProps<typeof TextField>, "type" | "trailing">;

// Password text box with a Show/Hide button inside it. Same props as TextField (label, error, hint,
// autoComplete...). Whether the password is visible is temporary state kept in this component only.
export function PasswordField(props: PasswordFieldProps) {
  const [visible, setVisible] = useState(false);

  return (
    <TextField
      {...props}
      type={visible ? "text" : "password"}
      autoCapitalize="none"
      spellCheck={false}
      trailing={
        <button
          type="button"
          onClick={() => setVisible((v) => !v)}
          aria-label={`${visible ? "Hide" : "Show"} ${props.label.toLowerCase()}`}
          className="ts-mono-ui rounded-sm text-ink-muted transition-colors hover:text-ink-hover"
        >
          {visible ? "Hide" : "Show"}
        </button>
      }
    />
  );
}
