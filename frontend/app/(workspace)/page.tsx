"use client";

import { motion } from "framer-motion";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { PermissionError } from "@/app/functions/rbac/errors";
import { useCan, useDenyReason } from "@/app/functions/rbac/useCan";
import { ScanBlockedNotice } from "@/components/auth-status/scan-blocked-notice";
import { useScan } from "@/components/scan-provider";
import { Eyebrow } from "@/components/shell/page-frame";
import { EmptyState } from "@/components/ui/empty-state";
import { HeroBackdrop } from "@/components/ui/hero-backdrop";
import { ArrowRightIcon } from "@/components/ui/icons";
import { HistoryRow } from "@/components/ui/history-row";
import { ScrambleText } from "@/components/ui/scramble-text";
import { SmoothScroll } from "@/components/ui/smooth-scroll";
import { StatStrip } from "@/components/ui/stat-tile";
import { UrlInput } from "@/components/ui/url-input";
import { downloadScan } from "@/lib/download";
import { formatSeconds } from "@/lib/format";
import { RISE_ITEM, STAGGER_ITEM, STAGGER_LIST } from "@/lib/motion";
import { useHistory, useHistoryStatus, useIsHydrated } from "@/lib/history-store";
import { parseUrl } from "@/lib/url";

const DELIVERABLES = [
  { title: "Technical documentation", text: "A readable map of frameworks, infrastructure, dependencies, and content." },
  { title: "Design system exports", text: "Tokens Studio JSON, CSS variables, and a Tailwind v3 configuration." },
  { title: "Reusable building blocks", text: "Copy-ready components and detected animations in your preferred format." },
];

// Home page (/): a full-height hero with the URL bar, a "what you'll get" panel, then summary stats
// and the 3 latest scans. Stats are calculated from the saved history on every render (not stored).
// The typed URL and error are temporary state, not stored. The URL bar is disabled (with the reason
// shown) when the account may not create scans.
export default function GeneratePage() {
  const router = useRouter();
  const { startScan } = useScan();
  const history = useHistory();
  const hydrated = useIsHydrated();
  const { error: loadError } = useHistoryStatus();
  const canCreate = useCan("scan:create");
  const canExport = useCan("scan:export");
  const denyReason = useDenyReason("scan:create");
  const [value, setValue] = useState("");
  const [error, setError] = useState<string | null>(null);

  // Called when the form is submitted: validates the typed URL with parseUrl. On error it shows
  // the message; otherwise it starts a scan and goes to /processing. If startScan refuses with a
  // PermissionError (signed out, unverified email or read-only role) its message is shown as the
  // form error and we stay here. Nothing is saved yet.
  function submit() {
    const parsed = parseUrl(value);
    if (!parsed.ok) return setError(parsed.error);
    setError(null);
    try {
      startScan(parsed.url);
    } catch (err) {
      if (err instanceof PermissionError) return setError(err.message);
      throw err;
    }
    router.push("/processing");
  }

  const n = history.length;
  const avg = (pick: (s: (typeof history)[number]) => number) =>
    history.reduce((sum, s) => sum + pick(s), 0) / n;
  const stats = [
    { value: String(n), caption: "Total scans" },
    { value: n ? `${Math.round(avg((s) => s.confidence))}%` : "—", caption: "Avg confidence" },
    { value: String(history.reduce((sum, s) => sum + s.pages, 0)), caption: "Pages indexed" },
    { value: n ? formatSeconds(avg((s) => s.durationMs)) : "—", caption: "Avg speed" },
  ];

  return (
    <>
      <SmoothScroll />
      <section className="relative isolate flex min-h-[640px] items-center justify-center overflow-hidden border-b border-border-subtle px-4 py-20 md:min-h-screen">
        <HeroBackdrop />
        {/* Outer div drifts slower than the page (Locomotive parallax); the inner one staggers in. */}
        <div data-scroll data-scroll-speed="-0.2" className="w-full max-w-[700px]">
          <motion.div initial="initial" animate="animate" variants={STAGGER_LIST} className="text-center">
            <motion.span variants={STAGGER_ITEM} className="ts-label inline-flex items-center gap-2 rounded-pill border border-border-strong bg-bg-base/70 px-3 py-1.5 text-ink-body">
              <span aria-hidden="true" className="size-1.5 rounded-full bg-violet-500 dot-blink" />
              AI website intelligence
            </motion.span>
            <motion.h1 variants={STAGGER_ITEM} className="ts-hero mt-5 text-ink">
              <span className="block"><ScrambleText text="Project" /></span>
              <span className="-mt-1 block text-violet-400"><ScrambleText text="MD" delay={320} /></span>
            </motion.h1>
            <motion.p variants={STAGGER_ITEM} className="mx-auto mt-8 max-w-[620px] text-[17px] leading-[30px] text-ink-body">
              Enter a URL and Project MD will inspect the stack, content, components, design tokens, and motion—then
              package everything for your team.
            </motion.p>
            <motion.div variants={STAGGER_ITEM} className="mt-8">
              <UrlInput
                value={value}
                onChange={setValue}
                onSubmit={submit}
                error={error}
                disabled={!canCreate}
                notice={denyReason ? <ScanBlockedNotice reason={denyReason} /> : undefined}
              />
            </motion.div>
          </motion.div>
        </div>
      </section>

      <div className="space-y-16 px-4 py-14 md:px-[84px] md:py-16">
        <section aria-labelledby="get">
          <Eyebrow>
            <ScrambleText text="What you'll get" />
          </Eyebrow>
          <h2 id="get" className="ts-h1 mt-3 text-ink">
            <ScrambleText text="A complete handoff, not another audit." />
          </h2>
          <motion.ul
            initial="initial"
            whileInView="animate"
            viewport={{ once: true, amount: 0.3 }}
            variants={STAGGER_LIST}
            className="mt-8 grid overflow-hidden rounded-xl border border-border-subtle bg-bg-surface md:grid-cols-3 md:divide-x md:divide-border-subtle">
            {DELIVERABLES.map((d, i) => (
              <motion.li
                key={d.title}
                variants={STAGGER_ITEM}
                className="flex min-h-[190px] flex-col justify-between gap-8 border-b border-border-subtle p-6 last:border-b-0 md:border-b-0"
              >
                <span aria-hidden="true" className="font-mono text-[10px] text-plum-300">
                  {String(i + 1).padStart(2, "0")}
                </span>
                <div>
                  <h3 className="ts-h3 text-ink">{d.title}</h3>
                  <p className="ts-body mt-2 text-ink-muted">{d.text}</p>
                </div>
              </motion.li>
            ))}
          </motion.ul>
        </section>

        <section aria-labelledby="recent" className="space-y-6">
          <motion.div
            initial={{ opacity: 0, y: 24 }}
            whileInView={{ opacity: 1, y: 0 }}
            viewport={{ once: true }}
            transition={{ duration: 0.5, ease: "easeOut" }}
          >
            <StatStrip stats={stats} />
          </motion.div>
          <div className="flex items-center justify-between">
            <h2 id="recent" className="ts-label text-[13px] leading-5 text-plum-300">
              Recent scans
            </h2>
            {n > 3 && (
              <Link
                href="/history"
                className="ts-mono-ui group inline-flex items-center gap-1.5 rounded-md border border-transparent px-3.5 py-2 text-[14px] text-violet-400 transition-colors duration-300 hover:border-violet-500 hover:text-ink"
              >
                View all
                <ArrowRightIcon className="-ml-1 w-0 -translate-x-2 opacity-0 transition-all duration-300 group-hover:ml-0 group-hover:w-4 group-hover:translate-x-0 group-hover:opacity-100" />
              </Link>
            )}
          </div>
          {!hydrated ? null : n === 0 ? (
            <EmptyState>{loadError ?? "No scans yet. Enter a URL above to generate your first .md file."}</EmptyState>
          ) : (
            <motion.div initial="initial" whileInView="animate" viewport={{ once: true, amount: 0.15 }} variants={STAGGER_LIST} className="space-y-3">
              {history.slice(0, 3).map((scan) => (
                <motion.div key={scan.id} variants={RISE_ITEM}>
                  <HistoryRow
                    scan={scan}
                    onDownload={() => downloadScan(scan)}
                    downloadDisabled={!canExport}
                    downloadHint="Your account cannot export scans."
                  />
                </motion.div>
              ))}
            </motion.div>
          )}
        </section>
      </div>
    </>
  );
}
