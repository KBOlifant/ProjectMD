"use client";

import { useId, type ReactNode } from "react";
import { Button } from "./button";

interface UrlInputProps {
  value: string;
  onChange: (value: string) => void;
  onSubmit: () => void;
  error?: string | null;
  // Turns off the text box and the button (for example when the account may not start scans).
  disabled?: boolean;
  // Replaces the default hint line under the box (shown only when there is no error).
  notice?: ReactNode;
}

// URL text box plus "Generate .MD" button. A controlled form: the typed text lives in the parent
// (value/onChange), submitting calls onSubmit, and `error` shows a validation message. `disabled`
// greys out both controls and `notice` explains why. Stores nothing.
export function UrlInput({ value, onChange, onSubmit, error, disabled = false, notice }: UrlInputProps) {
  const hintId = useId();

  return (
    <form
      noValidate
      onSubmit={(e) => {
        e.preventDefault();
        onSubmit();
      }}
    >
      <div className="flex flex-col gap-3 sm:flex-row">
        <label
          className={`flex h-[52px] flex-1 items-center gap-3 rounded-md border bg-bg-surface px-4 transition-shadow focus-within:border-violet-400 focus-within:shadow-glow ${
            error ? "border-danger" : "border-border-strong"
          } ${disabled ? "cursor-not-allowed opacity-60" : ""}`}
        >
          <span aria-hidden="true" className="ts-mono-ui text-ink-dim">
            ://
          </span>
          <input
            type="text"
            inputMode="url"
            autoComplete="off"
            spellCheck={false}
            aria-label="Website URL"
            aria-invalid={error ? true : undefined}
            aria-describedby={hintId}
            placeholder="https://your-site.com/docs"
            value={value}
            disabled={disabled}
            onChange={(e) => onChange(e.target.value)}
            className="ts-mono-ui min-w-0 flex-1 bg-transparent text-ink outline-none placeholder:text-ink-muted disabled:cursor-not-allowed"
          />
        </label>
        <Button type="submit" className="h-[52px] px-6" disabled={disabled}>
          Generate .MD
        </Button>
      </div>
      {error ? (
        <p id={hintId} role="alert" className="ts-small mt-3 text-danger">
          {error}
        </p>
      ) : (
        <p id={hintId} className="ts-small mt-3 text-ink-dim">
          {notice ?? "Enter to generate · Any public URL"}
        </p>
      )}
    </form>
  );
}
