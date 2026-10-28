// Shared Framer Motion entrance variants: a parent with STAGGER_LIST reveals its STAGGER_ITEM
// children one after another (fade and rise), instead of everything appearing at once.
export const STAGGER_LIST = { initial: {}, animate: { transition: { staggerChildren: 0.08 } } };
// A larger rise for rows that slide up into place as they scroll into view (recent scans).
export const RISE_ITEM = {
  initial: { opacity: 0, y: 64 },
  animate: { opacity: 1, y: 0, transition: { duration: 0.7, ease: [0.22, 1, 0.36, 1] as const } },
};
export const STAGGER_ITEM = {
  initial: { opacity: 0, y: 14 },
  animate: { opacity: 1, y: 0, transition: { duration: 0.45, ease: "easeOut" as const } },
};
