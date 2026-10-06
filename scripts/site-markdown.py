"""Render the small, escaped Markdown subset used by the public guides."""

import html
import re
from pathlib import Path
from urllib.parse import urlsplit

GITHUB = "https://github.com/scwlkr/RevitThyme/blob/master/"


def render(text, source, root, pages):
    by_source = {str((root / page["source"]).resolve()): page["slug"] for page in pages}
    headings, used_ids = [], {}

    def link(target):
        parsed = urlsplit(target)
        if parsed.scheme or target.startswith("//"):
            return target if parsed.scheme in ("https", "http", "mailto") else "#"
        if not parsed.path:
            return target
        path = (source.parent / parsed.path).resolve()
        slug = by_source.get(str(path))
        if slug:
            return f"/docs/{slug}/" + (f"#{parsed.fragment}" if parsed.fragment else "")
        try:
            relative = path.relative_to(root).as_posix()
        except ValueError:
            return "#"
        return GITHUB + relative + (f"#{parsed.fragment}" if parsed.fragment else "")

    def inline(value):
        tokens = []

        def keep(fragment):
            tokens.append(fragment)
            return f"\x00{len(tokens) - 1}\x00"

        value = re.sub(r"`([^`]+)`", lambda m: keep(f"<code>{html.escape(m[1])}</code>"), value)
        value = re.sub(r"\[([^\]]+)\]\(([^)]+)\)", lambda m: keep(
            f'<a href="{html.escape(link(m[2]), quote=True)}">{html.escape(m[1])}</a>'
        ), value)
        value = html.escape(value)
        value = re.sub(r"\*\*([^*]+)\*\*", r"<strong>\1</strong>", value)
        value = re.sub(r"(?<!\*)\*([^*]+)\*(?!\*)", r"<em>\1</em>", value)
        return re.sub(r"\x00(\d+)\x00", lambda m: tokens[int(m[1])], value)

    output, paragraph, list_kind = [], [], None

    def flush():
        nonlocal list_kind
        if paragraph:
            output.append("<p>" + inline(" ".join(paragraph)) + "</p>")
            paragraph.clear()
        if list_kind:
            output.append(f"</{list_kind}>")
            list_kind = None

    lines = text.splitlines()
    index = 0
    while index < len(lines):
        line = lines[index]
        index += 1
        if line.startswith("```"):
            flush()
            language, code = line[3:].strip(), []
            while index < len(lines) and not lines[index].startswith("```"):
                code.append(lines[index])
                index += 1
            index += 1
            output.append('<div class="code-block"><button class="copy-code" aria-label="Copy code">Copy</button>'
                          f'<pre><code class="language-{html.escape(language)}">{html.escape(chr(10).join(code))}</code></pre></div>')
        elif match := re.match(r"^(#{1,6}) (.+)$", line):
            flush()
            level, title = len(match[1]), match[2]
            anchor = re.sub(r"[^a-z0-9 -]", "", title.lower()).replace(" ", "-")
            occurrence = used_ids.get(anchor, 0)
            used_ids[anchor] = occurrence + 1
            anchor += f"-{occurrence}" if occurrence else ""
            output.append(f'<h{level} id="{anchor}">{inline(title)}</h{level}>')
            if level == 2:
                headings.append((anchor, title))
        elif line.startswith("|"):
            flush()
            rows = [line]
            while index < len(lines) and lines[index].startswith("|"):
                rows.append(lines[index])
                index += 1
            output.append('<div class="table-scroll"><table>')
            for row_index, row in enumerate(rows):
                cells = row.strip().strip("|").split("|")
                if all(re.fullmatch(r"\s*:?-+:?\s*", cell) for cell in cells):
                    continue
                cell_tag = "th" if row_index == 0 else "td"
                output.append("<tr>" + "".join(f"<{cell_tag}>{inline(cell.strip())}</{cell_tag}>" for cell in cells) + "</tr>")
            output.append("</table></div>")
        elif match := re.match(r"^(?:- |\d+\. )(.+)$", line):
            kind = "ul" if line.startswith("- ") else "ol"
            if kind != list_kind:
                flush()
                output.append(f"<{kind}>")
                list_kind = kind
            output.append("<li>" + inline(match[1]) + "</li>")
        elif not line.strip():
            flush()
        else:
            if list_kind:
                flush()
            paragraph.append(line)
    flush()
    return "\n".join(output), headings
