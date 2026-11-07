"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { apiPost, ApiError, LoginRes, saveSession } from "@/lib/api";
import { useT } from "@/components/i18n-provider";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";

export default function LoginPage() {
  const router = useRouter();
  const t = useT();
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [err, setErr] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setErr(null);
    try {
      const r = await apiPost<LoginRes>("/api/auth/login", {
        username, password,
      });
      saveSession(r.token, r.role, username);
      router.push("/dashboard");
    } catch (ex) {
      setErr(
        ex instanceof ApiError && ex.status === 401
          ? t("auth.wrongCreds")
          : t("common.unreachable"),
      );
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="mx-auto mt-14 max-w-md">
      <div className="panel overflow-hidden">
        {/* Bandeau jeu — illustration réelle (scène du client). */}
        <div className="relative h-28">
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img
            src="/art/scene-violet.webp" alt=""
            className="absolute inset-0 h-full w-full object-cover object-[center_35%] opacity-90"
          />
          <div
            aria-hidden
            className="absolute inset-0"
            style={{
              background:
                "linear-gradient(180deg, transparent 20%, var(--panel) 100%)",
            }}
          />
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img
            src="/art/logo.png" alt="Eden Eternal"
            className="absolute bottom-1 left-1/2 w-24 -translate-x-1/2 drop-shadow-[0_2px_8px_rgba(0,0,0,0.8)]"
          />
        </div>
        <div className="px-6 pb-6 pt-2">
          <h1 className="font-display text-2xl font-bold text-ink">
            {t("auth.signinTitle")}
          </h1>
          <p className="mt-1 text-sm text-dim">{t("auth.signinSub")}</p>
        <form onSubmit={submit} className="mt-5 space-y-4">
          <div className="space-y-2">
            <Label htmlFor="u">{t("auth.account")}</Label>
            <input
              id="u" value={username} required autoComplete="username"
              onChange={(e) => setUsername(e.target.value)}
              className="w-full rounded-md border border-raised bg-night/60 px-3 py-2 text-sm"
            />
          </div>
          <div className="space-y-2">
            <Label htmlFor="p">{t("auth.password")}</Label>
            <input
              id="p" type="password" value={password} required
              autoComplete="current-password"
              onChange={(e) => setPassword(e.target.value)}
              className="w-full rounded-md border border-raised bg-night/60 px-3 py-2 text-sm"
            />
          </div>
          {err && <p className="text-sm text-ember">{err}</p>}
          <Button type="submit" className="w-full" disabled={busy}>
            {busy ? t("auth.signingIn") : t("auth.signin")}
          </Button>
          <p className="text-center text-sm text-dim">
            {t("auth.noAccount")}{" "}
            <Link href="/register" className="text-gold hover:underline">
              {t("auth.createOne")}
            </Link>
          </p>
        </form>
        </div>
      </div>
    </div>
  );
}
