namespace ProjectMD.Api.Services.Extraction.Components;

// The JavaScript that runs inside the rendered page to read how a few main components look. Like
// DesignScript it only calls getComputedStyle and reads tag names, class names (to find cards and
// badges) and the type of an input; it never reads text, cookies, storage or form values and makes no
// request. It checks at most 1500 elements, keeps up to 2 differently-styled buttons and 1 each of
// input, card, nav, heading and badge, and for each walks the element tree (at most 30 elements, 4
// levels, 12 children each) recording the computed styles. It returns one JSON string:
//   { items:[ { kind, background, height, root:{ t, s:{prop:value}, i?, x?, c:[...] } } ] }
// where t is the tag, s the styles, i the input type, x true when the element has text of its own.
public static class ComponentScript
{
    public const string Source = """
(() => {
  const MAX_CHECK = 1500, MAX_NODES = 30, MAX_DEPTH = 4, MAX_CHILDREN = 12;
  const LIMITS = { button: 2, input: 1, card: 1, nav: 1, heading: 1, badge: 1 };
  const SIDES = ['top', 'right', 'bottom', 'left'], CORNERS = ['top-left', 'top-right', 'bottom-right', 'bottom-left'];
  const PROPS = ['color', 'background-color', 'box-shadow', 'font-family', 'font-size', 'font-weight', 'font-style', 'line-height',
    'letter-spacing', 'text-transform', 'text-align', 'text-decoration-line', 'display', 'flex-direction', 'flex-wrap',
    'align-items', 'justify-content', 'gap', 'opacity', 'cursor', 'white-space']
    .concat(SIDES.flatMap(s => ['border-' + s + '-width', 'border-' + s + '-style', 'border-' + s + '-color', 'padding-' + s, 'margin-' + s]))
    .concat(CORNERS.map(c => 'border-' + c + '-radius'));
  const COLOUR = p => p === 'color' || p === 'background-color' || /^border-.*-color$/.test(p);
  const TAGS = new Set(['DIV', 'SPAN', 'BUTTON', 'A', 'INPUT', 'LABEL', 'NAV', 'UL', 'OL', 'LI', 'P', 'SMALL', 'STRONG', 'EM',
    'H1', 'H2', 'H3', 'H4', 'H5', 'H6', 'HEADER', 'SECTION', 'ARTICLE', 'TEXTAREA']);
  const SKIP = new Set(['SCRIPT', 'STYLE', 'NOSCRIPT', 'TEMPLATE', 'BR', 'WBR', 'LINK', 'META']);
  let ctx = null;
  try { const c = document.createElement('canvas'); c.width = 1; c.height = 1; ctx = c.getContext('2d', { willReadFrequently: true }); } catch (e) { ctx = null; }
  const paint = (value, base) => { ctx.clearRect(0, 0, 1, 1); ctx.fillStyle = base; ctx.fillStyle = value; ctx.fillRect(0, 0, 1, 1); return ctx.getImageData(0, 0, 1, 1).data; };
  const hex2 = n => (n < 16 ? '0' : '') + n.toString(16);
  const colour = (v) => {
    if (!v || !ctx || /^#[0-9a-f]{6}$/i.test(v)) { return v; }
    try {
      const a = paint(v, '#000000'), b = paint(v, '#ffffff');
      if (a[0] === b[0] && a[1] === b[1] && a[2] === b[2] && a[3] === b[3]) {
        return a[3] < 6 ? 'transparent' : '#' + hex2(a[0]) + hex2(a[1]) + hex2(a[2]);
      }
    } catch (e) { /* keep the original */ }
    return v;
  };
  const visible = (el) => {
    const cs = getComputedStyle(el);
    if (cs.display === 'none' || cs.visibility === 'hidden') { return null; }
    const rect = el.getBoundingClientRect();
    return rect.width > 0 && rect.height > 0 ? { cs, rect } : null;
  };
  const read = (cs) => {
    const s = {};
    for (const p of PROPS) { const v = cs.getPropertyValue(p); s[p] = COLOUR(p) ? colour(v) : v; }
    return s;
  };
  const hasOwnText = (el) => Array.prototype.some.call(el.childNodes, n => n.nodeType === 3 && n.nodeValue.trim().length > 0);
  let count = 0;
  const walk = (el, depth, root) => {
    const seen = visible(el);
    if (!seen || count >= MAX_NODES) { return null; }
    count++;
    const { cs, rect } = seen;
    const styles = read(cs);
    const replaced = ['SVG', 'IMG', 'CANVAS', 'VIDEO', 'PICTURE'].includes(el.tagName.toUpperCase());
    const leaf = replaced || (el.children.length === 0 && !hasOwnText(el));
    if (root || leaf) { styles.width = Math.round(rect.width) + 'px'; styles.height = Math.round(rect.height) + 'px'; }
    const tag = TAGS.has(el.tagName) ? el.tagName.toLowerCase() : (cs.display.startsWith('inline') ? 'span' : 'div');
    const node = { t: tag, s: styles, c: [] };
    if (el.tagName === 'INPUT') { node.i = el.getAttribute('type') || 'text'; }
    if (hasOwnText(el)) { node.x = true; }
    if (depth < MAX_DEPTH && !['INPUT', 'TEXTAREA', 'SVG'].includes(el.tagName.toUpperCase())) {
      for (const child of Array.prototype.slice.call(el.children, 0, MAX_CHILDREN)) {
        if (SKIP.has(child.tagName)) { continue; }
        const sub = walk(child, depth + 1, false);
        if (sub) { node.c.push(sub); }
      }
    }
    return node;
  };
  const backdrop = (el) => {
    for (let p = el.parentElement; p; p = p.parentElement) {
      const c = colour(getComputedStyle(p).backgroundColor);
      if (c && c !== 'transparent' && /^#[0-9a-f]{6}$/i.test(c)) { return c; }
    }
    return '#ffffff';
  };
  const found = { button: [], input: [], card: [], nav: [], heading: [], badge: [] };
  const seenKeys = new Set();
  const take = (kind, el) => {
    if (found[kind].length >= LIMITS[kind]) { return; }
    const seen = visible(el);
    if (!seen || seen.rect.width < 8 || seen.rect.height < 8 || seen.rect.width > 1200) { return; }
    const key = kind + JSON.stringify(read(seen.cs));
    if (seenKeys.has(key)) { return; }
    seenKeys.add(key);
    count = 0;
    const root = walk(el, 0, true);
    if (root) { found[kind].push({ kind, background: backdrop(el), height: Math.round(seen.rect.height), root }); }
  };
  const looksLike = (el, re) => typeof el.className === 'string' && re.test(el.className);
  const all = document.body ? document.body.querySelectorAll('*') : [];
  const limit = Math.min(all.length, MAX_CHECK);
  for (let i = 0; i < limit; i++) {
    const el = all[i], tag = el.tagName;
    if (tag === 'BUTTON' || el.getAttribute('role') === 'button' || (tag === 'A' && looksLike(el, /(^|[\s_-])(btn|button)([\s_-]|$)/i))) { take('button', el); }
    else if (tag === 'INPUT' && ['text', 'email', 'search', 'password', 'url', 'tel', null, ''].includes(el.getAttribute('type'))) { take('input', el); }
    else if (tag === 'TEXTAREA' || tag === 'SELECT') { take('input', el); }
    else if (tag === 'NAV') { take('nav', el); }
    else if (tag === 'H1' || tag === 'H2') { take('heading', el); }
    else if (looksLike(el, /(^|[\s_-])(badge|chip|pill|tag)([\s_-]|$)/i)) { take('badge', el); }
    else if ((tag === 'DIV' || tag === 'ARTICLE' || tag === 'LI') && el.children.length >= 2 && looksLike(el, /card/i)) { take('card', el); }
  }
  if (found.card.length === 0) {
    for (let i = 0; i < limit && found.card.length < LIMITS.card; i++) {
      const el = all[i];
      if (!['DIV', 'ARTICLE', 'LI'].includes(el.tagName) || el.children.length < 2) { continue; }
      const cs = getComputedStyle(el);
      const boxed = cs.boxShadow !== 'none' || parseFloat(cs.borderTopWidth) > 0;
      if (boxed && parseFloat(cs.borderTopLeftRadius) >= 4 && cs.backgroundColor !== 'rgba(0, 0, 0, 0)') { take('card', el); }
    }
  }
  const items = [].concat(found.button, found.input, found.card, found.nav, found.heading, found.badge);
  return JSON.stringify({ items });
})()
""";
}
