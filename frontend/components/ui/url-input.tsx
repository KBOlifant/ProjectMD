"use client";

import { motion } from "framer-motion";
import { useId, type ReactNode } from "react";
import { buttonClass } from "./button";
import { ArrowRightIcon, EnterIcon } from "./icons";

// The arrow shifts right and grows slightly on hover; driven by the button's own hover state (see
// `variants`/`whileHover` below), not a separate hover handler.
const ARROW_VARIANTS = { rest: { x: 0, scale: 1 }, hover: { x: 3, scale: 1.15 } };

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
      <motion.div
        whileHover={disabled ? undefined : { scale: 1.015 }}
        transition={{ type: "spring", stiffness: 300, damping: 25 }}
        className={`flex items-center gap-2 rounded-xl border bg-white/10 p-1.5 shadow-panel backdrop-blur-lg backdrop-saturate-200 transition-colors focus-within:border-violet-400 ${
          error ? "border-danger" : "border-white/25"
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
            className="ts-mono-ui min-w-0 flex-1 bg-transparent text-[14px] text-ink outline-none! placeholder:text-zinc-200 disabled:cursor-not-allowed"
          />
        </label>
        <motion.button
          type="submit"
          disabled={disabled}
          initial="rest"
          whileHover="hover"
          animate="rest"
          className={buttonClass("accent", "h-12 flex-none px-5")}
        >
          Analyze website
          <motion.span variants={ARROW_VARIANTS} transition={{ type: "spring", stiffness: 400, damping: 20 }} className="inline-flex">
            <ArrowRightIcon size={14} />
          </motion.span>
        </motion.button>
      </motion.div>
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
