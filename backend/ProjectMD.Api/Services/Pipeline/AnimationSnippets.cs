using System.Globalization;

namespace ProjectMD.Api.Services.Pipeline;

// Fixed code templates for rebuilding an animation with GSAP or Framer Motion. The only inputs are
// the trigger word (one of MotionNotesLimits.Triggers) and a duration in seconds, so no text from
// the page or from the AI can end up inside the code. The result is shown as text and copied; it is
// never run by the server. The ".target" selector is a placeholder the reader replaces.
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
            _ =>
                $"gsap.from(\".target\", {{\n  opacity: 0,\n  y: 40,\n  duration: {d},\n  ease: \"power2.out\",\n}});",
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
            _ =>
                $"<motion.div\n  initial={{{{ opacity: 0, y: 40 }}}}\n  animate={{{{ opacity: 1, y: 0 }}}}\n  transition={{{{ duration: {d}, ease: \"easeOut\" }}}}\n/>",
        };
    }

    // Milliseconds as seconds with at most two decimals, for example 600 -> "0.6".
    internal static string Seconds(int durationMs) =>
        (Math.Clamp(durationMs, 100, 5000) / 1000.0).ToString("0.##", CultureInfo.InvariantCulture);
}
