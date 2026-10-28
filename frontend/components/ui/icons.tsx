import type { SVGProps } from "react";

type IconProps = SVGProps<SVGSVGElement> & { size?: number };

// Shared 16x16 line-icon wrapper (1.5px stroke, currentColor). `size` sets the rendered pixels;
// the glyphs below are drawn on a 16-unit grid. Stores nothing.
function Icon({ children, size = 14, ...props }: IconProps) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 16 16"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.5"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      {...props}
    >
      {children}
    </svg>
  );
}

// Navigation glyphs ---------------------------------------------------------------------------
export const SparkIcon = (p: IconProps) => (
  <Icon {...p}><path d="M6.5 2.5 7.7 6l3.5 1.2L7.7 8.4 6.5 12 5.3 8.4 1.8 7.2 5.3 6 6.5 2.5ZM12 1.5v3M10.5 3h3M12 11v3M10.5 12.5h3" /></Icon>
);
export const ScanIcon = (p: IconProps) => (
  <Icon {...p}><path d="M2 5.5V3.5A1.5 1.5 0 0 1 3.5 2h2M10.5 2h2A1.5 1.5 0 0 1 14 3.5v2M14 10.5v2a1.5 1.5 0 0 1-1.5 1.5h-2M5.5 14h-2A1.5 1.5 0 0 1 2 12.5v-2" /><circle cx="8" cy="8" r="2" /></Icon>
);
export const FileIcon = (p: IconProps) => (
  <Icon {...p}><path d="M4 1.5h5L12.5 5v9.5h-8.5V1.5Z" /><path d="M9 1.5V5h3.5M6 8.5h4M6 11h4" /></Icon>
);
export const HistoryIcon = (p: IconProps) => (
  <Icon {...p}><path d="M2.2 8a5.8 5.8 0 1 0 1.9-4.3L2 5.8M2 2.5v3.3h3.3M8 4.8V8l2.2 1.4" /></Icon>
);
export const UsersIcon = (p: IconProps) => (
  <Icon {...p}><circle cx="6" cy="5.5" r="2.5" /><path d="M1.5 14c0-2.5 2-4 4.5-4s4.5 1.5 4.5 4M10.5 3.2a2.5 2.5 0 0 1 0 4.6M12 10.3c1.5.5 2.5 1.8 2.5 3.7" /></Icon>
);

// Actions ---------------------------------------------------------------------------------------
export const CopyIcon = (p: IconProps) => (
  <Icon {...p}><rect x="5.5" y="5.5" width="8" height="8" rx="1.5" /><path d="M10.5 5.5v-2a1.5 1.5 0 0 0-1.5-1.5H3.5A1.5 1.5 0 0 0 2 3.5V9a1.5 1.5 0 0 0 1.5 1.5h2" /></Icon>
);
export const DownloadIcon = (p: IconProps) => (
  <Icon {...p}><path d="M8 2v8M4.5 7 8 10.5 11.5 7M2.5 13.5h11" /></Icon>
);
export const CheckIcon = (p: IconProps) => (
  <Icon {...p}><path d="m3 8.5 3.2 3L13 4.5" /></Icon>
);
export const ArrowRightIcon = (p: IconProps) => (
  <Icon {...p}><path d="M2.5 8h11M9 3.5 13.5 8 9 12.5" /></Icon>
);
export const EnterIcon = (p: IconProps) => (
  <Icon {...p}><path d="M13 3v5.5a1.5 1.5 0 0 1-1.5 1.5H3.5M6 7.5 3.5 10 6 12.5" /></Icon>
);
export const CodeIcon = (p: IconProps) => (
  <Icon {...p}><path d="m5.5 4.5-3.5 3.5 3.5 3.5M10.5 4.5 14 8l-3.5 3.5M9 3 7 13" /></Icon>
);

// Results tab glyphs ----------------------------------------------------------------------------
export const PaletteIcon = (p: IconProps) => (
  <Icon {...p}><circle cx="8" cy="8" r="6" /><path d="M8 2v12M2 8h12M3.8 3.8l8.4 8.4" /></Icon>
);
export const GridIcon = (p: IconProps) => (
  <Icon {...p}><rect x="2" y="2" width="5" height="5" rx="1" /><rect x="9" y="2" width="5" height="5" rx="1" /><rect x="2" y="9" width="5" height="5" rx="1" /><rect x="9" y="9" width="5" height="5" rx="1" /></Icon>
);
export const MotionIcon = (p: IconProps) => (
  <Icon {...p}><path d="M2 5h8M2 8h12M2 11h8M12 3.5 14 5.5 12 7.5" /></Icon>
);

// Tech-fingerprint glyphs (generic shapes, not brand logos) -----------------------------------
export const AtomIcon = (p: IconProps) => (
  <Icon {...p}><circle cx="8" cy="8" r="1.2" /><ellipse cx="8" cy="8" rx="6" ry="2.6" /><ellipse cx="8" cy="8" rx="6" ry="2.6" transform="rotate(60 8 8)" /><ellipse cx="8" cy="8" rx="6" ry="2.6" transform="rotate(120 8 8)" /></Icon>
);
export const TriangleIcon = (p: IconProps) => (
  <Icon {...p}><path d="M8 2.5 14 13H2L8 2.5Z" /></Icon>
);
export const WaveIcon = (p: IconProps) => (
  <Icon {...p}><path d="M1.5 6.5c1.5-3 3-3 4.5-1.2S9 8.3 10.5 6.5s3-3 4 0M1.5 11.5c1.5-3 3-3 4.5-1.2S9 13.3 10.5 11.5s3-3 4 0" /></Icon>
);
export const DiscIcon = (p: IconProps) => (
  <Icon {...p}><circle cx="8" cy="8" r="6" /><path d="M2.5 6.5h11M2.5 9.5h11" /></Icon>
);
export const UserSquareIcon = (p: IconProps) => (
  <Icon {...p}><rect x="2" y="2" width="12" height="12" rx="2.5" /><circle cx="8" cy="6.5" r="1.8" /><path d="M4.5 12c.6-1.6 1.9-2.4 3.5-2.4s2.9.8 3.5 2.4" /></Icon>
);
export const SearchIcon = (p: IconProps) => (
  <Icon {...p}><circle cx="7" cy="7" r="4.5" /><path d="m10.5 10.5 3.5 3.5" /></Icon>
);
export const CubeIcon = (p: IconProps) => (
  <Icon {...p}><path d="M8 1.8 13.5 5v6L8 14.2 2.5 11V5L8 1.8ZM2.8 5.1 8 8.1l5.2-3M8 8.1v6" /></Icon>
);
