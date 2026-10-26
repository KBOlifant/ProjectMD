export interface Stat {
  value: string;
  caption: string;
}

// Row of summary numbers (e.g. Total scans, Avg confidence) with captions. Display only; stores nothing.
export function StatStrip({ stats }: { stats: Stat[] }) {
  return (
    <dl className="grid grid-cols-2 overflow-hidden rounded-lg border border-border-subtle bg-bg-surface lg:grid-cols-4">
      {stats.map((s, i) => (
        <div
          key={s.caption}
          className={`px-5 py-4 ${i % 2 === 1 ? "border-l border-border-subtle" : ""} ${
            i > 0 ? "lg:border-l lg:border-border-subtle" : ""
          } ${i >= 2 ? "border-t border-border-subtle lg:border-t-0" : ""}`}
        >
          <dd className="ts-stat text-ink">{s.value}</dd>
          <dt className="ts-small text-ink-muted">{s.caption}</dt>
        </div>
      ))}
    </dl>
  );
}
