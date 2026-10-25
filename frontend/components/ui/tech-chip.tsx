// Small pill showing one detected technology (e.g. "React 19"); `highlighted` gives it the accent colour. Stores nothing.
export function TechChip({ name, highlighted = false }: { name: string; highlighted?: boolean }) {
  return (
    <span
      className={`inline-block rounded-sm border px-2 py-0.5 font-mono text-[11px] leading-4 ${
        highlighted
          ? "border-mulberry-500 bg-fig-500 text-ink"
          : "border-border-strong bg-bg-raised text-ink-muted"
      }`}
    >
      {name}
    </span>
  );
}

// Badge showing the scan's confidence %, coloured green (95+), amber (under 80) or neutral. Stores nothing.
export function ConfidenceBadge({ value }: { value: number }) {
  const tone =
    value >= 95
      ? "bg-success-bg text-success"
      : value < 80
        ? "bg-bg-raised text-warning"
        : "bg-bg-raised text-ink-muted";
  return (
    <span className={`ts-mono-ui rounded-sm px-2 py-0.5 text-[11px] leading-4 ${tone}`}>
      {value}% confidence
    </span>
  );
}
