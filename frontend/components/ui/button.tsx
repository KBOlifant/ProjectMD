import Link from "next/link";
import type { AnchorHTMLAttributes, ButtonHTMLAttributes } from "react";

// primary = plum (every in-app action), accent = violet (the one hero call to action),
// secondary = raised neutral, ghost = outline only.
export type ButtonVariant = "primary" | "accent" | "secondary" | "ghost";

const base =
  "ts-mono-ui inline-flex items-center justify-center gap-2 rounded-md border px-4 py-2 cursor-pointer transition-[color,background-color,border-color,scale] hover:scale-[1.03] " +
  "disabled:pointer-events-none disabled:border-border-subtle disabled:bg-bg-surface disabled:text-ink-dim disabled:shadow-none " +
  "aria-disabled:pointer-events-none aria-disabled:border-border-subtle aria-disabled:bg-bg-surface aria-disabled:text-ink-dim aria-disabled:shadow-none";

const variants: Record<ButtonVariant, string> = {
  primary: "border-transparent bg-plum-500 text-on-primary hover:bg-plum-400 active:bg-plum-700",
  accent: "border-transparent bg-violet-500 text-on-primary hover:bg-violet-400 active:bg-violet-600",
  secondary: "border-border-strong bg-bg-surface text-ink-body hover:bg-bg-raised hover:text-ink",
  ghost: "border-border-strong bg-transparent text-ink-muted hover:bg-bg-surface hover:text-ink",
};

// Builds the CSS class string for a button style so buttons and links look the same. Stores nothing.
export function buttonClass(variant: ButtonVariant = "primary", className = "") {
  return `${base} ${variants[variant]} ${className}`.trim();
}

// A styled <button> (defaults to type="button"). Stores nothing.
export function Button({
  variant = "primary",
  className,
  type = "button",
  ...props
}: ButtonHTMLAttributes<HTMLButtonElement> & { variant?: ButtonVariant }) {
  return <button type={type} className={buttonClass(variant, className)} {...props} />;
}

export function ButtonLink({
  variant = "primary",
  className,
  href,
  ...props
}: Omit<AnchorHTMLAttributes<HTMLAnchorElement>, "href"> & { variant?: ButtonVariant; href: string }) {
  return <Link href={href} className={buttonClass(variant, className)} {...props} />;
}
