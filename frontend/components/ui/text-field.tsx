import { useId, type InputHTMLAttributes, type ReactNode, type Ref } from "react";

interface TextFieldProps
  extends Omit<InputHTMLAttributes<HTMLInputElement>, "id" | "className" | "aria-invalid" | "aria-describedby"> {
  label: string;
  hint?: ReactNode;
  error?: string | null;
  trailing?: ReactNode;
  ref?: Ref<HTMLInputElement>;
}

// Labelled text box styled like the URL input. The visible label sits above the box, `hint` is a
// helper line under it, and `error` replaces the hint in danger colour (announced politely to screen
// readers and wired up with aria-invalid and aria-describedby). `trailing` puts a control, such as a
// show/hide button, inside the box at the right edge. Every other prop (type, value, onChange,
// autoComplete...) goes straight to the <input>. The focus ring is drawn on the box (violet border
// and glow); the input's own outline is removed with `outline-none!` because the global
// :focus-visible rule in globals.css would otherwise draw a second ring inside it.
// Stores nothing: the typed value lives in the parent.
export function TextField({ label, hint, error, trailing, ref, ...inputProps }: TextFieldProps) {
  const uid = useId();
  const inputId = `${uid}-input`;
  const hintId = `${uid}-hint`;
  const errorId = `${uid}-error`;
  const showHint = Boolean(hint) && !error;
  const describedBy = error ? errorId : showHint ? hintId : undefined;

  return (
    <div>
      <label htmlFor={inputId} className="ts-label mb-2 block text-ink-muted">
        {label}
      </label>
      <div
        className={`flex h-12 items-center gap-3 rounded-md border bg-bg-surface px-4 transition-shadow focus-within:border-violet-400 focus-within:shadow-glow ${
          error ? "border-danger" : "border-border-strong"
        }`}
      >
        <input
          ref={ref}
          id={inputId}
          aria-invalid={error ? true : undefined}
          aria-describedby={describedBy}
          className="ts-mono-ui min-w-0 flex-1 bg-transparent text-ink outline-none! placeholder:text-ink-muted autofill:shadow-[inset_0_0_0_1000px_var(--bg-surface)] autofill:[-webkit-text-fill-color:var(--ink)]"
          {...inputProps}
        />
        {trailing}
      </div>
      {showHint ? (
        <p id={hintId} className="ts-small mt-2 text-ink-muted">
          {hint}
        </p>
      ) : null}
      <p id={errorId} aria-live="polite" className={error ? "ts-small mt-2 text-danger" : undefined}>
        {error}
      </p>
    </div>
  );
}
