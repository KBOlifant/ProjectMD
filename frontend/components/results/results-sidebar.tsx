import { formatDate, formatSeconds } from "@/lib/format";
import type { Scan } from "@/lib/types";
import { TechChip } from "@/components/ui/tech-chip";

// Small plum label above each block of the side panel. Stores nothing.
function PanelLabel({ id, children }: { id: string; children: string }) {
  return (
    <h2 id={id} className="ts-label mb-3 text-plum-300">
      {children}
    </h2>
  );
}

// Page preview: the site's screenshot when the scan has one, otherwise a stylised purple
// "browser" with placeholder bars; then the host and scan time. The whole card opens the site.
function ScanThumbnail({ scan }: { scan: Scan }) {
  return (
    <a
      href={scan.url}
      target="_blank"
      rel="noopener noreferrer"
      title={`Open ${scan.host}`}
      className="block overflow-hidden rounded-xl border border-border-strong bg-bg-surface transition-colors hover:border-violet-500/60"
    >
      {scan.screenshotUrl ? (
        // eslint-disable-next-line @next/next/no-img-element -- remote Cloudinary image, no Next image config needed
        <img src={scan.screenshotUrl} alt={`Screenshot of ${scan.host}`} className="aspect-[16/10] w-full object-cover object-top" />
      ) : (
        <div aria-hidden="true" className="bg-violet-700 px-3 pb-5 pt-3">
          <div className="flex items-center gap-1.5 rounded-sm bg-black/25 px-2 py-1.5">
            <span className="size-1.5 rounded-full bg-ink/60" />
            <span className="size-1.5 rounded-full bg-ink/60" />
            <span className="size-1.5 rounded-full bg-ink/60" />
            <span className="ml-1 h-1 flex-1 rounded-full bg-ink/25" />
          </div>
          <div className="mt-5 space-y-2 px-1">
            <span className="block h-2 w-4/5 rounded-full bg-ink/80" />
            <span className="block h-2 w-3/5 rounded-full bg-ink/80" />
            <span className="mt-3 block h-3.5 w-14 rounded-sm bg-plum-500" />
          </div>
        </div>
      )}
      <div className="px-3.5 py-3">
        <p className="ts-mono-ui truncate font-semibold text-ink">{scan.host}</p>
        <p className="font-mono text-[10px] leading-4 text-ink-muted">
          Scanned {formatDate(scan.createdAt)} · {formatSeconds(scan.durationMs)}
        </p>
      </div>
    </a>
  );
}

// The left column of Results: page thumbnail, tech fingerprint (two-column tiles) and a table of
// scan details taken from the saved scan. Display only; stores nothing.
export function ResultsSidebar({ scan }: { scan: Scan }) {
  const details = [
    { label: "Pages crawled", value: String(scan.pages) },
    { label: "Scan time", value: formatSeconds(scan.durationMs) },
    { label: "Technologies", value: String(scan.tech.length) },
    { label: "Confidence", value: `${scan.confidence}%` },
  ];
  return (
    <div className="space-y-7">
      <ScanThumbnail scan={scan} />

      <section aria-labelledby="fingerprint">
        <PanelLabel id="fingerprint">Tech fingerprint</PanelLabel>
        {scan.tech.length > 0 ? (
          <ul className="grid grid-cols-2 gap-2">
            {scan.tech.map((t) => (
              <li key={t.name} className="min-w-0">
                <TechChip name={t.name} highlighted={t.highlighted} />
              </li>
            ))}
          </ul>
        ) : (
          <p className="ts-small text-ink-muted">No technologies were detected on this page.</p>
        )}
      </section>

      <section aria-labelledby="details">
        <PanelLabel id="details">Scan details</PanelLabel>
        <dl className="overflow-hidden rounded-lg border border-border-subtle bg-bg-surface">
          {details.map((d) => (
            <div key={d.label} className="flex items-center justify-between gap-3 border-b border-border-subtle px-3 py-2.5 last:border-b-0">
              <dt className="ts-small text-ink-muted">{d.label}</dt>
              <dd className="font-mono text-[11px] text-ink">{d.value}</dd>
            </div>
          ))}
        </dl>
      </section>
    </div>
  );
}
