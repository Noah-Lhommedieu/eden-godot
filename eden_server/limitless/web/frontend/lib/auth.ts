"use client";

import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import {
  apiGet, ApiError, clearSession, getName, getRole, getToken,
  saveSession,
} from "@/lib/api";

/** Redirige vers /login si pas de token ; `role` exige un rôle JWT
 *  (comparaison insensible à la casse). `forbidden` = connecté mais
 *  pas le bon rôle — l'appelant affiche un message, pas de boucle.
 *
 *  Le rôle local (ll_role, écrit au login) peut être périmé : un
 *  compte promu Admin garde un ll_role « User » tant qu'il ne se
 *  reconnecte pas. Avant de conclure « interdit », on interroge
 *  /api/auth/me — le backend relit accounts.role en DB à chaque
 *  requête (DbRoleClaimsTransformer), donc la réponse est fraîche
 *  même avec un vieux JWT. */
export function useRequireAuth(role?: string) {
  const router = useRouter();
  const [ok, setOk] = useState(false);
  const [forbidden, setForbidden] = useState(false);
  const [token, setToken] = useState<string | null>(null);

  useEffect(() => {
    const t = getToken();
    if (!t) {
      router.replace("/login");
      return;
    }
    const passes = (r: string | null | undefined) =>
      !role || (r ?? "").toLowerCase() === role.toLowerCase();

    if (passes(getRole())) {
      setToken(t);
      setOk(true);
      return;
    }
    // Rôle local insuffisant → vérification DB avant de refuser.
    let stop = false;
    apiGet<{ name?: string; role?: string }>("/api/auth/me", t)
      .then((me) => {
        if (stop) return;
        if (me?.role) {
          saveSession(t, me.role, me.name ?? getName() ?? "");
          if (passes(me.role)) {
            setToken(t);
            setOk(true);
            return;
          }
        }
        setForbidden(true);
      })
      .catch((ex) => {
        if (stop) return;
        if (ex instanceof ApiError && ex.status === 401) {
          clearSession();
          router.replace("/login");
          return;
        }
        setForbidden(true);
      });
    return () => { stop = true; };
  }, [router, role]);

  return { ok, token, forbidden };
}
