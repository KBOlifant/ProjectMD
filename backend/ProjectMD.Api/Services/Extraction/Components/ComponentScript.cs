namespace ProjectMD.Api.Services.Extraction.Components;

// The JavaScript that runs inside the rendered page to read the look of a few main components.
// Like DesignScript it only calls getComputedStyle and reads tag names and class names (to find
// cards and badges); it never reads text, cookies, storage or form values and makes no request.
// It checks at most 1500 elements, keeps at most 2 differently-styled examples of each kind
// (button, input, card, nav, heading, badge) and returns one JSON string:
//   { items:[ { kind, tag, styles:{ cssProperty: value } } ] }
public static class ComponentScript
{
    public const string Source = """
(() => {
  const MAX_CHECK = 1500, PER_KIND = 2;
  const PROPS = ['color', 'background-color', 'border-top-width', 'border-top-style', 'border-top-color', 'border-radius',
    'box-shadow', 'font-family', 'font-size', 'font-weight', 'line-height', 'letter-spacing', 'text-transform',
    'padding-top', 'padding-right', 'padding-bottom', 'padding-left', 'width', 'height', 'display', 'gap', 'text-decoration-line'];
  const COLOUR = new Set(['color', 'background-color', 'border-top-color']);
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
  const kinds = { button: [], input: [], card: [], nav: [], heading: [], badge: [] };
  const seen = new Set();
  const take = (kind, el) => {
    if (kinds[kind].length >= PER_KIND) { return; }
    const cs = getComputedStyle(el);
    if (cs.display === 'none' || cs.visibility === 'hidden') { return; }
    const rect = el.getBoundingClientRect();
    if (rect.width < 8 || rect.height < 8 || rect.width > 1200) { return; }
    const styles = {};
    for (const p of PROPS) {
      let v = cs.getPropertyValue(p);
      if (COLOUR.has(p)) { v = colour(v); }
      if (p === 'width' || p === 'height') { v = Math.round(p === 'width' ? rect.width : rect.height) + 'px'; }
      styles[p] = v;
    }
    const key = kind + JSON.stringify(styles);
    if (seen.has(key)) { return; }
    seen.add(key);
    kinds[kind].push({ kind, tag: el.tagName.toLowerCase(), styles });
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
  if (kinds.card.length === 0) {
    for (let i = 0; i < limit && kinds.card.length < PER_KIND; i++) {
      const el = all[i];
      if (!['DIV', 'ARTICLE', 'LI'].includes(el.tagName) || el.children.length < 2) { continue; }
      const cs = getComputedStyle(el);
      const boxed = cs.boxShadow !== 'none' || parseFloat(cs.borderTopWidth) > 0;
      if (boxed && parseFloat(cs.borderTopLeftRadius) >= 4 && cs.backgroundColor !== 'rgba(0, 0, 0, 0)') { take('card', el); }
    }
  }
  const items = [].concat(kinds.button, kinds.input, kinds.card, kinds.nav, kinds.heading, kinds.badge);
  return JSON.stringify({ items });
})()
""";
}
