import type { ReactNode } from "react";

// Renders the subset of Markdown that Project MD emits as React elements:
// headings, blockquotes, bullet lists, fenced code, paragraphs, **bold**, `code`.

// Converts **bold** and `code` spans inside one line of text into React elements. Stores nothing.
function inline(text: string): ReactNode[] {
  return text.split(/(\*\*[^*]+\*\*|`[^`]+`)/g).map((part, i) => {
    if (part.startsWith("**") && part.endsWith("**") && part.length > 4)
      return <strong key={i} className="font-semibold text-ink">{part.slice(2, -2)}</strong>;
    if (part.startsWith("`") && part.endsWith("`") && part.length > 2)
      return <code key={i} className="rounded-sm bg-bg-raised px-1 font-mono text-[12px] text-ink">{part.slice(1, -1)}</code>;
    return part;
  });
}

// Walks the markdown text line by line and builds the formatted preview (headings, quotes, bullet
// lists, code blocks, paragraphs). Display only; stores nothing.
export function MarkdownPreview({ source }: { source: string }) {
  const lines = source.split("\n");
  const out: ReactNode[] = [];
  let i = 0;

  while (i < lines.length) {
    const line = lines[i];
    const key = out.length;

    if (line.startsWith("```")) {
      const code: string[] = [];
      i++;
      while (i < lines.length && !lines[i].startsWith("```")) code.push(lines[i++]);
      i++;
      out.push(
        <pre key={key} className="ts-code overflow-x-auto rounded-lg border border-border-subtle bg-bg-base p-4 text-ink-body">
          {code.join("\n")}
        </pre>,
      );
    } else if (/^#{1,3} /.test(line)) {
      const level = line.indexOf(" ");
      const text = line.slice(level + 1);
      if (level === 1) out.push(<h1 key={key} className="ts-h2 text-ink">{text}</h1>);
      else if (level === 2) out.push(<h2 key={key} className="ts-h3 pt-2 text-ink">{text}</h2>);
      else out.push(<h3 key={key} className="ts-body-strong text-ink">{text}</h3>);
      i++;
    } else if (line.startsWith("> ")) {
      out.push(<blockquote key={key} className="ts-body text-ink-muted">{inline(line.slice(2))}</blockquote>);
      i++;
    } else if (line.startsWith("- ")) {
      const items: string[] = [];
      while (i < lines.length && lines[i].startsWith("- ")) items.push(lines[i++].slice(2));
      out.push(
        <ul key={key} className="ts-body space-y-1">
          {items.map((item, j) => (
            <li key={j} className="flex gap-2">
              <span aria-hidden="true" className="text-violet-400">•</span>
              <span className="min-w-0 break-words">{inline(item)}</span>
            </li>
          ))}
        </ul>,
      );
    } else if (line.trim()) {
      out.push(<p key={key} className="ts-body">{inline(line)}</p>);
      i++;
    } else {
      i++;
    }
  }

  return <div className="space-y-4">{out}</div>;
}
