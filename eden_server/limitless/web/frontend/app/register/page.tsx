"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { apiPost, ApiError } from "@/lib/api";
import { useT } from "@/components/i18n-provider";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";

export default function RegisterPage() {
  const router = useRouter();
  const t = useT();
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [email, setEmail] = useState("");
  const [err, setErr] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setErr(null);
    try {
      await apiPost("/api/account/register", { username, password, email });
      router.push("/login");
    } catch (ex) {
      setErr(
        ex instanceof ApiError && ex.status === 409
          ? t("auth.nameTaken")
          : t("auth.registerErr"),
      );
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="mx-auto mt-14 max-w-md">
      <div className="panel overflow-hidden">
        {/* Bandeau jeu — illustration réelle (scène chaude). */}
        <div className="relative h-28">
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img
            src="/art/scene-warm.webp" alt=""
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
          {t("auth.signupTitle")}
        </h1>
        <p className="mt-1 text-sm text-dim">{t("auth.signupSub")}</p>
        <form onSubmit={submit} className="mt-5 space-y-4">
          <div className="space-y-2">
            <Label htmlFor="u">{t("auth.account")}</Label>
            <input
              id="u" value={username} required minLength={3}
              autoComplete="username"
              onChange={(e) => setUsername(e.target.value)}
              className="w-full rounded-md border border-raised bg-night/60 px-3 py-2 text-sm"
            />
          </div>
          <div className="space-y-2">
            <Label htmlFor="e">{t("auth.email")}</Label>
            <input
              id="e" type="email" value={email}
              autoComplete="email"
              onChange={(e) => setEmail(e.target.value)}
              className="w-full rounded-md border border-raised bg-night/60 px-3 py-2 text-sm"
            />
          </div>
          <div className="space-y-2">
            <Label htmlFor="p">{t("auth.password")}</Label>
            <input
              id="p" type="password" value={password} required minLength={6}
              autoComplete="new-password"
              onChange={(e) => setPassword(e.target.value)}
              className="w-full rounded-md border border-raised bg-night/60 px-3 py-2 text-sm"
            />
          </div>
          {err && <p className="text-sm text-ember">{err}</p>}
          <Button type="submit" className="w-full" disabled={busy}>
            {busy ? t("auth.creating") : t("auth.signup")}
          </Button>
          <p className="text-center text-sm text-dim">
            {t("auth.haveAccount")}{" "}
            <Link href="/login" className="text-gold hover:underline">
              {t("auth.signin")}
            </Link>
          </p>
        </form>
        </div>
      </div>
    </div>
  );
}
