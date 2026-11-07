import "./globals.css";
import type { Metadata } from "next";
import { Cinzel, Sora } from "next/font/google";
import { SiteNav } from "@/components/site-nav";
import { I18nProvider } from "@/components/i18n-provider";
import { getLocale, getT } from "@/lib/i18n-server";
import { getDict, HTML_LANG } from "@/lib/i18n";

const cinzel = Cinzel({
  subsets: ["latin"],
  weight: ["600", "700"],
  variable: "--font-cinzel",
});
const sora = Sora({
  subsets: ["latin"],
  weight: ["400", "500", "600"],
  variable: "--font-sora",
});

export const metadata: Metadata = {
  title: "Limitless — Eden Eternal",
  description:
    "Portail communautaire Limitless — wiki, classements et progression des personnages du serveur Eden Eternal.",
};

export default async function RootLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  const locale = await getLocale();
  const dict = getDict(locale);
  const { t } = await getT();
  return (
    <html
      lang={HTML_LANG[locale]}
      className={`${cinzel.variable} ${sora.variable}`}
    >
      <body className="dark theme-eden style-luma min-h-screen">
        <I18nProvider locale={locale} dict={dict}>
          <SiteNav />
          <main className="mx-auto w-full max-w-6xl px-4 py-8 sm:px-6">
            {children}
          </main>
          <footer className="filet-t mx-auto flex w-full max-w-6xl items-center gap-3 px-4 py-6 text-xs text-dim sm:px-6">
            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img
              src="/art/mark.png" alt="" width={18} height={18}
              className="opacity-60"
            />
            <span>{t("footer.tagline")}</span>
          </footer>
        </I18nProvider>
      </body>
    </html>
  );
}
