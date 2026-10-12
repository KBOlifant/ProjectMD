import { scrapeSite } from "./scrape";
import { writeFileSync, mkdirSync } from "fs";

async function main() {
  const url = process.argv[2];
  if (!url) {
    console.error("Usage: npx tsx frontend/lib/test-scrape.ts <url>");
    process.exit(1);
  }

  console.time("scrape");
  const r = await scrapeSite(url);
  console.timeEnd("scrape");

  console.log("HTML size (chars):", r.html.length);
  console.log("Stylesheets:", r.stylesheets.length, "files,", r.stylesheets.reduce((n, s) => n + s.css.length, 0), "chars");
  console.log("Scripts:", r.scripts.length);
  console.log("Server header:", r.headers["server"], "| x-vercel-id:", r.headers["x-vercel-id"]);
  console.log("Screenshot bytes:", r.screenshot.length);

  

    mkdirSync("out", { recursive: true });
    writeFileSync("out/page.html", r.html);
    writeFileSync("out/page.jpg", r.screenshot);
    writeFileSync("out/styles.css", r.stylesheets.map(s => `/* ${s.url} */\n${s.css}`).join("\n\n"));
    console.log("Title:", r.html.match(/<title>(.*?)<\/title>/is)?.[1]);
}

main().catch((e) => {
  console.error("Scrape failed:", e);
  process.exit(1);
});