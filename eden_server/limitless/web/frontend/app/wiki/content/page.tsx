import Link from "next/link";
import { serverGet } from "@/lib/api";

// Wiki du contenu statique (base eden_content, PLAN-ACTION-3 lot D) : liste des types avec compteurs ;
// la navigation détaillée (fiches, « comment l'obtenir », villes de guilde) est servie par /wiki/ côté API.
type Kind = { kind: string; total: number; inScope: number };

const LABELS: Record<string, string> = {
  items: "Objets", monsters: "Monstres", npcs: "PNJ", quests: "Quêtes", maps: "Cartes", recipes: "Recettes",
};

export default async function ContentWikiPage() {
  const kinds = await serverGet<Kind[]>("/api/content/kinds");
  return (
    <main className="mx-auto max-w-3xl p-4">
      <h1 className="text-2xl font-semibold">Wiki du contenu d&apos;Eden</h1>
      <p className="text-dim text-sm">Seul le contenu présent en jeu et de niveau ≤ 90 est affiché par défaut.</p>
      <div className="mt-4 grid gap-3 sm:grid-cols-2">
        {kinds.map((k) => (
          <a key={k.kind} href={`/wiki/?k=${k.kind}`} className="panel block p-4">
            <div className="text-lg">{LABELS[k.kind] ?? k.kind}</div>
            <div className="text-dim text-sm">{k.inScope} en jeu ≤ Lv90 · {k.total} au total</div>
          </a>
        ))}
      </div>
      <p className="mt-6 text-sm">
        <Link href="/wiki" className="underline">Retour au wiki historique</Link>
      </p>
    </main>
  );
}
