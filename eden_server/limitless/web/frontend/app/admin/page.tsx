"use client";

// Panneau admin (WF7) — onglets : annonces (CRUD), guides (CMS),
// joueurs (base), terminal de commandes vers la zone.
// Les endpoints WD9 qui n'existent pas encore tombent sur un état
// « en attente du backend » explicite au lieu de casser.
import { useEffect, useState } from "react";
import {
  apiDelete, apiGet, apiPost, apiPut, ApiError, GuideRow,
  NewsRow, ServerStatus,
} from "@/lib/api";
import { useRequireAuth } from "@/lib/auth";
import { useFmt, useT } from "@/components/i18n-provider";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { cn } from "@/lib/utils";

const inputCls =
  "w-full rounded-md border border-raised bg-night/60 px-3 py-2 text-sm";

function isUnavailable(ex: unknown) {
  return ex instanceof ApiError && (ex.status === 404 || ex.status === 405);
}

/* ----------------------------- Annonces ----------------------------- */

function NewsPanel({ token }: { token: string }) {
  const t = useT();
  const fmt = useFmt();
  const [news, setNews] = useState<NewsRow[]>([]);
  const [title, setTitle] = useState("");
  const [body, setBody] = useState("");
  const [editing, setEditing] = useState<NewsRow | null>(null);
  const [msg, setMsg] = useState<string | null>(null);
  const [crudMissing, setCrudMissing] = useState(false);
  const [busy, setBusy] = useState(false);

  const reload = () =>
    apiGet<NewsRow[]>("/api/admin/news", token)
      .then(setNews).catch(() => setNews([]));
  useEffect(() => { reload(); }, [token]); // eslint-disable-line

  const reset = () => { setEditing(null); setTitle(""); setBody(""); };

  const save = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setMsg(null);
    try {
      if (editing) {
        await apiPut(
          `/api/admin/news/${editing.id}`, { title, body }, token);
        setMsg(t("admin.newsEditSaved"));
      } else {
        await apiPost("/api/admin/news", { title, body }, token);
        setMsg(t("admin.published"));
      }
      reset();
      await reload();
    } catch (ex) {
      if (editing && isUnavailable(ex)) {
        setCrudMissing(true);
      } else {
        setMsg(ex instanceof ApiError && ex.status === 403
          ? t("admin.newsErrAdmin") : t("admin.newsErr"));
      }
    } finally {
      setBusy(false);
    }
  };

  const remove = async (n: NewsRow) => {
    if (!window.confirm(t("admin.newsDeleteConfirm", { title: n.title })))
      return;
    try {
      await apiDelete(`/api/admin/news/${n.id}`, token);
      setMsg(t("admin.newsDeleted"));
      await reload();
    } catch (ex) {
      if (isUnavailable(ex)) setCrudMissing(true);
    }
  };

  return (
    <>
      <section className="panel px-5 py-4">
        <div className="flex items-center justify-between">
          <h2 className="font-display text-lg font-bold">
            {editing ? t("common.edit") : t("admin.newsPost")}
          </h2>
          {editing && (
            <button
              onClick={reset}
              className="text-xs text-dim hover:text-gold"
            >
              {t("common.cancel")}
            </button>
          )}
        </div>
        <form onSubmit={save} className="mt-4 space-y-4">
          <div className="space-y-2">
            <Label htmlFor="t">{t("admin.newsTitle")}</Label>
            <input
              id="t" value={title} required maxLength={200}
              onChange={(e) => setTitle(e.target.value)}
              className={inputCls}
            />
          </div>
          <div className="space-y-2">
            <Label htmlFor="b">{t("admin.newsBody")}</Label>
            <textarea
              id="b" value={body} required rows={4}
              onChange={(e) => setBody(e.target.value)}
              className={inputCls}
            />
          </div>
          {msg && <p className="text-sm text-gold">{msg}</p>}
          <Button type="submit" disabled={busy}>
            {busy
              ? t("admin.publishing")
              : editing ? t("common.save") : t("admin.publish")}
          </Button>
        </form>
      </section>

      <section className="panel px-5 py-4">
        <h2 className="font-display text-lg font-bold">
          {t("admin.postedNews", { n: news.length })}
        </h2>
        {crudMissing && (
          <p className="mt-1 text-xs text-dim">
            {t("admin.newsUpdateUnavailable")}
          </p>
        )}
        <div className="mt-3 space-y-2">
          {news.length === 0 && (
            <p className="text-sm text-dim">{t("admin.noNews")}</p>
          )}
          {news.map((n) => (
            <div
              key={n.id}
              className="flex items-start justify-between gap-3 rounded-md border border-raised p-3"
            >
              <div className="min-w-0">
                <p className="font-medium">{n.title}</p>
                <p className="text-xs text-dim">
                  {fmt.dateTime(n.created_at)}
                </p>
              </div>
              <span className="flex shrink-0 gap-3 text-xs">
                <button
                  onClick={() => {
                    setEditing(n); setTitle(n.title); setBody(n.body);
                    setMsg(null);
                  }}
                  className="text-mutedx hover:text-gold"
                >
                  {t("common.edit")}
                </button>
                <button
                  onClick={() => remove(n)}
                  className="text-ember hover:underline"
                >
                  {t("common.delete")}
                </button>
              </span>
            </div>
          ))}
        </div>
      </section>
    </>
  );
}

/* ------------------------------ Guides ------------------------------ */

function GuidesPanel({ token }: { token: string }) {
  const t = useT();
  const [guides, setGuides] = useState<GuideRow[]>([]);
  const [editing, setEditing] = useState<GuideRow | null>(null);
  const [title, setTitle] = useState("");
  const [category, setCategory] = useState("General");
  const [slug, setSlug] = useState("");
  const [body, setBody] = useState("");
  const [sortOrder, setSortOrder] = useState(0);
  const [published, setPublished] = useState(false);
  const [msg, setMsg] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const reload = () =>
    apiGet<GuideRow[]>("/api/guides?all=true", token)
      .then(setGuides).catch(() => setGuides([]));
  useEffect(() => { reload(); }, [token]); // eslint-disable-line

  const reset = () => {
    setEditing(null);
    setTitle(""); setCategory("General"); setSlug("");
    setBody(""); setSortOrder(0); setPublished(false);
  };

  const pick = (g: GuideRow) => {
    setEditing(g);
    setTitle(g.title); setCategory(g.category); setSlug(g.slug);
    setBody(g.body); setSortOrder(g.sortOrder); setPublished(g.published);
    setMsg(null);
  };

  const save = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setMsg(null);
    try {
      const payload = {
        title, category, body,
        sortOrder, published,
        slug: slug || undefined,
      };
      if (editing) {
        await apiPut(`/api/guides/${editing.id}`, payload, token);
        setMsg(t("admin.guideSaved"));
      } else {
        await apiPost("/api/guides", payload, token);
        setMsg(t("admin.guideCreated"));
      }
      await reload();
    } catch {
      setMsg(t("admin.guideErr"));
    } finally {
      setBusy(false);
    }
  };

  const remove = async (g: GuideRow) => {
    if (!window.confirm(t("admin.guideDeleteConfirm", { title: g.title })))
      return;
    await apiDelete(`/api/guides/${g.id}`, token).catch(() => {});
    if (editing?.id === g.id) reset();
    await reload();
  };

  return (
    <section className="panel px-5 py-4">
      <div className="flex items-center justify-between">
        <h2 className="font-display text-lg font-bold">
          {t("admin.guidesTitle", { n: guides.length })}
        </h2>
        {editing && (
          <button
            onClick={reset}
            className="text-xs text-dim hover:text-gold"
          >
            {t("admin.newGuide")}
          </button>
        )}
      </div>
      <p className="mt-1 text-xs text-dim">{t("admin.guidesPublic")}</p>

      <div className="mt-4 grid gap-4 lg:grid-cols-[240px_1fr]">
        <ul className="max-h-96 space-y-1 overflow-y-auto">
          {guides.length === 0 && (
            <li className="text-sm text-dim">{t("admin.noGuides")}</li>
          )}
          {guides.map((g) => (
            <li
              key={g.id}
              className="flex items-center justify-between gap-2 rounded-md border border-raised px-2 py-1.5 text-sm"
            >
              <button
                onClick={() => pick(g)}
                className="min-w-0 truncate text-left hover:text-gold"
              >
                {g.title}
              </button>
              <span className="flex shrink-0 items-center gap-1.5">
                <Badge
                  variant={g.published ? "default" : "secondary"}
                  className={g.published ? "bg-aurora/90 text-night" : ""}
                >
                  {g.published ? t("admin.guideLive") : t("admin.guideDraft")}
                </Badge>
                <button
                  onClick={() => remove(g)}
                  className="text-xs text-ember hover:underline"
                >
                  {t("admin.del")}
                </button>
              </span>
            </li>
          ))}
        </ul>

        <form onSubmit={save} className="space-y-3">
          <div className="grid gap-3 sm:grid-cols-2">
            <div className="space-y-1.5">
              <Label htmlFor="gt">{t("admin.guideTitle")}</Label>
              <input
                id="gt" value={title} required maxLength={160}
                onChange={(e) => setTitle(e.target.value)}
                className={inputCls}
              />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="gc">{t("admin.guideCat")}</Label>
              <input
                id="gc" value={category} required maxLength={40}
                onChange={(e) => setCategory(e.target.value)}
                className={inputCls}
              />
            </div>
          </div>
          <div className="grid gap-3 sm:grid-cols-3">
            <div className="space-y-1.5 sm:col-span-1">
              <Label htmlFor="gs">{t("admin.guideSlug")}</Label>
              <input
                id="gs" value={slug} maxLength={80}
                placeholder={t("admin.guideSlugHint")}
                onChange={(e) => setSlug(e.target.value)}
                className={inputCls}
              />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="go">{t("admin.guideSort")}</Label>
              <input
                id="go" type="number" value={sortOrder}
                onChange={(e) => setSortOrder(Number(e.target.value) || 0)}
                className={inputCls}
              />
            </div>
            <div className="flex items-end pb-1">
              <label className="flex items-center gap-2 text-sm">
                <input
                  type="checkbox" checked={published}
                  onChange={(e) => setPublished(e.target.checked)}
                  className="size-4 accent-[#f0bc55]"
                />
                {t("admin.guidePublished")}
              </label>
            </div>
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="gb">{t("admin.guideBody")}</Label>
            <textarea
              id="gb" value={body} required rows={10}
              onChange={(e) => setBody(e.target.value)}
              className={inputCls + " font-mono text-xs"}
            />
          </div>
          {msg && <p className="text-sm text-gold">{msg}</p>}
          <Button type="submit" disabled={busy}>
            {busy
              ? t("admin.saving")
              : editing ? t("admin.saveChanges") : t("admin.createGuide")}
          </Button>
        </form>
      </div>
    </section>
  );
}

/* ------------------------------ Joueurs ----------------------------- */

// GET /api/admin/players?q= → [{id,username,state,characters:
// [{id,name,level,classId}]}] (state=-1 banni). Ban via
// POST /api/admin/players/{accountId}/ban {banned}.
interface PlayerChar { id: number; name: string; level: number; classId: number }
interface PlayerRow {
  id: number; username: string; state: number;
  characters: PlayerChar[];
}

function PlayersPanel({ token }: { token: string }) {
  const t = useT();
  const [q, setQ] = useState("");
  const [players, setPlayers] = useState<PlayerRow[] | null>(null);
  const [state, setState] = useState<"idle" | "ok" | "missing" | "err">(
    "idle",
  );
  const [note, setNote] = useState<string | null>(null);

  const search = async (e?: React.FormEvent) => {
    e?.preventDefault();
    try {
      const res = await apiGet<PlayerRow[]>(
        `/api/admin/players?q=${encodeURIComponent(q)}&size=50`, token);
      setPlayers(res);
      setState("ok");
    } catch (ex) {
      setState(isUnavailable(ex) ? "missing" : "err");
    }
  };
  useEffect(() => { search(); }, [token]); // eslint-disable-line

  const toggleBan = async (p: PlayerRow) => {
    const banned = p.state !== -1;
    try {
      await apiPost(
        `/api/admin/players/${p.id}/ban`, { banned }, token);
      setNote(t("admin.banDone"));
      await search();
    } catch {
      setNote(t("admin.termErr"));
    }
  };

  if (state === "missing") {
    return (
      <section className="panel px-5 py-10 text-center text-sm text-dim">
        {t("admin.playersUnavailable")}
      </section>
    );
  }

  return (
    <section className="panel px-5 py-4">
      <h2 className="font-display text-lg font-bold">
        {t("admin.playersTitle")}
      </h2>
      <p className="mt-1 text-xs text-dim">{t("admin.playersSub")}</p>
      <form onSubmit={search} className="mt-3 flex gap-2">
        <input
          value={q}
          onChange={(e) => setQ(e.target.value)}
          placeholder={t("admin.playersSearch")}
          className={cn(inputCls, "max-w-sm")}
        />
        <Button type="submit" variant="secondary">
          {t("admin.playersSearchBtn")}
        </Button>
      </form>
      {note && <p className="mt-2 text-sm text-gold">{note}</p>}
      <div className="mt-4 overflow-x-auto">
        {state === "err" ? (
          <p className="text-sm text-ember">{t("common.unreachable")}</p>
        ) : players === null ? (
          <p className="text-sm text-dim">{t("common.loading")}</p>
        ) : players.length === 0 ? (
          <p className="text-sm text-dim">{t("admin.playersEmpty")}</p>
        ) : (
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-raised text-left text-xs text-dim">
                <th className="px-3 py-2 font-normal">{t("admin.colAccount")}</th>
                <th className="hidden px-3 py-2 font-normal md:table-cell">
                  {t("admin.colChars")}
                </th>
                <th className="px-3 py-2 text-right font-normal">
                  {t("admin.colActions")}
                </th>
              </tr>
            </thead>
            <tbody>
              {players.map((p) => (
                <tr
                  key={p.id}
                  className="border-b border-raised/50 last:border-0"
                >
                  <td className="px-3 py-2">
                    {p.username}
                    {p.state === -1 && (
                      <Badge variant="secondary" className="ml-2 text-ember">
                        {t("admin.banned")}
                      </Badge>
                    )}
                  </td>
                  <td className="hidden px-3 py-2 text-dim md:table-cell">
                    {p.characters.length === 0
                      ? "—"
                      : p.characters.map((c) => c.name).join(", ")}
                  </td>
                  <td className="px-3 py-2 text-right">
                    <button
                      onClick={() => toggleBan(p)}
                      className={cn(
                        "text-xs hover:underline",
                        p.state === -1 ? "text-aurora" : "text-ember",
                      )}
                    >
                      {p.state === -1 ? t("admin.unban") : t("admin.ban")}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </section>
  );
}

/* ----------------------------- Terminal ----------------------------- */

// Terminal de commandes jeu (WD9) : POST /api/admin/command
// {kind:"gm|kick|message", target, command} → file
// fndb1.web_admin_commands (status=pending — le game-server la
// dépilie). GET /api/admin/command?status= → historique.
interface CmdRow {
  id: number; kind: string; target?: string; command: string;
  author?: string; status: string; created_at: string; done_at?: string;
}

function TerminalPanel({ token }: { token: string }) {
  const t = useT();
  const fmt = useFmt();
  const [kind, setKind] = useState("gm");
  const [target, setTarget] = useState("");
  const [cmd, setCmd] = useState("");
  const [log, setLog] = useState<CmdRow[]>([]);
  const [msg, setMsg] = useState<string | null>(null);
  const [missing, setMissing] = useState(false);
  const [busy, setBusy] = useState(false);

  const reload = () =>
    apiGet<CmdRow[]>("/api/admin/command", token)
      .then(setLog).catch(() => setLog([]));
  useEffect(() => { reload(); }, [token]); // eslint-disable-line

  const send = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!cmd.trim()) return;
    setBusy(true);
    setMsg(null);
    try {
      const res = await apiPost<{ id: number; status: string }>(
        "/api/admin/command",
        { kind, target: target || undefined, command: cmd },
        token,
      );
      setMsg(t("admin.termQueued", { id: res.id, status: res.status }));
      setCmd("");
      await reload();
    } catch (ex) {
      if (isUnavailable(ex)) {
        setMissing(true);
      } else {
        setMsg(t("admin.termErr"));
      }
    } finally {
      setBusy(false);
    }
  };

  if (missing) {
    return (
      <section className="panel px-5 py-10 text-center text-sm text-dim">
        {t("admin.termUnavailable")}
      </section>
    );
  }

  return (
    <section className="panel px-5 py-4">
      <h2 className="font-display text-lg font-bold">
        {t("admin.termTitle")}
      </h2>
      <p className="mt-1 text-xs text-dim">{t("admin.termSub")}</p>
      <form onSubmit={send} className="mt-3 grid gap-2 sm:grid-cols-[140px_1fr_2fr_auto]">
        <select
          value={kind}
          onChange={(e) => setKind(e.target.value)}
          aria-label={t("admin.termKind")}
          className="rounded-md border border-raised bg-night/60 px-3 py-2 text-sm text-ink"
        >
          <option value="gm">{t("admin.kind.gm")}</option>
          <option value="kick">{t("admin.kind.kick")}</option>
          <option value="message">{t("admin.kind.message")}</option>
        </select>
        <input
          value={target}
          onChange={(e) => setTarget(e.target.value)}
          placeholder={t("admin.termTarget")}
          className={inputCls}
        />
        <input
          value={cmd}
          onChange={(e) => setCmd(e.target.value)}
          placeholder={t("admin.termPlaceholder")}
          className={cn(inputCls, "font-mono")}
        />
        <Button type="submit" disabled={busy}>
          {busy ? t("admin.termSending") : t("admin.termSend")}
        </Button>
      </form>
      {msg && <p className="mt-2 text-sm text-gold">{msg}</p>}

      <h3 className="mt-5 text-xs text-dim">{t("admin.termHistory")}</h3>
      <div className="mt-2 space-y-1.5">
        {log.length === 0 ? (
          <p className="text-sm text-dim">{t("admin.termEmpty")}</p>
        ) : (
          log.map((e) => (
            <div
              key={e.id}
              className="flex flex-wrap items-baseline gap-x-3 rounded-md border border-raised bg-night/60 px-3 py-1.5 font-mono text-xs"
            >
              <span className={e.status === "pending"
                ? "text-gold" : "text-aurora"}>
                {e.status}
              </span>
              <span className="text-crystal">{e.kind}</span>
              {e.target && <span className="text-dim">{e.target}</span>}
              <span className="min-w-0 flex-1 truncate text-ink/85">
                {e.command}
              </span>
              <span className="text-dim">
                {e.author ?? "—"} · {fmt.dateTime(e.created_at)}
              </span>
            </div>
          ))
        )}
      </div>
    </section>
  );
}

/* ------------------------------ Onglets ----------------------------- */

const TABS = ["news", "guides", "players", "terminal"] as const;
type TabId = (typeof TABS)[number];

export default function AdminPage() {
  const t = useT();
  const { ok, token, forbidden } = useRequireAuth("Admin");
  const [status, setStatus] = useState<ServerStatus | null>(null);
  const [tab, setTab] = useState<TabId>("news");

  useEffect(() => {
    if (!token) return;
    apiGet<ServerStatus>("/api/admin/status", token)
      .then(setStatus).catch(() => setStatus(null));
  }, [token]);

  if (forbidden) {
    return (
      <div className="panel mx-auto mt-16 max-w-md px-6 py-10 text-center">
        <h1 className="font-display text-2xl font-bold text-ink">
          {t("admin.title")}
        </h1>
        <p className="mt-3 text-sm text-ember">{t("admin.forbidden")}</p>
      </div>
    );
  }
  if (!ok) return null;

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <h1 className="font-display text-3xl font-bold text-ink">
          {t("admin.title")}
        </h1>
        {status ? (
          <Badge
            variant={status.online > 0 ? "default" : "secondary"}
            className={status.online > 0 ? "bg-aurora/90 text-night" : ""}
          >
            {t("admin.statusBadge", {
              online: status.online, total: status.total,
            })}
          </Badge>
        ) : (
          <Badge variant="secondary">{t("admin.unreachable")}</Badge>
        )}
      </div>

      <nav
        className="flex flex-wrap gap-1 border-b border-raised pb-3"
        role="tablist"
      >
        {TABS.map((tb) => (
          <button
            key={tb}
            role="tab"
            aria-selected={tab === tb}
            onClick={() => setTab(tb)}
            className={cn(
              "rounded-md px-3 py-1.5 text-sm transition-colors",
              tab === tb
                ? "bg-raised text-gold"
                : "text-mutedx hover:bg-raised/50 hover:text-ink",
            )}
          >
            {t(`admin.tab${tb[0].toUpperCase()}${tb.slice(1)}`)}
          </button>
        ))}
      </nav>

      {tab === "news" && <NewsPanel token={token!} />}
      {tab === "guides" && <GuidesPanel token={token!} />}
      {tab === "players" && <PlayersPanel token={token!} />}
      {tab === "terminal" && <TerminalPanel token={token!} />}
    </div>
  );
}
