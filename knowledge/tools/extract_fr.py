#!/usr/bin/env python3
"""Extraction des dumps HTML Eden Eternal FR -> markdown.

- wiki3  (fandom FR, dump deja epure)      -> knowledge/wiki-fr/pages/
- wiki2  (WordPress guides edenfr)         -> knowledge/guides-fr/pages/

Produit aussi knowledge/tools/index_data.json (titre, resume, theme,
hors_perimetre) utilise par build_index.py.
"""
import json
import re
import sys
import unicodedata
from pathlib import Path
from urllib.parse import unquote, quote

import html2text
from bs4 import BeautifulSoup, Tag

ROOT = Path("/home/ster2lune/Bureau/reverseden/eden-godot/knowledge")
WIKI3_SRC = ROOT / "raw/wiki-fr"
WIKI2_SRC = ROOT / "raw/guides-fr"

FANDOM = "https://eden-eternal.fandom.com"
WPCOM = "https://edenfr.wordpress.com"


def make_h2t():
    h = html2text.HTML2Text()
    h.body_width = 0
    h.ignore_images = False
    h.ignore_emphasis = False
    h.unicode_snob = True
    h.bypass_tables = False
    h.mark_code = False
    h.single_line_break = True
    return h


def clean_md(md: str) -> str:
    md = re.sub(r"\n{3,}", "\n\n", md)
    # lignes constituees uniquement de residus html2text
    md = re.sub(r"^\s*\[[^\]]*\]\s*$", "", md, flags=re.M)  # liens orphelins seuls
    md = re.sub(r"[ \t]+\n", "\n", md)
    return md.strip() + "\n"


def text_of(node) -> str:
    return node.get_text(" ", strip=True) if node else ""


def slugify(name: str) -> str:
    name = unquote(name).replace(" ", "_")
    keep = []
    for ch in name:
        cat = unicodedata.category(ch)
        if cat.startswith("M"):
            continue
        keep.append(ch)
    n = unicodedata.normalize("NFC", "".join(keep))
    n = re.sub(r"[^0-9A-Za-zÀ-ÿ_\-'().!]", "_", n)
    return re.sub(r"_+", "_", n).strip("_")


def first_paragraph_text(main: Tag, limit=300) -> str:
    """Premier paragraphe 'utile' (>= 40 chars, pas une legende image)."""
    for p in main.find_all(["p", "li"], recursive=True):
        t = text_of(p)
        if len(t) >= 40:
            return t[:limit] + ("…" if len(t) > limit else "")
    return text_of(main)[:limit]


# ---------------------------------------------------------------- wiki3

def convert_wiki3() -> list[dict]:
    out_dir = ROOT / "wiki-fr/pages"
    existing = {p.stem for p in WIKI3_SRC.glob("*.html")}
    records = []
    for src in sorted(WIKI3_SRC.glob("*.html")):
        soup = BeautifulSoup(src.read_text(encoding="utf-8"), "lxml")
        main = soup.select_one(".mw-parser-output")
        title = text_of(soup.find("h1")) or src.stem.replace("_", " ")
        stem = src.stem
        if main is None:
            records.append(dict(site="wiki-fr", file=src.name, title=title,
                                md=None, summary="", theme="divers",
                                empty=True))
            continue

        # menage : toc, noscript (doublons lazy-load), svg, scripts
        for junk in main.select("#toc, noscript, svg, script, style, "
                                ".mw-editsection, .info-icon, "
                                "input.toctogglecheckbox, .toctogglelabel"):
            junk.decompose()

        # liens internes -> liens md locaux
        for a in main.find_all("a", href=True):
            href = a["href"]
            m = re.match(r"^(?:https?://eden-eternal\.fandom\.com)?/fr/wiki/([^#\s]+)(#.*)?$", href)
            if m:
                page = unquote(m.group(1))
                if page.startswith(("Fichier:", "File:", "Catégorie:", "Category:")):
                    a.unwrap()  # garde img/texte interne, supprime le lien mort
                    continue
                st = slugify(page)
                if st in existing:
                    a["href"] = st + ".md" + (m.group(2) or "")
                else:
                    a["href"] = f"{FANDOM}/fr/wiki/{quote(page)}"
            elif href.startswith("//"):
                a["href"] = "https:" + href

        # images : supprimer le wrapper <a> inutile, garder img+caption
        for img in main.find_all("img"):
            if img.get("data-src") and (not img.get("src") or "data:image" in img.get("src", "")):
                img["src"] = img["data-src"]
        for fig in main.select("figure"):
            cap = fig.select_one("figcaption")
            captext = text_of(cap)
            if cap:
                cap.decompose()
            img = fig.find("img")
            repl = soup.new_tag("p")
            if img and img.get("src"):
                repl.append(BeautifulSoup(str(img), "lxml"))
                if captext:
                    repl.append(BeautifulSoup(f"<br><em>{captext}</em>", "lxml"))
            else:
                repl.append(BeautifulSoup(f"<em>{captext}</em>", "lxml"))
            fig.replace_with(repl)

        md = make_h2t().handle(str(main))
        md = clean_md(md)
        out = out_dir / f"{stem}.md"
        out.write_text(
            f"# {title}\n\n"
            f"> Source : `raw/wiki-fr/{src.name}` (fandom FR)\n\n" + md,
            encoding="utf-8")
        records.append(dict(
            site="wiki-fr", file=src.name, md=f"pages/{stem}.md",
            title=title, summary=first_paragraph_text(main),
            n_tables=len(main.find_all("table")),
            n_imgs=len(main.find_all("img"))))
    return records


# ---------------------------------------------------------------- wiki2

def convert_wiki2() -> list[dict]:
    out_dir = ROOT / "guides-fr/pages"
    records = []
    files = sorted(WIKI2_SRC.rglob("*.html"))
    for src in files:
        rel = src.relative_to(WIKI2_SRC)
        soup = BeautifulSoup(src.read_text(encoding="utf-8"), "lxml")
        body_cls = (soup.body.get("class") if soup.body else []) or []

        if "wp-content" in rel.parts:
            # page-attachement image : index seulement
            link = soup.select_one('link[rel="preload"][as="image"]')
            img = soup.find("img")
            url = (link.get("href") if link else None) or \
                  (img.get("src") if img else None) or ""
            records.append(dict(site="guides-fr", file=str(rel),
                                title=src.stem, md=None, summary="",
                                theme="attachments", image_url=url))
            continue

        main = soup.select_one(".entry-content") or soup.select_one("article")
        tnode = soup.select_one("h1.entry-title") or soup.select_one(".entry-title")
        title = text_of(tnode) or src.stem.replace("-", " ").title()

        if "archive" in body_cls or main is None:
            records.append(dict(site="guides-fr", file=str(rel), title=title,
                                md=None, summary="", theme="archives",
                                archive=True))
            continue

        # slug de sortie
        if len(rel.parts) == 4:
            stem = rel.parts[-1][:-5]
        else:
            stem = rel.stem
        stem = slugify(stem)

        for junk in main.select("script, style, noscript, .sharedaddy, "
                                ".jp-relatedposts, .comments-area, "
                                "#comments, .entry-footer, .post-navigation, "
                                ".taglist, .entry-meta, #nav-below, "
                                ".navigation-post, .screen-reader-text, "
                                ".sd-block, .wpcnt, .marketing-bar, form, "
                                ".robots-nocontent, .pd-rating, iframe"):
            junk.decompose()

        # liens internes wordpress -> md locaux
        for a in main.find_all("a", href=True):
            href = a["href"]
            m = re.match(r"^(?:https?://edenfr\.wordpress\.com)?/\d{4}/\d{2}/\d{2}/([^/\s'\"]+)/?(#.*)?$", href)
            if m:
                slug_raw = unquote(m.group(1))
                slug = slugify(slug_raw)
                # le fichier article existe-t-il dans le dump ?
                if list(WIKI2_SRC.glob(f"????/??/??/{slug_raw}.html")):
                    a["href"] = slug + ".md" + (m.group(2) or "")
                    continue
            m2 = re.match(r"^https?://edenfr\.wordpress\.com/([^/\s?#]+)/?(#.*)?$", href)
            if m2:
                slug = m2.group(1)
                if (WIKI2_SRC / (slug + ".html")).exists():
                    a["href"] = slugify(slug) + ".md" + (m2.group(2) or "")

        for img in main.find_all("img"):
            for attr in ("data-orig-file", "data-large-file", "data-medium-file"):
                if img.get(attr):
                    img["src"] = img[attr]
                    break
            for junk_attr in ("srcset", "sizes", "data-attachment-id", "class"):
                img.attrs.pop(junk_attr, None)

        md = make_h2t().handle(str(main))
        md = clean_md(md)
        date = "/".join(rel.parts[:3]) if len(rel.parts) == 4 else ""
        out = out_dir / f"{stem}.md"
        out.write_text(
            f"# {title}\n\n"
            f"> Source : `raw/guides-fr/{rel}`"
            + (f" — publié le {date}" if date else "")
            + " (site WordPress edenfr.wordpress.com)\n\n" + md,
            encoding="utf-8")
        records.append(dict(
            site="guides-fr", file=str(rel), md=f"pages/{stem}.md",
            title=title, summary=first_paragraph_text(main),
            date=date,
            n_tables=len(main.find_all("table")),
            n_imgs=len(main.find_all("img"))))
    return records


def main():
    recs = convert_wiki3() + convert_wiki2()
    (ROOT / "tools/index_data.json").write_text(
        json.dumps(recs, ensure_ascii=False, indent=1), encoding="utf-8")
    n_md = sum(1 for r in recs if r.get("md"))
    print(f"{len(recs)} entrees, {n_md} pages md generees")


if __name__ == "__main__":
    sys.exit(main())
