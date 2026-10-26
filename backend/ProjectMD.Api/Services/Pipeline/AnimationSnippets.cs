using System.Globalization;

namespace ProjectMD.Api.Services.Pipeline;

// Fixed code templates for rebuilding an animation with GSAP or Framer Motion. The only inputs are
// the trigger word (one of MotionNotesLimits.Triggers) and a duration in seconds, so no text from
// the page or from the AI can end up inside the code. The result is shown as text and copied; it is
// never run by the server. The ".target" selector is a placeholder the reader replaces.
//
// Every trigger word has its own case. "load" used to fall through to the same catch-all branch as
// "page-transition" and "unknown", and the AI's most common trigger by far is "load" (most measured
// durations also land on the 600ms default), so nearly every animation on every scan rendered the
// exact same snippet. Keep each trigger distinct, and keep the catch-all for truly unrecognised
// values only.
internal static class AnimationSnippets
{
    // The default duration in milliseconds when the browser measured none.
    public const int DefaultDurationMs = 600;

    // GSAP code for the trigger.
    public static string Gsap(string trigger, int durationMs)
    {
        var d = Seconds(durationMs);
        return trigger switch
        {
            "scroll" =>
                $"gsap.registerPlugin(ScrollTrigger);\n\ngsap.from(\".target\", {{\n  opacity: 0,\n  y: 40,\n  duration: {d},\n  ease: \"power2.out\",\n  scrollTrigger: {{ trigger: \".target\", start: \"top 80%\" }},\n}});",
            "scroll-scrub" =>
                "gsap.registerPlugin(ScrollTrigger);\n\ngsap.to(\".target\", {\n  y: -120,\n  ease: \"none\",\n  scrollTrigger: {\n    trigger: \".target\",\n    start: \"top bottom\",\n    end: \"bottom top\",\n    scrub: true,\n  },\n});",
            "hover" =>
                $"const el = document.querySelector(\".target\");\nel.addEventListener(\"mouseenter\", () => gsap.to(el, {{ scale: 1.05, duration: {d} }}));\nel.addEventListener(\"mouseleave\", () => gsap.to(el, {{ scale: 1, duration: {d} }}));",
            "click" =>
                $"const el = document.querySelector(\".target\");\nel.addEventListener(\"click\", () => {{\n  gsap.fromTo(el, {{ scale: 0.95 }}, {{ scale: 1, duration: {d}, ease: \"back.out(2)\" }});\n}});",
            "loop" =>
                "gsap.to(\".target\", {\n  rotation: 360,\n  duration: 8,\n  ease: \"none\",\n  repeat: -1,\n});",
            "load" =>
                $"gsap.from(\".target\", {{\n  opacity: 0,\n  y: 40,\n  duration: {d},\n  ease: \"power2.out\",\n}});",
            "page-transition" =>
                $"gsap.timeline()\n  .to(\".target\", {{ opacity: 0, duration: {d}, ease: \"power1.in\" }})\n  .set(\".target\", {{ /* swap page content here */ }})\n  .to(\".target\", {{ opacity: 1, duration: {d}, ease: \"power1.out\" }});",
            _ =>
                $"gsap.from(\".target\", {{\n  opacity: 0,\n  duration: {d},\n  ease: \"power1.out\",\n}});",
        };
    }

    // Framer Motion (React) code for the trigger.
    public static string FramerMotion(string trigger, int durationMs)
    {
        var d = Seconds(durationMs);
        return trigger switch
        {
            "scroll" =>
                $"<motion.div\n  initial={{{{ opacity: 0, y: 40 }}}}\n  whileInView={{{{ opacity: 1, y: 0 }}}}\n  viewport={{{{ once: true, amount: 0.3 }}}}\n  transition={{{{ duration: {d}, ease: \"easeOut\" }}}}\n/>",
            "scroll-scrub" =>
                "const { scrollYProgress } = useScroll({ target: ref, offset: [\"start end\", \"end start\"] });\nconst y = useTransform(scrollYProgress, [0, 1], [0, -120]);\n\n<motion.div ref={ref} style={{ y }} />",
            "hover" =>
                $"<motion.div\n  whileHover={{{{ scale: 1.05 }}}}\n  transition={{{{ duration: {d} }}}}\n/>",
            "click" =>
                $"<motion.button\n  whileTap={{{{ scale: 0.95 }}}}\n  transition={{{{ duration: {d} }}}}\n/>",
            "loop" =>
                "<motion.div\n  animate={{ rotate: 360 }}\n  transition={{ duration: 8, repeat: Infinity, ease: \"linear\" }}\n/>",
            "load" =>
                $"<motion.div\n  initial={{{{ opacity: 0, y: 40 }}}}\n  animate={{{{ opacity: 1, y: 0 }}}}\n  transition={{{{ duration: {d}, ease: \"easeOut\" }}}}\n/>",
            "page-transition" =>
                $"<AnimatePresence mode=\"wait\">\n  <motion.div\n    key={{pageKey}}\n    initial={{{{ opacity: 0 }}}}\n    animate={{{{ opacity: 1 }}}}\n    exit={{{{ opacity: 0 }}}}\n    transition={{{{ duration: {d} }}}}\n  />\n</AnimatePresence>",
            _ =>
                $"<motion.div\n  initial={{{{ opacity: 0 }}}}\n  animate={{{{ opacity: 1 }}}}\n  transition={{{{ duration: {d}, ease: \"easeOut\" }}}}\n/>",
        };
    }

    // Plain CSS for the trigger: a @keyframes animation for anything that plays on its own, and a
    // transition (with the matching pseudo-class) for hover/click. Used when the animation doesn't
    // look like it is running through GSAP or Framer Motion at all.
    public static string Css(string trigger, int durationMs)
    {
        var d = Seconds(durationMs);
        return trigger switch
        {
            "scroll" or "load" =>
                $"@keyframes reveal {{\n  from {{ opacity: 0; transform: translateY(40px); }}\n  to {{ opacity: 1; transform: translateY(0); }}\n}}\n\n.target {{\n  animation: reveal {d}s ease-out {(trigger == "scroll" ? "both" : "forwards")};\n}}",
            "scroll-scrub" =>
                ".target {\n  animation-timeline: scroll();\n  animation-name: scroll-move;\n  animation-fill-mode: both;\n}\n\n@keyframes scroll-move {\n  from { transform: translateY(0); }\n  to { transform: translateY(-120px); }\n}",
            "hover" =>
                $".target {{\n  transition: transform {d}s;\n}}\n.target:hover {{\n  transform: scale(1.05);\n}}",
            "click" =>
                $".target:active {{\n  transform: scale(0.95);\n  transition: transform {d}s;\n}}",
            "loop" =>
                ".target {\n  animation: spin 8s linear infinite;\n}\n\n@keyframes spin {\n  to { transform: rotate(360deg); }\n}",
            "page-transition" =>
                $"@keyframes fade {{\n  0% {{ opacity: 1; }}\n  50% {{ opacity: 0; }}\n  100% {{ opacity: 1; }}\n}}\n\n.target {{\n  animation: fade {d}s ease-in-out;\n}}",
            _ =>
                $"@keyframes fade-in {{\n  from {{ opacity: 0; }}\n  to {{ opacity: 1; }}\n}}\n\n.target {{\n  animation: fade-in {d}s ease-out forwards;\n}}",
        };
    }

    // Milliseconds as seconds with at most two decimals, for example 600 -> "0.6".
    internal static string Seconds(int durationMs) =>
        (Math.Clamp(durationMs, 100, 5000) / 1000.0).ToString("0.##", CultureInfo.InvariantCulture);
}
