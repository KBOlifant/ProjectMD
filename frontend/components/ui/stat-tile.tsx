export interface Stat {
  value: string;
  caption: string;
}

// Row of summary numbers (e.g. Total scans, Avg confidence) with captions. Display only; stores nothing.
export function StatStrip({ stats }: { stats: Stat[] }) {
  return (
    <dl className="grid grid-cols-2 overflow-hidden rounded-[2.5rem] border border-border-subtle bg-bg-surface shadow-[0_40px_90px_-10px_rgba(0,0,0,0.85),0_0_60px_-15px_rgba(158,74,187,0.45)] lg:grid-cols-4 lg:rounded-pill">
      {stats.map((s, i) => (
        <div
          key={s.caption}
          className={`px-8 py-8 text-center ${i % 2 === 1 ? "border-l border-border-subtle" : ""} ${
            i > 0 ? "lg:border-l lg:border-border-subtle" : ""
          } ${i >= 2 ? "border-t border-border-subtle lg:border-t-0" : ""}`}
        >
          <dd className="ts-stat text-ink text-4xl">{s.value}</dd>
          <dt className="ts-small text-ink-muted">{s.caption}</dt>
        </div>
      ))}
    </dl>
  );
}
