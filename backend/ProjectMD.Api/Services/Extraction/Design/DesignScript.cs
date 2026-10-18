namespace ProjectMD.Api.Services.Extraction.Design;

// The JavaScript that runs inside the rendered page to sample its styles. It only calls
// getComputedStyle and reads tag names, class names and the "role" attribute; it never reads text,
// cookies, storage or form values, and never makes a network request. It samples at most 800
// visible elements (every n-th element when the page is bigger), skips hidden, zero-size and
// script/style/svg-internal elements, and returns one JSON string of counted values:
//   { sampled, colors:[[role,value,n]], fonts:[[family,weight,size,n]], radii:[[v,n]],
//     shadows:[[v,n]], spacing:[[px,n]], classes:[["a b c",n]] }
// Each list is cut to its 150 most frequent entries so the answer stays small. (Playwright for
// .NET offers no isolated world for evaluate, so the script runs in the page's world; it changes
// nothing on the page.)
public static class DesignScript
{
    // The script text, evaluated as one expression that yields the JSON string.
    public const string Source = """
(() => {
  const MAX_ELEMENTS = 800, MAX_ENTRIES = 150, MAX_CLASSES = 12, SEP = '\u0001';
  const root = document.documentElement, body = document.body;
  if (!root || !body) { return JSON.stringify({ sampled: 0 }); }
  const maps = { colors: new Map(), fonts: new Map(), radii: new Map(), shadows: new Map(), spacing: new Map(), classes: new Map() };
  const bump = (map, key) => map.set(key, (map.get(key) || 0) + 1);
  const isClear = v => !v || v === 'transparent' || v === 'rgba(0, 0, 0, 0)';
  // Resolves any colour the browser understands (oklch, oklab, lab, lch, color(), color-mix...) to
  // "#rrggbb" by painting it on a 1x1 canvas; "transparent" when it is see-through. Unknown values
  // are returned unchanged so the server-side parser can try. Painting on two different
  // backgrounds tells a rejected value (which paints nothing) from a real black.
  const resolved = new Map();
  let ctx = null;
  try { const c = document.createElement('canvas'); c.width = 1; c.height = 1; ctx = c.getContext('2d', { willReadFrequently: true }); } catch (e) { ctx = null; }
  const paint = (value, base) => {
    ctx.clearRect(0, 0, 1, 1);
    ctx.fillStyle = base; ctx.fillStyle = value;
    ctx.fillRect(0, 0, 1, 1);
    return ctx.getImageData(0, 0, 1, 1).data;
  };
  const hex2 = n => (n < 16 ? '0' : '') + n.toString(16);
  const colour = (v) => {
    if (!v || !ctx || /^#[0-9a-f]{6}$/i.test(v)) { return v; }
    if (resolved.has(v)) { return resolved.get(v); }
    let out = v;
    try {
      const a = paint(v, '#000000'), b = paint(v, '#ffffff');
      if (a[0] === b[0] && a[1] === b[1] && a[2] === b[2] && a[3] === b[3]) {
        out = a[3] < 6 ? 'transparent' : '#' + hex2(a[0]) + hex2(a[1]) + hex2(a[2]);
      }
    } catch (e) { out = v; }
    if (resolved.size < 2000) { resolved.set(v, out); }
    return out;
  };
  const faces = [];
  try {
    document.fonts.forEach(f => { if (f.status === 'loaded' && faces.length < 100 && f.family.length <= 80) { faces.push(f.family.replace(/^["']|["']$/g, '')); } });
  } catch (e) { /* no font list available */ }
  const SKIP = new Set(['SCRIPT', 'STYLE', 'NOSCRIPT', 'TEMPLATE', 'HEAD', 'META', 'LINK', 'TITLE', 'BR', 'WBR']);
  const FORM = new Set(['INPUT', 'SELECT', 'TEXTAREA']);
  const SPACING = ['marginTop', 'marginRight', 'marginBottom', 'marginLeft', 'paddingTop', 'paddingRight', 'paddingBottom', 'paddingLeft', 'rowGap', 'columnGap'];

  const read = (el) => {
    const cs = getComputedStyle(el);
    const interactive = el.tagName === 'A' || el.tagName === 'BUTTON' || el.getAttribute('role') === 'button' ||
      (el.tagName === 'INPUT' && ['submit', 'button'].includes(el.getAttribute('type')));
    const fg = colour(cs.color), bg = colour(cs.backgroundColor);
    const hasBg = !isClear(bg);
    if (!isClear(fg)) { bump(maps.colors, 'text' + SEP + fg); }
    if (hasBg) { bump(maps.colors, 'background' + SEP + bg); }
    if (parseFloat(cs.borderTopWidth) > 0 && cs.borderTopStyle !== 'none') {
      const border = colour(cs.borderTopColor);
      if (!isClear(border)) { bump(maps.colors, 'border' + SEP + border); }
    }
    if (interactive) {
      if (!isClear(fg)) { bump(maps.colors, 'accent' + SEP + fg); }
      if (hasBg) { bump(maps.colors, 'accent' + SEP + bg); }
    }
    bump(maps.fonts, [cs.fontFamily, cs.fontWeight, cs.fontSize].join(SEP));
    if (cs.borderRadius && cs.borderRadius !== '0px' && (interactive || FORM.has(el.tagName) || hasBg)) {
      bump(maps.radii, cs.borderRadius);
    }
    if (cs.boxShadow && cs.boxShadow !== 'none') { bump(maps.shadows, cs.boxShadow); }
    for (const prop of SPACING) {
      const v = parseFloat(cs[prop]);
      if (v > 0 && v <= 128 && String(cs[prop]).endsWith('px')) { bump(maps.spacing, String(Math.round(v))); }
    }
    if (el.classList && el.classList.length >= 3) {
      bump(maps.classes, Array.prototype.slice.call(el.classList, 0, MAX_CLASSES).join(' '));
    }
  };

  const all = body.querySelectorAll('*');
  const step = Math.max(1, Math.ceil(all.length / MAX_ELEMENTS));
  let sampled = 0;
  read(root); read(body); sampled += 2;
  for (let i = 0; i < all.length && sampled < MAX_ELEMENTS; i += step) {
    const el = all[i];
    if (SKIP.has(el.tagName)) { continue; }
    if (el.namespaceURI === 'http://www.w3.org/2000/svg' && el.tagName.toLowerCase() !== 'svg') { continue; }
    const cs = getComputedStyle(el);
    if (cs.display === 'none' || cs.visibility === 'hidden') { continue; }
    const rect = el.getBoundingClientRect();
    if (rect.width <= 0 || rect.height <= 0) { continue; }
    read(el);
    sampled++;
  }

  const top = (map, split) => Array.from(map.entries())
    .sort((a, b) => b[1] - a[1] || (a[0] < b[0] ? -1 : a[0] > b[0] ? 1 : 0))
    .slice(0, MAX_ENTRIES)
    .map(([key, count]) => split ? key.split(SEP).concat([count]) : [key, count]);
  return JSON.stringify({
    sampled,
    colors: top(maps.colors, true),
    fonts: top(maps.fonts, true),
    radii: top(maps.radii, false),
    shadows: top(maps.shadows, false),
    spacing: top(maps.spacing, false),
    classes: top(maps.classes, false),
    faces
  });
})()
""";
}
