import { TrashIcon } from "./icons";

interface DeleteButtonProps {
  // Names the thing being deleted for screen readers and the tooltip, e.g. "example.com".
  label: string;
  onClick: () => void;
  disabled?: boolean;
  // Tooltip shown instead of the default (for example why the button is disabled).
  title?: string;
  className?: string;
}

// A dark red outlined trash-can icon button (no fill). On hover or keyboard focus the word "Delete"
// slides out beside the icon; with reduced motion it just appears. Display only; stores nothing.
export function DeleteButton({ label, onClick, disabled = false, title, className = "" }: DeleteButtonProps) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      aria-label={`Delete ${label}`}
      title={title ?? `Delete ${label}`}
      className={
        "ts-mono-ui group inline-flex cursor-pointer items-center rounded-md border border-danger-strong bg-transparent px-2.5 py-1.5 text-danger-strong " +
        "transition-colors hover:border-[#a82438] hover:text-[#a82438] focus-visible:border-[#a82438] focus-visible:text-[#a82438] " +
        "disabled:pointer-events-none disabled:border-border-subtle disabled:text-ink-dim " +
        className
      }
    >
      <TrashIcon size={14} />
      <span
        aria-hidden="true"
        className="max-w-0 overflow-hidden whitespace-nowrap opacity-0 transition-[max-width,opacity,margin] duration-200 group-hover:ml-2 group-hover:max-w-16 group-hover:opacity-100 group-focus-visible:ml-2 group-focus-visible:max-w-16 group-focus-visible:opacity-100 motion-reduce:transition-none"
      >
        Delete
      </span>
    </button>
  );
}
