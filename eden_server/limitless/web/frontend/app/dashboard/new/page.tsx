"use client";

import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { apiGet, apiPost, ApiError, WikiClass } from "@/lib/api";
import { useRequireAuth } from "@/lib/auth";
import { GameIcon } from "@/components/icon-img";
import { useT } from "@/components/i18n-provider";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { cn } from "@/lib/utils";

export default function NewCharacterPage() {
  const { ok, token } = useRequireAuth();
  const router = useRouter();
  const t = useT();
  const [name, setName] = useState("");
  const [classId, setClassId] = useState(1);
  const [gender, setGender] = useState("0");
  const [classes, setClasses] = useState<WikiClass[]>([]);
  const [err, setErr] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    apiGet<{ items: WikiClass[] }>("/api/wiki/classes?size=400")
      .then((p) => {
        // Classes de départ : non-éveillées, prérequis « Beginning Job ».
        const base = p.items.filter(
          (c) => !c.awakened &&
            (c.requirement ?? "").toLowerCase().includes("beginning"),
        );
        setClasses(base.length > 0 ? base : p.items.filter((c) => !c.awakened));
        if (base.length > 0) setClassId(base[0].id);
      })
      .catch(() => {});
  }, []);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setErr(null);
    try {
      await apiPost("/api/character", {
        name, race: 1, classId,
        gender: Number(gender),
        hair: 1, face: 1, hairColor: 1, skinColor: 1,
        eyes: 1, eyes2: 1, flag: 0,
      }, token);
      router.push("/dashboard");
    } catch (ex) {
      setErr(ex instanceof ApiError ? ex.message : t("dash.createErr"));
    } finally {
      setBusy(false);
    }
  };

  if (!ok) return null;

  return (
    <div className="mx-auto max-w-lg">
      <h1 className="font-display text-2xl font-bold text-ink">
        {t("dash.newTitle")}
      </h1>
      <form onSubmit={submit} className="panel mt-4 space-y-5 px-5 py-5">
        <div className="space-y-2">
          <Label htmlFor="n">{t("dash.nameLabel")}</Label>
          <input
            id="n" value={name} maxLength={16} required
            onChange={(e) => setName(e.target.value)}
            className="w-full rounded-md border border-raised bg-night/60 px-3 py-2 text-sm"
          />
        </div>
        <div className="space-y-2">
          <Label>{t("dash.startClass")}</Label>
          <div className="grid grid-cols-2 gap-2 sm:grid-cols-3">
            {classes.map((c) => (
              <button
                type="button" key={c.id}
                onClick={() => setClassId(c.id)}
                className={cn(
                  "panel-raised flex items-center gap-2 p-2 text-left text-sm transition-colors",
                  classId === c.id && "border-gold/60 text-gold",
                )}
              >
                <GameIcon code={c.icon} name={c.name} size={28} />
                <span className="truncate">{c.name ?? `#${c.id}`}</span>
              </button>
            ))}
          </div>
        </div>
        <div className="space-y-2">
          <Label>{t("dash.appearance")}</Label>
          <div className="flex gap-2">
            {[
              { v: "0", l: t("dash.masc") },
              { v: "1", l: t("dash.fem") },
            ].map((o) => (
              <button
                type="button" key={o.v}
                onClick={() => setGender(o.v)}
                className={cn(
                  "rounded-md border border-raised px-4 py-2 text-sm",
                  gender === o.v
                    ? "border-gold/60 text-gold"
                    : "text-mutedx hover:text-ink",
                )}
              >
                {o.l}
              </button>
            ))}
          </div>
        </div>
        {err && <p className="text-sm text-ember">{err}</p>}
        <Button type="submit" className="w-full" disabled={busy}>
          {busy ? t("dash.creating") : t("dash.create")}
        </Button>
      </form>
    </div>
  );
}
