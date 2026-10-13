export type FormMessageTone = "error" | "success" | "neutral";

const tones: Record<FormMessageTone, string> = {
  error: "border-danger/40 bg-bg-base text-danger",
  success: "border-success/40 bg-success-bg text-success",
  neutral: "border-border-strong bg-bg-raised text-ink-body",
};

// Form-level message box (for example a failed sign-in or "reset link sent"). The surrounding
// aria-live="polite" region is always in the page, so screen readers announce a message when it
// appears. Renders an empty region when `message` is empty. Stores nothing.
export function FormMessage({ message, tone = "error" }: { message?: string | null; tone?: FormMessageTone }) {
  return (
    <div aria-live="polite">
      {message ? <p className={`ts-small rounded-md border px-3 py-2 ${tones[tone]}`}>{message}</p> : null}
    </div>
  );
}
