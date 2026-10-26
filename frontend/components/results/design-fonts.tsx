"use client";

import { useEffect, useState } from "react";
import { Button } from "@/components/ui/button";
import { DesignCard } from "./design-card";
import { CopyIcon } from "@/components/ui/icons";
import { cleanWeights, fontFamilyCss, googleFontsUrl, isGoogleFontCandidate } from "@/lib/design-utils";
import type { DesignFont } from "@/lib/types";

interface Props {
  fonts: readonly DesignFont[] | undefined;
  onCopy: (text: string, label: string) => void;
}

const SAMPLE = "The quick brown fox jumps over the lazy dog";

// Adds one Google Fonts stylesheet link (once per URL) and reports whether the font really loaded.
function useGoogleFont(font: DesignFont): "loading" | "ready" | "missing" {
  const url = googleFontsUrl(font);
  const [state, setState] = useState<"loading" | "ready" | "missing">(url ? "loading" : "missing");

  useEffect(() => {
    if (!url) return;
    let cancelled = false;
    const fail = () => {
      if (!cancelled) setState("missing");
    };
    let link = Array.from(document.head.querySelectorAll<HTMLLinkElement>("link[data-design-font]")).find(
      (l) => l.dataset.designFont === url,
    );
    if (!link) {
      link = document.createElement("link");
      link.rel = "stylesheet";
      link.href = url;
      link.dataset.designFont = url;
      document.head.appendChild(link);
    }
    // Only ask the browser for the font once the stylesheet has loaded, otherwise it answers "none".
    const check = () => {
      document.fonts
        .load(`16px "${font.family}"`)
        .then((faces) => {
          if (!cancelled) setState(faces.length > 0 ? "ready" : "missing");
        })
        .catch(fail);
    };
    const el = link;
    el.addEventListener("error", fail);
    if (el.sheet) check();
    else el.addEventListener("load", check);
    return () => {
      cancelled = true;
      el.removeEventListener("error", fail);
      el.removeEventListener("load", check);
    };
  }, [url, font.family]);

  return state;
}

function FontCard({ font, onCopy }: { font: DesignFont; onCopy: Props["onCopy"] }) {
  const state = useGoogleFont(font);
  const weights = cleanWeights(font.weights);
  const url = googleFontsUrl(font);
  const css = fontFamilyCss(font.family);
  const style = { fontFamily: `"${font.family.replace(/["\\]/g, "")}", system-ui, sans-serif` };
  const generic = !isGoogleFontCandidate(font.family);
  return (
    <article className="space-y-3 rounded-lg border border-border-subtle bg-bg-base p-4">
      <header className="flex flex-wrap items-center gap-2">
        <h4 className="ts-h3 min-w-0 flex-1 basis-32 truncate text-ink">{font.family}</h4>
        <Button variant="ghost" className="!px-3 !py-1.5" onClick={() => onCopy(css, "font-family")}>
          <CopyIcon /> font-family
        </Button>
        {url && state !== "missing" && (
          <>
            <Button variant="ghost" className="!px-3 !py-1.5" onClick={() => onCopy(`<link rel="stylesheet" href="${url}">`, "link tag")}>
              <CopyIcon /> link
            </Button>
            <Button variant="ghost" className="!px-3 !py-1.5" onClick={() => onCopy(`@import url("${url}");`, "@import")}>
              <CopyIcon /> @import
            </Button>
          </>
        )}
      </header>
      {state === "missing" && (
        <p className="ts-small text-warning">
          {generic ? "Generic font, showing the browser default." : "Not on Google Fonts, showing a fallback."}{" "}
          <code className="ts-code text-ink-muted">{css}</code>
        </p>
      )}
      <p className="text-2xl text-ink" style={style}>{SAMPLE}</p>
      <ul className="space-y-1">
        {weights.map((w) => (
          <li key={w} className="flex items-baseline gap-3 text-ink-body">
            <span className="ts-label w-10 flex-none text-ink-muted">{w}</span>
            <span className="min-w-0 truncate text-lg" style={{ ...style, fontWeight: w }}>{SAMPLE}</span>
          </li>
        ))}
      </ul>
      {font.sizes && font.sizes.length > 0 && (
        <div className="flex flex-wrap items-baseline gap-x-4 gap-y-1 text-ink-body" aria-label="Size scale">
          {font.sizes.slice(0, 12).map((s) => {
            const px = Number.parseFloat(s);
            const shown = Number.isFinite(px) ? Math.min(Math.max(px, 10), 48) : 16;
            return (
              <span key={s} style={{ ...style, fontSize: shown }} title={s}>
                Aa <span className="ts-small text-ink-dim">{s}</span>
              </span>
            );
          })}
        </div>
      )}
    </article>
  );
}

// Font section: one card per family with a live sample, weight ladder, size scale and copy buttons.
export function DesignFonts({ fonts, onCopy }: Props) {
  const list = (fonts ?? []).filter((f) => f && typeof f.family === "string" && f.family.trim());
  if (list.length === 0) return null;
  return (
    <DesignCard id="design-fonts" title="Fonts" meta={`${list.length} ${list.length === 1 ? "family" : "families"}`}>
      <div className="grid gap-3 lg:grid-cols-2">
        {list.map((f) => (
          <FontCard key={f.family} font={f} onCopy={onCopy} />
        ))}
      </div>
    </DesignCard>
  );
}
