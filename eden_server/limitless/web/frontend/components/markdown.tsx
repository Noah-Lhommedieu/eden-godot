import Link from "next/link";
import React from "react";

/// Rendu markdown-lite des guides du CMS (wiki_articles). Volontairement
/// limité : titres ##/###, **gras**, *italique*, `code`, listes à puces,
/// liens [texte](url). Le texte n'est JAMAIS injecté en HTML brut —
/// tout passe par des nœuds React (pas de XSS possible).
function inline(text: string, keyBase: string): React.ReactNode[] {
  const out: React.ReactNode[] = [];
  // Découpe sur les motifs inline : **b**, *i*, `c`, [t](u).
  const re = /(\*\*[^*]+\*\*|\*[^*\n]+\*|`[^`\n]+`|\[[^\]\n]+\]\([^)\n]+\))/g;
  text.split(re).forEach((part, i) => {
    const k = `${keyBase}-${i}`;
    if (part.startsWith("**") && part.endsWith("**")) {
      out.push(<strong key={k} className="font-semibold text-ink">{part.slice(2, -2)}</strong>);
    } else if (part.startsWith("*") && part.endsWith("*") && part.length > 2) {
      out.push(<em key={k}>{part.slice(1, -1)}</em>);
    } else if (part.startsWith("`") && part.endsWith("`") && part.length > 2) {
      out.push(
        <code key={k}
          className="rounded border border-raised bg-night/70 px-1 font-mono text-[0.85em] text-aurora">
          {part.slice(1, -1)}
        </code>,
      );
    } else if (part.startsWith("[")) {
      const m = part.match(/^\[([^\]]+)\]\(([^)]+)\)$/);
      if (m) {
        const href = m[2];
        const internal = href.startsWith("/");
        out.push(
          internal
            ? <Link key={k} href={href} className="text-gold hover:underline">{m[1]}</Link>
            : <a key={k} href={href} rel="noreferrer" target="_blank"
                className="text-gold hover:underline">{m[1]}</a>,
        );
        return;
      }
      out.push(part);
    } else if (part) {
      out.push(part);
    }
  });
  return out;
}

export function Markdown({ text }: { text: string }) {
  const blocks: React.ReactNode[] = [];
  const paragraphs = text.split(/\n{2,}/);
  paragraphs.forEach((p, pi) => {
    const lines = p.split("\n");
    if (lines.every((l) => l.trimStart().startsWith("- "))) {
      blocks.push(
        <ul key={pi} className="list-disc space-y-1 pl-5">
          {lines.map((l, i) => (
            <li key={i}>{inline(l.trimStart().slice(2), `${pi}-${i}`)}</li>
          ))}
        </ul>,
      );
      return;
    }
    const nodes = lines.flatMap((l, i) => {
      const h = l.match(/^(#{1,3})\s+(.*)$/);
      if (h) {
        const lvl = h[1].length;
        const cls = lvl === 1
          ? "font-display text-2xl font-bold text-ink"
          : lvl === 2
            ? "font-display text-xl font-bold text-gold"
            : "font-display text-base font-bold text-ink";
        return [
          <h2 key={`h${i}`} className={cls + (i > 0 ? " mt-4" : "")}>
            {inline(h[2], `${pi}h${i}`)}
          </h2>,
        ];
      }
      return [
        <p key={`p${i}`} className="leading-relaxed">
          {inline(l, `${pi}p${i}`)}
        </p>,
      ];
    });
    blocks.push(
      <div key={pi} className="space-y-2">{nodes}</div>,
    );
  });
  return <div className="space-y-4 text-sm text-ink/85">{blocks}</div>;
}
