import { chromium } from "playwright";

export async function scrapeSite(rawUrl: string) {
  const url = new URL(rawUrl);
  if (!["http:", "https:"].includes(url.protocol)) throw new Error("Bad protocol");
  // TODO: block localhost / private IPs before going further

  const browser = await chromium.launch();
  const page = await browser.newPage();

  const stylesheets: { url: string; css: string }[] = [];
  const scripts = new Set<string>();
  let headers: Record<string, string> = {};

  page.on("response", async (res) => {
    const type = res.request().resourceType();
    if (type === "stylesheet") stylesheets.push({ url: res.url(), css: await res.text().catch(() => "") });
    if (type === "script") scripts.add(res.url());
    if (res.url() === url.href) headers = res.headers();
  });

  await page.goto(url.href, { waitUntil: "networkidle", timeout: 30_000 });
  const html = await page.content();           // rendered DOM
  const screenshot = await page.screenshot({ type: "jpeg", quality: 60 });
  await browser.close();

  return { html, stylesheets, scripts: [...scripts], headers, screenshot };
}