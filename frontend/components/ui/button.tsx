import Link from "next/link";
import type { AnchorHTMLAttributes, ButtonHTMLAttributes } from "react";

export type ButtonVariant = "primary" | "secondary" | "ghost";

const base =
  "ts-mono-ui inline-flex items-center justify-center gap-2 rounded-md border px-5 py-2.5 transition-colors " +
  "disabled:pointer-events-none disabled:border-border-subtle disabled:bg-bg-surface disabled:text-ink-dim disabled:shadow-none " +
  "aria-disabled:pointer-events-none aria-disabled:border-border-subtle aria-disabled:bg-bg-surface aria-disabled:text-ink-dim aria-disabled:shadow-none";

const variants: Record<ButtonVariant, string> = {
  primary:
    "border-transparent bg-mulberry-500 text-on-primary shadow-glow hover:bg-mulberry-400 active:bg-fig-700",
  secondary: "border-border-strong bg-bg-raised text-ink-body hover:text-ink",
  ghost: "border-border-subtle bg-transparent text-ink-muted hover:text-ink",
};

// Builds the CSS class string for a button style (primary/secondary/ghost) so buttons and links look the same. Stores nothing.
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
