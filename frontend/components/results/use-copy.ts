"use client";

import { useCallback, useEffect, useRef, useState } from "react";

const MESSAGE_MS = 1800;

// Old-style copy for browsers that block the clipboard API: selects a hidden text box and runs "copy".
function fallbackCopy(text: string): boolean {
  const box = document.createElement("textarea");
  box.value = text;
  box.setAttribute("readonly", "");
  box.style.position = "fixed";
  box.style.opacity = "0";
  document.body.appendChild(box);
  box.select();
  let ok = false;
  try {
    ok = document.execCommand("copy");
  } catch {
    ok = false;
  }
  document.body.removeChild(box);
  return ok;
}

// Gives a `copy(text, label)` function and a short status message ("Copied #fff" or "Copy blocked")
// for an aria-live region. The message clears itself. Stores nothing.
export function useCopy(): { message: string; copy: (text: string, label?: string) => Promise<void> } {
  const [message, setMessage] = useState("");
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(
    () => () => {
      if (timer.current) clearTimeout(timer.current);
    },
    [],
  );

  const copy = useCallback(async (text: string, label?: string) => {
    let ok = false;
    try {
      await navigator.clipboard.writeText(text);
      ok = true;
    } catch {
      ok = fallbackCopy(text);
    }
    setMessage(ok ? `Copied ${label ?? ""}`.trim() : "Copy blocked by the browser");
    if (timer.current) clearTimeout(timer.current);
    timer.current = setTimeout(() => setMessage(""), MESSAGE_MS);
  }, []);

  return { message, copy };
}
