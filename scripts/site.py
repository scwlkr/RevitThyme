"""Build, inspect and serve the public static site without external dependencies."""

import argparse
import html
import importlib.util
import json
import shutil
import subprocess
from collections import defaultdict
from html.parser import HTMLParser
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import unquote, urlsplit

ROOT = Path(__file__).resolve().parents[1]
SITE = ROOT / "site"
OUTPUT = ROOT / "artifacts/site/dist"
ORIGIN = "https://revitthyme.wlkrlabs.com"
PAGES = json.loads((SITE / "pages.json").read_text(encoding="utf-8"))
spec = importlib.util.spec_from_file_location("site_markdown", ROOT / "scripts/site-markdown.py")
markdown = importlib.util.module_from_spec(spec)
spec.loader.exec_module(markdown)


def write(path, content):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8", newline="\n")


def layout(title, description, content, route, section=""):
    template = (SITE / "layout.html").read_text(encoding="utf-8")
    values = {"TITLE": html.escape(title), "DESCRIPTION": html.escape(description, quote=True),
              "CANONICAL": ORIGIN + route, "CONTENT": content,
              "DOCS_ACTIVE": 'aria-current="page"' if section == "docs" else "",
              "DOWNLOAD_ACTIVE": 'aria-current="page"' if section == "download" else ""}
    for key, value in values.items():
        template = template.replace(f"@@{key}@@", value)
    write(OUTPUT / route.strip("/") / "index.html", template)


def sidebar(selected):
    groups = defaultdict(list)
    for page in PAGES:
        groups[page["group"]].append(page)
    nav = '<button class="search-trigger" data-open-search>Search docs <kbd>Ctrl K</kbd></button>'
    nav += '<button class="docs-mobile-nav" aria-expanded="false" aria-controls="docs-navigation">Browse documentation ↓</button>'
    nav += '<nav class="docs-navigation" id="docs-navigation" aria-label="Documentation">'
    nav += '<div class="docs-nav-group"><a href="/docs/">Overview</a></div>'
    for group, pages in groups.items():
        nav += f'<div class="docs-nav-group"><p>{html.escape(group)}</p>'
        for page in pages:
            active = ' aria-current="page"' if selected == page["slug"] else ""
            nav += f'<a href="/docs/{page["slug"]}/"{active}>{html.escape(page["title"])}</a>'
        nav += "</div>"
    return nav + '</nav><div class="docs-version">v0.2.0 · Preview release</div>'


def docs_shell(content, selected="", headings=()):
    toc = '<aside class="docs-toc" aria-label="On this page"><p>On this page</p>'
    toc += "".join(f'<a href="#{anchor}">{html.escape(title)}</a>' for anchor, title in headings)
    return f'<main id="main" class="docs-layout"><aside class="docs-sidebar">{sidebar(selected)}</aside><article class="docs-content">{content}</article>{toc}</aside></main>'


def build():
    # Replace only this task's generated, fixed output path; never follow a symlink.
    if OUTPUT.exists():
        if OUTPUT.is_symlink() or not OUTPUT.resolve().is_relative_to(ROOT / "artifacts/site"):
            raise ValueError("Unsafe generated site destination")
        shutil.rmtree(OUTPUT)
    OUTPUT.mkdir(parents=True)
    for name in ("styles.css", "docs.css", "app.js"):
        shutil.copyfile(SITE / name, OUTPUT / name)
    shutil.copytree(SITE / "assets", OUTPUT / "assets")
    for name in ("revitthyme-logo", "status", "settings", "timberfold"):
        shutil.copyfile(ROOT / f"assets/branding/{name}.svg", OUTPUT / f"assets/{name}.svg")
    layout("RevitThyme — Tools with room to grow", "Downloads and documentation for the growing RevitThyme Revit tool suite. Built on pyRevit.",
           (SITE / "home.html").read_text(encoding="utf-8"), "/")
    layout("Download RevitThyme", "Download the v0.2.0 preview ZIP and checksum. Installation requirements and guides for Windows / Revit 2027.",
           (SITE / "download.html").read_text(encoding="utf-8"), "/download/", "download")
    index = []
    for position, page in enumerate(PAGES):
        source = ROOT / page["source"]
        text = source.read_text(encoding="utf-8")
        body, headings = markdown.render(text, source, ROOT, PAGES)
        content = f'<div class="breadcrumb"><a href="/docs/">Documentation</a><span>/</span><span>{html.escape(page["group"])}</span></div>'
        source_link = page["source"] if not page["source"].startswith("site/") else "docs/DEPLOYMENT.md"
        content += f'<h1>{html.escape(page["title"])}</h1><div class="doc-meta">v0.2.0 preview · <a href="{markdown.GITHUB}{source_link}">Source guide on GitHub ↗</a></div><div class="markdown">{body}</div>'
        content += '<nav class="doc-pagination" aria-label="Adjacent guides">'
        if position:
            previous = PAGES[position - 1]
            content += f'<a href="/docs/{previous["slug"]}/"><small>← Previous</small>{html.escape(previous["title"])}</a>'
        if position + 1 < len(PAGES):
            following = PAGES[position + 1]
            content += f'<a href="/docs/{following["slug"]}/"><small>Next →</small>{html.escape(following["title"])}</a>'
        content += '</nav>'
        route = f'/docs/{page["slug"]}/'
        layout(page["title"] + " — RevitThyme docs", page["summary"], docs_shell(content, page["slug"], headings), route, "docs")
        parsed_text = PageParser()
        parsed_text.feed(body)
        index.append({"title": page["title"], "summary": page["summary"], "url": route, "text": " ".join(" ".join(parsed_text.text).split())})
    content = '<p class="eyebrow">REVITTHYME DOCUMENTATION</p><h1>A practical guide.<br>Room to explore.</h1><p class="docs-home-intro">Get started, understand the tools, or build the next chapter with us.</p><div class="doc-index-grid">'
    for page in PAGES:
        content += f'<a class="doc-index-card" href="/docs/{page["slug"]}/"><span>{html.escape(page["group"])}</span><h2>{html.escape(page["title"])} ↗</h2><p>{html.escape(page["summary"])}</p></a>'
    content += '</div><p class="docs-home-note">This is the v0.2.0 preview documentation. The ribbon and read-only Routes are implemented. A native host, dedicated MCP server, fabrication preview and generation remain planned.</p>'
    layout("Documentation — RevitThyme", "Installation, tools, architecture, troubleshooting and roadmap guides for RevitThyme.", docs_shell(content), "/docs/", "docs")
    layout("Page not found — RevitThyme", "Find your way back to the RevitThyme documentation.", '<main id="main" class="wrap section"><p class="eyebrow">404 · A PATH LESS TRAVELLED</p><h1>Let’s get you<br>back on track.</h1><a class="button primary" href="/docs/">Explore documentation ↗</a></main>', "/404/")
    shutil.copyfile(OUTPUT / "404/index.html", OUTPUT / "404.html")
    write(OUTPUT / "search-index.json", json.dumps(index, ensure_ascii=False) + "\n")
    routes = ["/", "/download/", "/docs/"] + [row["url"] for row in index]
    write(OUTPUT / "sitemap.xml", '<?xml version="1.0" encoding="UTF-8"?><urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">' + "".join(f"<url><loc>{ORIGIN}{route}</loc></url>" for route in routes) + "</urlset>\n")
    write(OUTPUT / "robots.txt", f"User-agent: *\nAllow: /\nSitemap: {ORIGIN}/sitemap.xml\n")
    revision = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    write(OUTPUT / "build.json", json.dumps({"source_revision": revision, "release_version": "0.2.0", "canonical_origin": ORIGIN}, indent=2) + "\n")
    print(f"Built {len(routes)} public pages: {OUTPUT}")


class PageParser(HTMLParser):
    def __init__(self):
        super().__init__()
        self.links, self.ids = [], set()
        self.text = []

    def handle_data(self, data):
        self.text.append(data)

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if "id" in attrs:
            self.ids.add(attrs["id"])
        for attribute in ("href", "src"):
            if attribute in attrs:
                self.links.append(attrs[attribute])


def check():
    build()
    pages, failures, checked = {}, [], 0
    for path in OUTPUT.rglob("*.html"):
        parsed = PageParser()
        parsed.feed(path.read_text(encoding="utf-8"))
        pages[path] = parsed
    for path, parsed in pages.items():
        for target in parsed.links:
            url = urlsplit(target)
            if url.scheme or url.netloc:
                continue
            destination = OUTPUT / unquote(url.path).lstrip("/") if url.path.startswith("/") else path.parent / unquote(url.path)
            if not url.path:
                destination = path
            if destination.is_dir():
                destination /= "index.html"
            checked += 1
            if not destination.is_file():
                failures.append(f"{path.relative_to(OUTPUT)}: missing {target}")
            elif url.fragment and destination in pages and unquote(url.fragment) not in pages[destination].ids:
                failures.append(f"{path.relative_to(OUTPUT)}: missing anchor {target}")
    report = {"passed": not failures, "pages": len(pages), "local_links_checked": checked, "failures": failures,
              "browser_checked": False, "external_links_checked": False}
    write(ROOT / "artifacts/site/check.json", json.dumps(report, indent=2) + "\n")
    print(json.dumps(report))
    return int(bool(failures))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("build", "check", "serve", "stage"))
    parser.add_argument("--port", type=int, default=4173)
    args = parser.parse_args()
    if args.action == "check":
        return check()
    build()
    if args.action == "serve":
        class Handler(SimpleHTTPRequestHandler):
            def __init__(self, *values, **kwargs):
                super().__init__(*values, directory=str(OUTPUT), **kwargs)
        server = ThreadingHTTPServer(("127.0.0.1", args.port), Handler)
        print(f"Local: http://127.0.0.1:{args.port}", flush=True)
        try:
            server.serve_forever()
        except KeyboardInterrupt:
            server.server_close()
    elif args.action == "stage":
        target = ROOT / "artifacts/site-publish"
        target.mkdir(parents=True, exist_ok=True)
        if target.is_symlink() or target.resolve() != ROOT / "artifacts/site-publish":
            raise ValueError("Unsafe publishing destination")
        if (target / "dist").exists():
            if (target / "dist").is_symlink():
                raise ValueError("Unsafe publishing output")
            shutil.rmtree(target / "dist")
        shutil.copytree(OUTPUT, target / "dist", dirs_exist_ok=True)
        (target / ".openai").mkdir(exist_ok=True)
        shutil.copyfile(SITE / ".openai/hosting.json", target / ".openai/hosting.json")
        print(f"Sanitized publishing source: {target}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
