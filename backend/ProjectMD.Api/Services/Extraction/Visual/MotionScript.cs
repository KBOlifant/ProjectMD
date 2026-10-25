namespace ProjectMD.Api.Services.Extraction.Visual;

// The script run inside the loaded page to read animation evidence. It only reads: which animation
// libraries are present (globals such as window.gsap, or tell-tale attributes such as data-lenis),
// the animations the browser itself is running (document.getAnimations(): CSS animations, CSS
// transitions, Web Animations), GSAP's own tween list and ScrollTrigger list when GSAP is exposed on
// window, and a few element counts. It never reads page text, form values, cookies or storage, and
// it changes nothing on the page. Every part is wrapped so one failure cannot stop the rest. The
// answer is one JSON string, read by MotionEvidenceReader.
public static class MotionScript
{
    // The script text, evaluated as one expression that yields the JSON string.
    public const string Source = """
        (() => {
          const MAX_ANIMS = 40, MAX_TWEENS = 30, MAX_TRIGGERS = 20, MAX_SCAN = 1500;
          const w = window;
          const html = document.documentElement;
          const clip = (s, n) => { s = String(s == null ? '' : s); return s.length > n ? s.slice(0, n) : s; };
          const num = (v) => (typeof v === 'number' && isFinite(v)) ? Math.round(v) : null;
          const has = (sel) => { try { return !!document.querySelector(sel); } catch (e) { return false; } };
          const count = (sel) => { try { return document.querySelectorAll(sel).length; } catch (e) { return 0; } };
          const describe = (el) => {
            try {
              if (!el || !el.tagName) { return 'element'; }
              let d = el.tagName.toLowerCase();
              if (el.id) { d += '#' + el.id; }
              if (el.classList && el.classList.length) { d += '.' + el.classList[0]; }
              return clip(d, 80);
            } catch (e) { return 'element'; }
          };
          const hasClass = (name) => { try { return !!html && html.classList.contains(name); } catch (e) { return false; } };

          const libs = {};
          try {
            libs.gsap = !!(w.gsap || w.TweenMax || w.TweenLite);
            libs.scrollTrigger = !!w.ScrollTrigger;
            libs.splitText = !!(w.SplitText || w.SplitType);
            libs.lenis = !!(w.lenis || w.Lenis) || hasClass('lenis') || has('[data-lenis-prevent]');
            libs.locomotive = has('[data-scroll-container]') || hasClass('has-scroll-smooth');
            libs.barba = !!w.barba || has('[data-barba]');
            libs.swup = !!w.swup || has('#swup');
            libs.three = !!w.THREE || has('canvas[data-engine^="three"]');
            libs.pixi = !!w.PIXI;
            libs.lottie = !!(w.lottie || w.bodymovin) || has('lottie-player, dotlottie-player, dotlottie-wc');
            libs.rive = !!w.rive;
            libs.spline = has('spline-viewer');
            libs.anime = !!w.anime;
            libs.aos = !!w.AOS || has('[data-aos]');
            libs.webflowIx = !!(w.Webflow && html && html.hasAttribute('data-wf-page'));
            libs.framer = has('[data-framer-name], [data-framer-appear-id]');
            libs.swiper = !!w.Swiper || has('.swiper-wrapper');
          } catch (e) { }

          const counts = { canvas: count('canvas'), video: count('video'), split: count('.char, [data-split], [data-splitting]'), smil: count('animate, animateTransform, animateMotion'), willChange: 0, sticky: 0 };
          try {
            const all = document.body ? document.body.getElementsByTagName('*') : [];
            const n = Math.min(all.length, MAX_SCAN);
            for (let i = 0; i < n; i++) {
              const cs = getComputedStyle(all[i]);
              if (cs.willChange && cs.willChange !== 'auto') { counts.willChange++; }
              if (cs.position === 'sticky') { counts.sticky++; }
            }
          } catch (e) { }

          const animations = [];
          try {
            const list = document.getAnimations ? document.getAnimations() : [];
            for (const a of list) {
              if (animations.length >= MAX_ANIMS) { break; }
              const fx = a.effect;
              if (!fx || !fx.getTiming) { continue; }
              const t = fx.getTiming();
              const type = a.constructor ? a.constructor.name : '';
              const source = type === 'CSSAnimation' ? 'css-animation' : (type === 'CSSTransition' ? 'css-transition' : 'web-animation');
              const props = new Set();
              const values = [];
              try {
                const kfs = fx.getKeyframes ? fx.getKeyframes() : [];
                for (const k of kfs) {
                  for (const p of Object.keys(k)) {
                    if (p !== 'offset' && p !== 'computedOffset' && p !== 'easing' && p !== 'composite') { props.add(p); }
                  }
                }
                if (kfs.length > 1) {
                  const first = kfs[0], last = kfs[kfs.length - 1];
                  for (const p of ['opacity', 'transform', 'translate', 'scale', 'rotate']) {
                    if (first[p] != null || last[p] != null) {
                      values.push({ p, f: first[p] == null ? null : clip(first[p], 60), t: last[p] == null ? null : clip(last[p], 60) });
                    }
                  }
                }
              } catch (e) { }
              animations.push({
                source,
                target: describe(fx.target),
                name: clip(a.animationName || a.transitionProperty || '', 60) || null,
                durationMs: num(t.duration),
                delayMs: num(t.delay),
                easing: clip(t.easing || '', 60) || null,
                repeat: t.iterations === Infinity ? 'infinite' : (typeof t.iterations === 'number' ? String(t.iterations) : null),
                props: Array.from(props).slice(0, 8),
                values
              });
            }
          } catch (e) { }

          try {
            const g = w.gsap;
            if (g && g.globalTimeline && g.globalTimeline.getChildren) {
              const skip = new Set(['ease', 'duration', 'delay', 'stagger', 'scrollTrigger', 'repeat', 'yoyo', 'paused', 'immediateRender',
                'overwrite', 'id', 'data', 'callbackScope', 'lazy', 'inherit', 'runBackwards', 'startAt', 'keyframes', 'repeatDelay', 'runBackwards',
                'parent', 'scrollTrigger', 'reversed', 'smoothChildTiming', 'autoRemoveChildren']);
              let tweens = 0;
              for (const tw of g.globalTimeline.getChildren(true, true, false)) {
                if (tweens >= MAX_TWEENS) { break; }
                // Only tweens that move page elements; GSAP's own timers (delayedCall) have none.
                const elements = (tw.targets ? tw.targets() : []).filter(t => t && t.nodeType === 1);
                if (elements.length === 0) { continue; }
                const v = tw.vars || {};
                let stagger = null;
                if (typeof v.stagger === 'number') { stagger = v.stagger; }
                else if (v.stagger && typeof v.stagger === 'object' && typeof v.stagger.each === 'number') { stagger = v.stagger.each; }
                const values = [];
                const back = !!v.runBackwards, sa = (v.startAt && typeof v.startAt === 'object') ? v.startAt : {};
                for (const p of ['opacity', 'autoAlpha', 'x', 'y', 'xPercent', 'yPercent', 'scale', 'rotation', 'rotate']) {
                  if (typeof v[p] !== 'number') { continue; }
                  const name = p === 'autoAlpha' ? 'opacity' : (p === 'rotate' ? 'rotation' : p);
                  values.push({
                    p: name,
                    f: back ? String(v[p]) : (typeof sa[p] === 'number' ? String(sa[p]) : null),
                    t: back ? null : String(v[p])
                  });
                }
                const targets = elements.slice(0, 2).map(describe);
                animations.push({
                  source: 'gsap',
                  target: clip(targets.join(', ') + (elements.length > 2 ? ' (+' + (elements.length - 2) + ' more)' : ''), 120),
                  name: stagger === null ? null : 'stagger ' + clip(stagger, 8) + 's',
                  durationMs: num((tw.duration ? tw.duration() : 0) * 1000),
                  delayMs: num((tw.delay ? tw.delay() : 0) * 1000),
                  easing: typeof v.ease === 'string' ? clip(v.ease, 40) : (v.ease ? 'custom' : null),
                  repeat: typeof v.repeat === 'number' ? (v.repeat < 0 ? 'infinite' : String(v.repeat)) : null,
                  props: Object.keys(v).filter(k => !skip.has(k) && k.indexOf('on') !== 0).slice(0, 8),
                  values
                });
                tweens++;
              }
            }
          } catch (e) { }

          const triggers = [];
          try {
            const ST = w.ScrollTrigger;
            if (ST && ST.getAll) {
              for (const st of ST.getAll()) {
                if (triggers.length >= MAX_TRIGGERS) { break; }
                const v = st.vars || {};
                triggers.push({
                  trigger: describe(st.trigger),
                  start: typeof v.start === 'string' ? clip(v.start, 40) : null,
                  end: typeof v.end === 'string' ? clip(v.end, 40) : null,
                  scrub: v.scrub === true ? 'true' : (typeof v.scrub === 'number' ? String(v.scrub) : null),
                  pinned: !!v.pin
                });
              }
            }
          } catch (e) { }

          return JSON.stringify({ libs, counts, animations, triggers });
        })()
        """;
}
