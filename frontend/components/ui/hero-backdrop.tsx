// Full-bleed backdrop for the Generate hero. Drop the final photo at `public/hero.jpg` and it is
// used automatically (it is the top background layer). Until that file exists, the gradient
// underneath shows: deep navy on the left, a graphite fold in the middle, violet on the right,
// the same split as the wireframe. A dark scrim keeps the headline readable on any image.
// Decorative, so hidden from assistive tech. Stores nothing.
const HERO_IMAGE = "/hero.jpg";

const PLACEHOLDER =
  "radial-gradient(ellipse 55% 60% at 12% 8%, rgba(18, 52, 140, 0.55), transparent 70%), " +
  "linear-gradient(112deg, #060c2c 0%, #0a1546 28%, #3e3e41 46%, #5d5d60 55%, #46464a 66%, #2a1046 77%, #1d0838 100%)";

export function HeroBackdrop() {
  return (
    <div aria-hidden="true" className="absolute inset-0 -z-10">
      <div
        className="absolute inset-0 bg-cover bg-center"
        style={{ backgroundImage: `url(${HERO_IMAGE}), ${PLACEHOLDER}` }}
      />
      <div className="absolute inset-0 bg-black/35" />
    </div>
  );
}
