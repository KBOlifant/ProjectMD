import type { SVGProps } from "react";

// Shared 16x16 SVG wrapper for the icons below (Bolt, Loader, File, Clock, Copy, Download, Check). Stores nothing.
function Icon({ children, ...props }: SVGProps<SVGSVGElement>) {
  return (
    <svg
      width="14"
      height="14"
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

export const BoltIcon = (p: SVGProps<SVGSVGElement>) => (
  <Icon {...p}><path d="M9 1.5 3 9h4.5L7 14.5 13 7H8.5L9 1.5Z" /></Icon>
);
export const LoaderIcon = (p: SVGProps<SVGSVGElement>) => (
  <Icon {...p}><path d="M8 2v3M8 11v3M2 8h3M11 8h3M3.8 3.8l2.1 2.1M10.1 10.1l2.1 2.1M12.2 3.8l-2.1 2.1M5.9 10.1l-2.1 2.1" /></Icon>
);
export const FileIcon = (p: SVGProps<SVGSVGElement>) => (
  <Icon {...p}><path d="M4 1.5h5L12.5 5v9.5h-8.5V1.5Z" /><path d="M9 1.5V5h3.5M6 8.5h4M6 11h4" /></Icon>
);
export const ClockIcon = (p: SVGProps<SVGSVGElement>) => (
  <Icon {...p}><circle cx="8" cy="8" r="6" /><path d="M8 4.5V8l2.5 1.5" /></Icon>
);
export const CopyIcon = (p: SVGProps<SVGSVGElement>) => (
  <Icon {...p}><rect x="5.5" y="5.5" width="8" height="8" rx="1.5" /><path d="M10.5 5.5v-2a1.5 1.5 0 0 0-1.5-1.5H3.5A1.5 1.5 0 0 0 2 3.5V9a1.5 1.5 0 0 0 1.5 1.5h2" /></Icon>
);
export const DownloadIcon = (p: SVGProps<SVGSVGElement>) => (
  <Icon {...p}><path d="M8 2v8M4.5 7 8 10.5 11.5 7M2.5 13.5h11" /></Icon>
);
export const CheckIcon = (p: SVGProps<SVGSVGElement>) => (
  <Icon {...p}><path d="m3 8.5 3.2 3L13 4.5" /></Icon>
);
