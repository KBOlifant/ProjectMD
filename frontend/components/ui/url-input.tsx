"use client";

import { useId, type ReactNode } from "react";
import { Button } from "./button";
import { ArrowRightIcon, EnterIcon } from "./icons";

interface UrlInputProps {
  value: string;
  onChange: (value: string) => void;
  onSubmit: () => void;
  error?: string | null;
  // Turns off the text box and the button (for example when the account may not start scans).
  disabled?: boolean;
  // Replaces the default hint row under the box (shown only when there is no error).
  notice?: ReactNode;
}

const HINTS = [
  { icon: <EnterIcon size={11} />, text: "Press enter" },
  { icon: null, text: "Public URLs only" },
  { icon: null, text: "Usually 30-60 seconds" },
];

// The hero's URL bar: a "://" prefix, the address field and the violet "Analyze website" button
// in one rounded frame, with a hint row under it. A controlled form: the typed text lives in the
// parent (value/onChange), submitting calls onSubmit, and `error` shows a validation message in
// place of the hints. `disabled` greys out both controls and `notice` explains why. Stores nothing.
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
      <div
        className={`flex items-center gap-2 rounded-xl border bg-bg-surface/95 p-1.5 shadow-panel transition-colors focus-within:border-violet-400 ${
          error ? "border-danger" : "border-border-strong"
        } ${disabled ? "opacity-60" : ""}`}
      >
        <label className="flex h-12 min-w-0 flex-1 items-center gap-3 px-3">
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
            placeholder="your-site.com"
            value={value}
            disabled={disabled}
            onChange={(e) => onChange(e.target.value)}
            className="ts-mono-ui min-w-0 flex-1 bg-transparent text-[14px] text-ink outline-none! placeholder:text-ink-muted disabled:cursor-not-allowed"
          />
        </label>
        <Button type="submit" variant="accent" className="h-12 flex-none px-5" disabled={disabled}>
          Analyze website
          <ArrowRightIcon size={14} />
        </Button>
      </div>
      {error ? (
        <p id={hintId} role="alert" className="ts-small mt-3 text-center text-danger">
          {error}
        </p>
      ) : (
        <div id={hintId} className="ts-small mt-4 flex flex-wrap justify-center gap-x-6 gap-y-1 font-mono text-[11px] text-ink-muted">
          {notice ??
            HINTS.map((h) => (
              <span key={h.text} className="inline-flex items-center gap-1.5">
                {h.icon}
                {h.text}
              </span>
            ))}
        </div>
      )}
    </form>
  );
}
