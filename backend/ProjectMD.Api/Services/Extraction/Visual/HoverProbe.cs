using Microsoft.Playwright;

namespace ProjectMD.Api.Services.Extraction.Visual;

// Finds hover states by really hovering. A hover effect cannot be read from the page without a
// mouse, so a few differently-styled buttons, cards, links and images are scrolled into view one at a
// time, the mouse is moved onto each, and the computed style before and after is compared. A change
// is reported as an animation with source "css-hover", the changed properties with their start and
// end values, and the element's own transition timing. Like MotionScript it only reads (plus the
// scrolling and mouse movement a visitor would do) and changes nothing on the page. The answer is
// {"animations":[...]}, read and cleaned by MotionEvidenceReader.WithHoverStates. Any failure just
// means fewer hover states; it never fails a scan.
public static class HoverProbe
{
    // Total time allowed for all the hovers of one page.
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(7);
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(2);

    // Picks the elements to hover (stored on window so the next steps can find them) and returns how
    // many there are. Takes the maximum as its argument. Round-robin over buttons, cards, links and
    // other elements so ten identical menu links cannot use up every slot.
    public const string CollectSource = """
        (max) => {
          const SELECTOR = 'button, [role=button], [role=tab], summary, a[href], [class*=card], [class*=btn], [class*=button], [class*=link], img';
          const buckets = { button: [], card: [], link: [], other: [] };
          const seen = new Set();
          const all = document.body ? document.body.querySelectorAll(SELECTOR) : [];
          const limit = Math.min(all.length, 800);
          for (let i = 0; i < limit; i++) {
            const el = all[i];
            const rect = el.getBoundingClientRect();
            if (rect.width < 16 || rect.height < 12 || rect.width > 900 || rect.height > 600) { continue; }
            const cs = getComputedStyle(el);
            if (cs.display === 'none' || cs.visibility === 'hidden' || cs.pointerEvents === 'none') { continue; }
            const cls = typeof el.className === 'string' ? el.className.split(/\s+/)[0] : '';
            const key = [el.tagName, cls, cs.color, cs.backgroundColor, cs.borderRadius, cs.fontSize].join('|');
            if (seen.has(key)) { continue; }
            seen.add(key);
            const tag = el.tagName;
            const bucket = (tag === 'BUTTON' || el.getAttribute('role') === 'button' || el.getAttribute('role') === 'tab' || /(^|[\s_-])(btn|button)/i.test(cls)) ? 'button'
              : (/card/i.test(cls) ? 'card' : (tag === 'A' ? 'link' : 'other'));
            if (buckets[bucket].length < max) { buckets[bucket].push(el); }
          }
          const picked = [];
          for (let round = 0; picked.length < max; round++) {
            let any = false;
            for (const b of ['button', 'card', 'link', 'other']) {
              if (round < buckets[b].length && picked.length < max) { picked.push(buckets[b][round]); any = true; }
            }
            if (!any) { break; }
          }
          window.__pmdHover = picked;
          window.__pmdHoverBefore = [];
          return picked.length;
        }
        """;

    // Scrolls element i into view, remembers its style, and returns "x,y" (its centre in the window)
    // or null when it cannot be hovered.
    public const string BeginSource = """
        (i) => {
          const el = window.__pmdHover && window.__pmdHover[i];
          if (!el || !el.isConnected) { return null; }
          el.scrollIntoView({ block: 'center', inline: 'center', behavior: 'instant' });
          const rect = el.getBoundingClientRect();
          const x = rect.left + rect.width / 2, y = rect.top + rect.height / 2;
          if (x < 0 || y < 0 || x > window.innerWidth || y > window.innerHeight) { return null; }
          const cs = getComputedStyle(el);
          const before = {};
          for (const p of ['color', 'background-color', 'border-top-color', 'box-shadow', 'transform', 'opacity', 'scale', 'translate', 'filter', 'text-decoration-line']) {
            before[p] = cs.getPropertyValue(p);
          }
          window.__pmdHoverBefore[i] = before;
          return Math.round(x) + ',' + Math.round(y);
        }
        """;

    // Compares element i with its remembered style after the mouse arrived. Returns one animation
    // as a JSON string, or null when nothing changed.
    public const string EndSource = """
        (i) => {
          const el = window.__pmdHover && window.__pmdHover[i];
          const before = window.__pmdHoverBefore && window.__pmdHoverBefore[i];
          if (!el || !before) { return null; }
          const clip = (s, n) => { s = String(s == null ? '' : s); return s.length > n ? s.slice(0, n) : s; };
          const first = (v) => String(v || '').split(/,(?![^(]*\))/)[0].trim();
          const ms = (v) => { const t = first(v); const n = parseFloat(t); return isFinite(n) ? Math.round(t.endsWith('ms') ? n : n * 1000) : null; };
          const cs = getComputedStyle(el);
          const values = [], props = [];
          for (const p of Object.keys(before)) {
            const now = cs.getPropertyValue(p);
            if (now === before[p]) { continue; }
            const name = p === 'border-top-color' ? 'border-color' : p;
            props.push(name);
            values.push({ p: name, f: clip(before[p], 60), t: clip(now, 60) });
          }
          if (props.length === 0) { return null; }
          let target = el.tagName.toLowerCase();
          if (el.id) { target += '#' + el.id; }
          if (el.classList && el.classList.length) { target += '.' + el.classList[0]; }
          const textTag = /^(H[1-6]|P|SPAN|A|LI|LABEL|STRONG|EM|B|I|SMALL)$/.test(el.tagName);
          return JSON.stringify({
            source: 'css-hover',
            target: clip(target, 80),
            name: 'hover',
            durationMs: ms(cs.transitionDuration),
            delayMs: ms(cs.transitionDelay),
            easing: clip(first(cs.transitionTimingFunction), 60) || null,
            repeat: null,
            props: props.slice(0, 8),
            values: values.slice(0, 8),
            text: textTag
          });
        }
        """;

    // Hovers up to Motion:HoverProbes elements and returns {"animations":[...]}, or null when none
    // were enabled or nothing changed. Only the caller's own cancellation is rethrown.
    public static async Task<string?> TryProbeAsync(IPage page, MotionOptions options, ILogger logger, CancellationToken ct)
    {
        if (options.HoverProbes <= 0)
        {
            return null;
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(Budget);
        var token = limit.Token;
        var entries = new List<string>();
        try
        {
            var count = await page.EvaluateAsync<int>(CollectSource, options.HoverProbes).WaitAsync(StepTimeout, token);
            for (var i = 0; i < count; i++)
            {
                if (await ProbeOneAsync(page, i, options, token) is { } entry)
                {
                    entries.Add(entry);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The time limit or a page problem: keep the hover states found so far.
            logger.LogDebug(ex, "Hover probe stopped early after {Found} hover states", entries.Count);
        }

        return entries.Count == 0 ? null : "{\"animations\":[" + string.Join(',', entries) + "]}";
    }

    // Hovers one element; null when it could not be hovered or nothing changed.
    private static async Task<string?> ProbeOneAsync(IPage page, int index, MotionOptions options, CancellationToken token)
    {
        // Park the mouse in the corner first so the element starts un-hovered.
        await page.Mouse.MoveAsync(1, 1).WaitAsync(StepTimeout, token);
        var position = await page.EvaluateAsync<string?>(BeginSource, index).WaitAsync(StepTimeout, token);
        var parts = position?.Split(',');
        if (parts is not { Length: 2 }
            || !float.TryParse(parts[0], out var x) || !float.TryParse(parts[1], out var y))
        {
            return null;
        }

        await page.Mouse.MoveAsync(x, y).WaitAsync(StepTimeout, token);
        if (options.HoverSettleMs > 0)
        {
            await Task.Delay(options.HoverSettleMs, token);
        }

        return await page.EvaluateAsync<string?>(EndSource, index).WaitAsync(StepTimeout, token);
    }
}
