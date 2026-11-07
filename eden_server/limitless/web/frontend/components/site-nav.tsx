"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import {
  apiGet, ApiError, clearSession, getName, getRole, getToken,
  saveSession, ServerStatus,
} from "@/lib/api";
import { useT } from "@/components/i18n-provider";
import { LangSwitch } from "@/components/lang-switch";
import { cn } from "@/lib/utils";

/// Chip live « N en ligne » — interroge /api/admin/status toutes les
/// 60 s. Discret quand l'API est coupée (point gris, pas de panique).
function OnlineChip() {
  const t = useT();
  const [status, setStatus] = useState<ServerStatus | null>(null);
  const [up, setUp] = useState(false);
  useEffect(() => {
    let stop = false;
    const tick = () =>
      apiGet<ServerStatus>("/api/admin/status")
        .then((s) => { if (!stop) { setStatus(s); setUp(true); } })
        .catch(() => { if (!stop) setUp(false); });
    tick();
    const tmr = setInterval(tick, 60_000);
    return () => { stop = true; clearInterval(tmr); };
  }, []);
  const online = status?.online ?? 0;
  return (
    <span
      title={up
        ? t("nav.onlineTitle", { n: online })
        : t("nav.statusUnknown")}
      className="inline-flex items-center gap-1.5 rounded-full border border-raised px-2.5 py-1 text-xs text-dim"
    >
      <span
        className={cn(
          "size-1.5 rounded-full",
          !up ? "bg-mutedx" : online > 0 ? "live-dot bg-aurora" : "bg-ember",
        )}
      />
      <span className="tabular-nums">
        {up ? t("nav.online", { n: online }) : "—"}
      </span>
    </span>
  );
}

export function SiteNav() {
  const pathname = usePathname();
  const router = useRouter();
  const t = useT();
  const [name, setName] = useState<string | null>(null);
  const [role, setRole] = useState<string | null>(null);
  const [open, setOpen] = useState(false);

  useEffect(() => {
    setOpen(false);
    const token = getToken();
    if (!token) {
      setName(null);
      setRole(null);
      return;
    }
    setName(getName());
    setRole(getRole());
    // Le rôle stocké date du login — si le compte a été promu/rétro-
    // gradé depuis, /api/auth/me resynchronise localStorage (retour
    // joueur : bouton admin invisible sur une vieille session).
    let stop = false;
    apiGet<{ name?: string; role?: string }>("/api/auth/me", token)
      .then((me) => {
        if (stop || !me?.role) return;
        const curName = me.name ?? getName() ?? "";
        saveSession(token, me.role, curName);
        setRole(me.role);
        if (me.name) setName(me.name);
      })
      .catch((ex) => {
        if (stop) return;
        // 401 = token expiré/révoqué : la session locale est morte —
        // garder ll_role/ll_name périmés masquait l'entrée admin et
        // affichait un pseudo-connecté fantôme. API coupée (autre
        // erreur) → on garde le local (mode dégradé).
        if (ex instanceof ApiError && ex.status === 401) {
          clearSession();
          setName(null);
          setRole(null);
        }
      });
    return () => { stop = true; };
  }, [pathname]);

  const logout = () => {
    clearSession();
    setName(null);
    router.push("/");
  };

  const LINKS = [
    { href: "/", label: t("nav.home") },
    { href: "/wiki", label: t("nav.wiki") },
    { href: "/wiki/talents", label: t("nav.talents") },
    { href: "/guides", label: t("nav.guides") },
    { href: "/rankings", label: t("nav.rankings") },
  ];

  const linkCls = (href: string) =>
    cn(
      "px-1 py-1 text-sm transition-colors hover:text-gold",
      pathname === href || (href !== "/" && pathname.startsWith(href))
        ? "text-gold border-b-2 border-gold"
        : "text-mutedx",
    );

  const navLinks = (
    <>
      {LINKS.map((l) => (
        <Link key={l.href} href={l.href} className={linkCls(l.href)}>
          {l.label}
        </Link>
      ))}
      {name && (
        <Link href="/dashboard" className={linkCls("/dashboard")}>
          {name}
        </Link>
      )}
      {(role ?? "").toLowerCase() === "admin" && (
        <Link
          href="/admin"
          className="px-1 py-1 text-sm text-ember hover:text-ember/80"
        >
          {t("nav.admin")}
        </Link>
      )}
    </>
  );

  return (
    <nav className="sticky top-0 z-40 border-b border-raised bg-night/90 backdrop-blur">
      <div className="mx-auto flex w-full max-w-6xl items-center gap-6 px-4 py-3 sm:px-6">
        <Link
          href="/"
          className="flex items-center gap-2.5 font-display text-lg font-bold tracking-[0.18em] text-gold"
        >
          {/* Emblème dragon du client (UI/title.png) — la marque. */}
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img
            src="/art/mark.png" alt="" width={30} height={30}
            className="object-contain"
          />
          <span className="hidden min-[400px]:inline">LIMITLESS</span>
        </Link>
        <div className="hidden items-center gap-5 sm:flex">{navLinks}</div>
        <div className="ml-auto hidden items-center gap-4 text-sm sm:flex">
          <OnlineChip />
          <LangSwitch />
          {name ? (
            <button
              onClick={logout}
              className="text-mutedx hover:text-ember"
            >
              {t("nav.signout")}
            </button>
          ) : (
            <>
              <Link href="/login" className="text-mutedx hover:text-gold">
                {t("nav.signin")}
              </Link>
              <Link
                href="/register"
                className="rounded-md bg-gold px-3 py-1.5 font-semibold text-night hover:bg-gold/85"
              >
                {t("nav.signup")}
              </Link>
            </>
          )}
        </div>
        <div className="ml-auto flex items-center gap-3 sm:hidden">
          <LangSwitch compact />
          <button
            className="rounded-md border border-raised px-2 py-1 text-sm text-mutedx"
            onClick={() => setOpen((o) => !o)}
            aria-expanded={open}
            aria-label={t("nav.menu")}
          >
            ☰
          </button>
        </div>
      </div>
      {open && (
        <div className="filet-t flex flex-col gap-2 px-4 py-3 sm:hidden">
          {navLinks}
          <div className="filet-t flex gap-4 pt-2 text-sm">
            {name ? (
              <button onClick={logout} className="text-mutedx">
                {t("nav.signout")}
              </button>
            ) : (
              <>
                <Link href="/login" className="text-mutedx">
                  {t("nav.signin")}
                </Link>
                <Link href="/register" className="text-gold">
                  {t("nav.signup")}
                </Link>
              </>
            )}
          </div>
        </div>
      )}
    </nav>
  );
}
