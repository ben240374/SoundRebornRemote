#!/usr/bin/env python3
"""Régénère docs/manual/xx.html à partir de xx.md.

L'enveloppe (en-tête, styles, barre de langues, pied) est reprise de la page
existante ; seul le corps est reconstruit. Usage : build_manuals.py <dossier docs>
"""
import pathlib
import re
import sys

import markdown

docs = pathlib.Path(sys.argv[1])


def slug(value, separator):
    value = re.sub(r"[^\w\s-]", "", value.lower())
    return re.sub(r"\s", separator, value)


for lang in ("fr", "en", "de", "nl", "es", "it"):
    md_path = docs / "manual" / f"{lang}.md"
    html_path = docs / "manual" / f"{lang}.html"

    old = html_path.read_text(encoding="utf-8")
    head_end = old.index('</div></header>\n<div class="wrap">\n') + len('</div></header>\n<div class="wrap">\n')
    foot_start = old.index("<footer>")
    langbar = re.search(r'<ul class="langbar">.*?</ul>', old, re.S).group(0)

    lines = md_path.read_text(encoding="utf-8").split("\n")
    marks = [i for i, l in enumerate(lines) if l.startswith("> ") and "](" in l and ".md)" in l]
    assert len(marks) == 1, (lang, marks)
    lines[marks[0]] = "LANGBARPLACEHOLDER"

    body = markdown.markdown(
        "\n".join(lines),
        extensions=["toc", "tables", "sane_lists"],
        extension_configs={"toc": {"slugify": slug}},
    )
    assert body.count("<p>LANGBARPLACEHOLDER</p>") == 1, lang
    body = body.replace("<p>LANGBARPLACEHOLDER</p>\n", langbar)

    html_path.write_text(old[:head_end] + body + "\n" + old[foot_start:], encoding="utf-8")
    print(lang, "ok")
