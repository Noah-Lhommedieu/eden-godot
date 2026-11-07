import * as React from "react";
import * as Lucide from "lucide-react";
import { cn } from "cn";

type IconPlaceholderProps = React.ComponentProps<"span"> & {
  lucide?: string;
  tabler?: string;
  hugeicons?: string;
  phosphor?: string;
  remixicon?: string;
};

/// Rend l'icône lucide nommée si `lucide` est fourni, sinon un
/// carré discret (placeholder d'icône font du registry shadcn).
export function IconPlaceholder({
  lucide,
  tabler: _t,
  hugeicons: _h,
  phosphor: _p,
  remixicon: _r,
  className,
  ...props
}: IconPlaceholderProps) {
  if (lucide) {
    const name = lucide
      .replace(/[-_](.)/g, (_, c) => c.toUpperCase())
      .replace(/^(.)/, (_, c) => c.toUpperCase());
    const Cmp = (Lucide as Record<string, unknown>)[name];
    if (typeof Cmp === "function") {
      const Icon = Cmp as React.ComponentType<{ className?: string }>;
      return <Icon className={cn("size-4", className)} />;
    }
  }
  return (
    <span
      aria-hidden
      className={cn(
        "inline-block size-4 rounded-[3px] bg-current opacity-40",
        className
      )}
      {...props}
    />
  );
}
