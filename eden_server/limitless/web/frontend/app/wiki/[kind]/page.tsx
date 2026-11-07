import { redirect } from "next/navigation";
import { DATASETS } from "@/lib/datasets";

// Alias d'URL : /wiki/<kind>?f=…&q=… → /wiki?kind=<kind>&f=…
// (liens générés dans les fiches — ex. « utilisé dans quel craft »
// pointait sur /wiki/crafts qui 404). /wiki/talents reste servi par
// sa route statique (priorité Next sur le segment dynamique).
export default async function WikiKindPage({
  params,
  searchParams,
}: {
  params: Promise<{ kind: string }>;
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}) {
  const { kind } = await params;
  const sp = await searchParams;
  const qs = new URLSearchParams();
  if (DATASETS.some((d) => d.id === kind)) qs.set("kind", kind);
  for (const [k, v] of Object.entries(sp)) {
    if (v === undefined) continue;
    for (const one of Array.isArray(v) ? v : [v]) qs.append(k, one);
  }
  redirect(`/wiki?${qs.toString()}`);
}
