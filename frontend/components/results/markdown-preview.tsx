import type { ReactNode } from "react";

// Renders the subset of Markdown that Project MD emits as React elements: YAML front matter (shown
// as a code block), headings, blockquotes, nested bullet lists, GFM tables, fenced code,
// horizontal rules, paragraphs, **bold**, *italic*, `code` and backslash escapes. No raw HTML is
// ever rendered: every piece of text becomes a React text node.

const PUNCTUATION = "\\`*_{}[]()#+-.!|<>&~:/@";

// Finds the end of a run of `count` backticks that starts at or after `from`, or -1.
function findBacktickRun(text: string, from: number, count: number): number {
  let i = from;
  while (i < text.length) {
    if (text[i] !== "`") {
      i++;
      continue;
    }
    let run = 0;
    while (text[i + run] === "`") run++;
    if (run === count) return i;
    i += run;
  }
  return -1;
}

// Finds the next unescaped `marker` (one or two asterisks) at or after `from`, or -1.
function findClosing(text: string, from: number, marker: string): number {
  for (let i = from; i < text.length; i++) {
    if (text[i] === "\\") i++;
    else if (text.startsWith(marker, i)) return i;
  }
  return -1;
}

// Converts one line of text into React elements: backslash escapes become plain characters,
// `code`, **bold** and *italic* become elements. Pure display; stores nothing.
function inline(text: string, inTable = false): ReactNode[] {
  const out: ReactNode[] = [];
  let plain = "";
  const flush = () => {
    if (plain) out.push(plain);
    plain = "";
  };

  let i = 0;
  while (i < text.length) {
    const c = text[i];
    if (c === "\\" && i + 1 < text.length && PUNCTUATION.includes(text[i + 1])) {
      plain += text[i + 1];
      i += 2;
    } else if (c === "`") {
      let ticks = 0;
      while (text[i + ticks] === "`") ticks++;
      const end = findBacktickRun(text, i + ticks, ticks);
      if (end < 0) {
        plain += "`".repeat(ticks);
        i += ticks;
        continue;
      }
      flush();
      let code = text.slice(i + ticks, end).trim();
      if (inTable) code = code.replaceAll("\\|", "|");
      out.push(<code key={out.length} className="rounded-sm bg-bg-raised px-1 font-mono text-[12px] text-ink">{code}</code>);
      i = end + ticks;
    } else if (c === "*") {
      const marker = text.startsWith("**", i) ? "**" : "*";
      const end = findClosing(text, i + marker.length, marker);
      if (end <= i + marker.length) {
        plain += marker;
        i += marker.length;
        continue;
      }
      flush();
      const inner = inline(text.slice(i + marker.length, end), inTable);
      out.push(marker === "**"
        ? <strong key={out.length} className="font-semibold text-ink">{inner}</strong>
        : <em key={out.length}>{inner}</em>);
      i = end + marker.length;
    } else {
      plain += c;
      i++;
    }
  }
  flush();
  return out;
}

// Splits a table row into its cells: pipes inside code spans or after a backslash do not split.
function splitRow(line: string): string[] {
  const cells: string[] = [];
  let cell = "";
  let inCode = false;
  const body = line.trim().replace(/^\|/, "");
  for (let i = 0; i < body.length; i++) {
    const c = body[i];
    if (c === "\\" && i + 1 < body.length) {
      cell += c + body[++i];
    } else if (c === "`") {
      inCode = !inCode;
      cell += c;
    } else if (c === "|" && !inCode) {
      cells.push(cell.trim());
      cell = "";
    } else {
      cell += c;
    }
  }
  if (cell.trim()) cells.push(cell.trim());
  return cells;
}

// True for a table separator row such as "| --- | --- |".
const isSeparator = (line: string | undefined): boolean => !!line && /^\|(\s*:?-{3,}:?\s*\|)+\s*$/.test(line.trim());

// A GFM table: scrolls sideways when it is wider than the panel.
function renderTable(header: string[], rows: string[][], key: number): ReactNode {
  return (
    <div key={key} className="overflow-x-auto rounded-lg border border-border-subtle">
      <table className="ts-body w-full border-collapse text-left">
        <thead className="bg-bg-raised">
          <tr>
            {header.map((h, j) => (
              <th key={j} className="ts-body-strong whitespace-nowrap px-3 py-2 text-ink">{inline(h, true)}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, r) => (
            <tr key={r} className="border-t border-border-subtle align-top">
              {header.map((_, j) => (
                <td key={j} className="px-3 py-2 text-ink-body">{inline(row[j] ?? "", true)}</td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

// A bullet list whose items may be indented (two spaces per level).
function renderList(items: { depth: number; text: string }[], key: number): ReactNode {
  return (
    <ul key={key} className="ts-body space-y-1">
      {items.map((item, j) => (
        <li key={j} className="flex gap-2" style={{ paddingLeft: `${Math.min(item.depth, 5) * 1.25}rem` }}>
          <span aria-hidden="true" className="text-violet-400">•</span>
          <span className="min-w-0 break-words">{inline(item.text)}</span>
        </li>
      ))}
    </ul>
  );
}

// A code block: monospace, scrolls sideways, text only.
function renderCode(lines: string[], key: number): ReactNode {
  return (
    <pre key={key} className="ts-code overflow-x-auto rounded-lg border border-border-subtle bg-bg-base p-4 font-mono text-ink-body">
      {lines.join("\n")}
    </pre>
  );
}

// Reads a fenced block that starts on line `start`; returns its lines and the index after it. The
// block ends on a line of only backticks that is at least as long as the opening fence.
function readFence(lines: string[], start: number): { code: string[]; next: number } {
  const ticks = /^`+/.exec(lines[start])![0].length;
  const code: string[] = [];
  let i = start + 1;
  while (i < lines.length && !(/^`+\s*$/.test(lines[i]) && lines[i].trim().length >= ticks)) code.push(lines[i++]);
  return { code, next: i + 1 };
}

// Splits off a leading YAML front matter block ("---" ... "---"), if there is one.
function splitFrontMatter(source: string): { front: string[] | null; body: string[] } {
  const lines = source.split("\n");
  if (lines[0]?.trim() === "---") {
    const end = lines.findIndex((l, i) => i > 0 && l.trim() === "---");
    if (end > 0) return { front: lines.slice(1, end), body: lines.slice(end + 1) };
  }
  return { front: null, body: lines };
}

// Walks the markdown text line by line and builds the formatted preview. Display only; stores nothing.
export function MarkdownPreview({ source }: { source: string }) {
  const { front, body: lines } = splitFrontMatter(source);
  const out: ReactNode[] = [];
  if (front) out.push(renderCode(front, out.length));
  let i = 0;

  while (i < lines.length) {
    const line = lines[i];
    const key = out.length;

    if (line.startsWith("```")) {
      const { code, next } = readFence(lines, i);
      out.push(renderCode(code, key));
      i = next;
    } else if (/^#{1,4} /.test(line)) {
      const level = line.indexOf(" ");
      const text = inline(line.slice(level + 1));
      if (level === 1) out.push(<h1 key={key} className="ts-h2 text-ink">{text}</h1>);
      else if (level === 2) out.push(<h2 key={key} className="ts-h3 pt-2 text-ink">{text}</h2>);
      else out.push(<h3 key={key} className="ts-body-strong text-ink">{text}</h3>);
      i++;
    } else if (line.trim() === "---") {
      out.push(<hr key={key} className="border-border-subtle" />);
      i++;
    } else if (line.startsWith("|") && isSeparator(lines[i + 1])) {
      const rows: string[][] = [];
      let j = i + 2;
      while (j < lines.length && lines[j].startsWith("|")) rows.push(splitRow(lines[j++]));
      out.push(renderTable(splitRow(line), rows, key));
      i = j;
    } else if (line.startsWith("> ")) {
      out.push(<blockquote key={key} className="ts-body text-ink-muted">{inline(line.slice(2))}</blockquote>);
      i++;
    } else if (/^\s*- /.test(line)) {
      const items: { depth: number; text: string }[] = [];
      while (i < lines.length && /^\s*- /.test(lines[i])) {
        const indent = lines[i].length - lines[i].trimStart().length;
        items.push({ depth: Math.floor(indent / 2), text: lines[i].trimStart().slice(2) });
        i++;
      }
      out.push(renderList(items, key));
    } else if (line.trim()) {
      out.push(<p key={key} className="ts-body break-words">{inline(line)}</p>);
      i++;
    } else {
      i++;
    }
  }

  return <div className="space-y-4">{out}</div>;
}
