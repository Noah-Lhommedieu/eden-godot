/// Échelle Limitless — le motif récurrent du site (DESIGN-WEB) :
/// barres or→aurora proportionnelles à l'exp du top, révélées au
/// chargement (une seule orchestration, reduced-motion respecté).
export function LimitlessLadder({
  rows,
  emptyLabel = "No Ultimate exp yet.",
  ultLabel = "Ult.",
}: {
  rows: { name: string; value: number; level?: number }[];
  emptyLabel?: string;
  ultLabel?: string;
}) {
  if (rows.length === 0) {
    return <p className="py-6 text-sm text-dim">{emptyLabel}</p>;
  }
  const max = Math.max(...rows.map((r) => r.value), 1);
  return (
    <ol className="space-y-2">
      {rows.map((r, i) => (
        <li key={r.name} className="flex items-center gap-3">
          <span className="w-5 text-right font-display text-sm text-gold">
            {i + 1}
          </span>
          <div className="min-w-0 flex-1">
            <div className="flex items-baseline justify-between gap-2">
              <span className="truncate text-sm">{r.name}</span>
              <span className="text-xs text-dim tabular-nums">
                {r.level ? `${ultLabel} ${r.level} · ` : ""}
                {r.value.toLocaleString()}
              </span>
            </div>
            <div className="mt-1 h-1.5 overflow-hidden rounded-full bg-raised/60">
              <div
                className="ladder-bar h-full rounded-full"
                style={{
                  width: `${Math.max((r.value / max) * 100, 2)}%`,
                  animationDelay: `${i * 60}ms`,
                  background:
                    "linear-gradient(90deg, var(--gold), var(--aurora))",
                }}
              />
            </div>
          </div>
        </li>
      ))}
    </ol>
  );
}
