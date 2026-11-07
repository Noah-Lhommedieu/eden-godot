"use client";

// Sélecteur de langue — écrit le cookie `locale` (1 an) puis
// router.refresh() : la page se re-rend côté serveur dans la langue
// choisie. FR = défaut ; le libellé affiche la langue courante.
import { useRouter } from "next/navigation";
import { LOCALE_COOKIE, LOCALES } from "@/lib/i18n";
import { useI18n } from "@/components/i18n-provider";
import { cn } from "@/lib/utils";

const FLAGS: Record<string, string> = { fr: "FR", en: "EN", br: "BR" };

export function LangSwitch({ compact = false }: { compact?: boolean }) {
  const router = useRouter();
  const { locale, t } = useI18n();

  const pick = (l: string) => {
    document.cookie =
      `${LOCALE_COOKIE}=${l};path=/;max-age=31536000;samesite=lax`;
    router.refresh();
  };

  return (
    <span
      role="group"
      aria-label={t("nav.menu")}
      className="inline-flex items-center overflow-hidden rounded-md border border-raised text-xs"
    >
      {LOCALES.map((l) => (
        <button
          key={l}
          type="button"
          onClick={() => pick(l)}
          aria-pressed={l === locale}
          title={t(`lang.${l}`)}
          className={cn(
            "px-2 py-1 transition-colors",
            compact ? "text-[11px]" : "text-xs",
            l === locale
              ? "bg-raised text-gold"
              : "text-dim hover:text-ink",
          )}
        >
          {FLAGS[l]}
        </button>
      ))}
    </span>
  );
}
