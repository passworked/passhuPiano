"""Narrow, metadata-preserving discovery adapters for classical MIDI archives.

This module performs no network or file I/O.  The downloader owns robots.txt,
request pacing, retries, persistence, and MIDI/archive validation.  Only links
inside the selected catalogues are followed; navigation and audio links are not.
"""

from html.parser import HTMLParser
import re
from urllib.parse import parse_qs, urldefrag, urljoin, urlsplit


MUTOPIA_STYLES = ("Baroque", "Classical", "Romantic", "Renaissance", "Technique")
MUTOPIA_BASE = "https://www.mutopiaproject.org/"
PIANO_BASE = "http://piano-midi.de/"
PIANO_LICENSE = "Creative Commons Attribution-ShareAlike 3.0 Germany"
PIANO_LICENSE_URL = "http://creativecommons.org/licenses/by-sa/3.0/de/"
PIANO_COPYRIGHT_URL = PIANO_BASE + "copy.htm"
_PIANO_STYLES = {"Baroque", "Classicism", "Romanticism"}
_VOID_TAGS = {"area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr"}


class _Node:
    def __init__(self, tag, attrs=None, parent=None):
        self.tag = tag
        self.attrs = dict(attrs or ())
        self.parent = parent
        self.children = []

    def text(self):
        return _clean(" ".join(child.text() if isinstance(child, _Node) else child
                               for child in self.children))

    def nodes(self, tag=None):
        for child in self.children:
            if isinstance(child, _Node):
                if tag is None or child.tag == tag:
                    yield child
                yield from child.nodes(tag)

    def has_class(self, name):
        return name in self.attrs.get("class", "").split()


class _Document(HTMLParser):
    def __init__(self, raw):
        super().__init__(convert_charrefs=True)
        self.root = _Node("document")
        self.stack = [self.root]
        self.ordered = []
        # Both archives declare UTF-8.  Retain a sensible fallback for older
        # piano-midi mirrors, rather than turning accented composer names into ?.
        try:
            decoded = raw.decode("utf-8-sig")
        except UnicodeDecodeError:
            decoded = raw.decode("windows-1252", errors="replace")
        self.feed(decoded)
        self.close()

    def handle_starttag(self, tag, attrs):
        node = _Node(tag, attrs, self.stack[-1])
        self.stack[-1].children.append(node)
        self.ordered.append(node)
        if tag not in _VOID_TAGS:
            self.stack.append(node)

    def handle_startendtag(self, tag, attrs):
        self.handle_starttag(tag, attrs)
        if tag not in _VOID_TAGS:
            self.handle_endtag(tag)

    def handle_endtag(self, tag):
        for index in range(len(self.stack) - 1, 0, -1):
            if self.stack[index].tag == tag:
                del self.stack[index:]
                return

    def handle_data(self, data):
        self.stack[-1].children.append(data)


def _clean(value):
    return re.sub(r"\s+", " ", value.replace("\xa0", " ")).strip()


def _links(node, base):
    for anchor in node.nodes("a"):
        href = anchor.attrs.get("href", "").strip()
        if href and not href.startswith(("javascript:", "mailto:")):
            yield anchor, urljoin(base, href)


def _same_archive(url, archive):
    parts = urlsplit(url)
    return (parts.scheme in ("http", "https")
            and (parts.hostname or "").lower().removeprefix("www.") == archive)


def _job(url, source, kind, metadata):
    return {"url": urldefrag(url)[0], "source": source, "kind": kind, "metadata": dict(metadata)}


def _midi_kind(url):
    path = urlsplit(url).path.lower()
    if path.endswith((".mid", ".midi")):
        return "midi"
    if path.endswith("-mids.zip"):
        return "midi-zip"
    return None


def seed_jobs():
    """Yield the bounded catalogue entry points, with no item-count limit."""
    for style in MUTOPIA_STYLES:
        yield _job(MUTOPIA_BASE + "cgibin/make-table.cgi?Style=" + style,
                   "mutopia", "mutopia-page", {"style": style})
    yield _job(PIANO_BASE + "midi_files.htm", "piano-midi.de", "piano-index", {})


def _table_rows(table):
    # Ignore rows in nested tables, important for old HTML with layout tables.
    for row in table.nodes("tr"):
        ancestor = row.parent
        while ancestor is not None and ancestor.tag != "table":
            ancestor = ancestor.parent
        if ancestor is table:
            yield row


def _cells(row):
    return [child for child in row.children
            if isinstance(child, _Node) and child.tag in ("td", "th")]


def _mutopia_list(job, document):
    base = job["url"]
    style = job.get("metadata", {}).get("style")
    if style not in MUTOPIA_STYLES:
        style = parse_qs(urlsplit(base).query).get("Style", [""])[0]
    if style not in MUTOPIA_STYLES:
        return

    # Every result-table is one work.  Never derive copyright from the complete
    # listing: adjacent rows can carry entirely different licenses.
    for table in document.root.nodes("table"):
        if not table.has_class("result-table"):
            continue
        rows = [_cells(row) for row in _table_rows(table)]
        if len(rows) < 3 or any(len(rows[index]) < 3 for index in (0, 1, 2)):
            continue
        title = rows[0][0].text()
        composer = re.sub(r"^by\s+", "", rows[0][1].text(), flags=re.I)
        item_style = rows[1][2].text()
        if item_style != style or not title:
            continue
        copyright_cell = rows[2][1]
        license_links = list(_links(copyright_cell, base))
        info_links = [(anchor, url) for anchor, url in _links(rows[2][2], base)
                      if urlsplit(url).path.endswith("/piece-info.cgi")]
        candidates = [{"url": url, "kind": _midi_kind(url)}
                      for _, url in _links(table, base)
                      if _same_archive(url, "mutopiaproject.org") and _midi_kind(url)]
        if not candidates:
            continue
        source_page = info_links[0][1] if info_links else base
        metadata = {
            "title": title,
            "composer": composer,
            "style": item_style,
            "instrument": re.sub(r"^for\s+", "", rows[1][0].text(), flags=re.I),
            "license": copyright_cell.text(),
            "license_url": license_links[0][1] if license_links else MUTOPIA_BASE + "legal.html",
            "source_page": source_page,
            "source_edition": rows[2][0].text(),
            "attribution": "{}; {}; The Mutopia Project; {}".format(title, composer, source_page),
        }
        if info_links:
            # Enrich with the individual contributor before any MIDI is queued.
            metadata["_download_candidates"] = candidates
            yield _job(source_page, "mutopia", "mutopia-piece", metadata)
        else:
            for candidate in candidates:
                yield _job(candidate["url"], "mutopia", candidate["kind"], metadata)

    for _, url in _links(document.root, base):
        parts = urlsplit(url)
        query = parse_qs(parts.query, keep_blank_values=True)
        if (not _same_archive(url, "mutopiaproject.org")
                or parts.path != "/cgibin/make-table.cgi"
                or query.get("Style") != [style]):
            continue
        # Only genuine pagination links, never composer, search or style menus.
        offsets = [values[0] for key, values in query.items()
                   if key.lower() in ("startat", "start", "offset") and values]
        if offsets and all(value.isdigit() for value in offsets):
            yield _job(url, "mutopia", "mutopia-page", {"style": style})


def _mutopia_piece(job, document):
    base = job["url"]
    metadata = dict(job.get("metadata", {}))
    candidates = metadata.pop("_download_candidates", [])
    metadata["source_page"] = base
    heading = next(document.root.nodes("h2"), None)
    composer = next(document.root.nodes("h4"), None)
    if heading is not None and heading.text():
        metadata["title"] = heading.text()
    if composer is not None and composer.text():
        metadata["composer"] = re.sub(r"^by\s+", "", composer.text(), flags=re.I)

    for cell in document.root.nodes("td"):
        text = cell.text()
        if text.startswith("Copyright:"):
            # Ignore the icon's alt text and preserve the first human license.
            links = list(_links(cell, base))
            labeled = [(anchor, url) for anchor, url in links if anchor.text()]
            if labeled:
                metadata["license"] = labeled[0][0].text()
                metadata["license_url"] = labeled[0][1]
            for _, url in links:
                if (urlsplit(url).hostname or "").lower().endswith("creativecommons.org"):
                    metadata["license_url"] = url
                    break
        elif text.startswith("Maintainer:"):
            metadata["maintainer"] = _clean(text.partition(":")[2])
        elif text.startswith("Music ID Number:"):
            metadata["mutopia_id"] = _clean(text.partition(":")[2])
        elif text.startswith("Style:"):
            metadata["style"] = _clean(text.partition(":")[2])

    if metadata.get("style") not in MUTOPIA_STYLES:
        return
    attribution = [metadata.get("title", ""), metadata.get("composer", "")]
    if metadata.get("maintainer"):
        attribution.append("Maintainer: " + metadata["maintainer"])
    attribution.extend(("The Mutopia Project", base))
    metadata["attribution"] = "; ".join(value for value in attribution if value)
    found = [{"url": url, "kind": _midi_kind(url)}
             for _, url in _links(document.root, base)
             if _same_archive(url, "mutopiaproject.org") and _midi_kind(url)]
    seen = set()
    for candidate in found or candidates:
        url = candidate["url"]
        if url not in seen and _same_archive(url, "mutopiaproject.org"):
            seen.add(url)
            yield _job(url, "mutopia", candidate["kind"], metadata)


def _piano_index(job, document):
    base = job["url"]
    for row in document.root.nodes("tr"):
        if not row.has_class("midi"):
            continue
        cells = _cells(row)
        if len(cells) < 4 or cells[3].text() not in _PIANO_STYLES:
            continue
        for anchor, url in _links(cells[0], base):
            parts = urlsplit(url)
            if (_same_archive(url, "piano-midi.de")
                    and re.fullmatch(r"/[a-z]+\.htm", parts.path)):
                yield _job(url, "piano-midi.de", "piano-page", {
                    "composer": anchor.text(), "style": cells[3].text(),
                    "birth_year": cells[1].text(), "death_year": cells[2].text(),
                })


def _piano_page(job, document):
    base = job["url"]
    inherited = job.get("metadata", {})
    heading = next(document.root.nodes("h1"), None)
    composer = inherited.get("composer") or (heading.text() if heading else "")
    work = ""
    subsection = ""
    seen = set()
    # Ordered headings retain the work title for generic labels like
    # "1. Movement", and the primary row title also applies to Format 0.
    for node in document.ordered:
        if node.tag == "h2":
            work = node.text()
            subsection = ""
            continue
        if node.tag == "h3":
            subsection = node.text()
            continue
        if node.tag != "tr" or not node.has_class("midi"):
            continue
        cells = _cells(node)
        if not cells:
            continue
        row_links = [(anchor, url) for anchor, url in _links(node, base)
                     if _same_archive(url, "piano-midi.de")
                     and urlsplit(url).path.startswith("/midis/")
                     and _midi_kind(url) == "midi"]
        if not row_links:
            continue
        primary_title = row_links[0][0].text()
        titles = [value for value in (work, subsection, primary_title)
                  if value and value not in ("♫", "MIDI")]
        # Avoid repeating an identical work name in headings and links.
        title = " - ".join(dict.fromkeys(titles))
        for _, url in row_links:
            if url in seen:
                continue
            seen.add(url)
            metadata = {
                "title": title or urlsplit(url).path.rsplit("/", 1)[-1],
                "composer": composer,
                "style": inherited.get("style", "Classical piano"),
                "instrument": "Piano",
                "license": PIANO_LICENSE,
                "license_url": PIANO_LICENSE_URL,
                "copyright_page": PIANO_COPYRIGHT_URL,
                "source_page": base,
                "performer": "Bernd Krueger",
                "midi_format": "0" if "format0" in urlsplit(url).path.lower() else "1",
                "attribution": "{}; {}; MIDI sequence by Bernd Krueger; {}; {}".format(
                    title, composer, PIANO_BASE, PIANO_LICENSE),
            }
            if len(cells) >= 4:
                metadata.update(tempo=cells[1].text(), duration=cells[2].text(),
                                sequence_date=cells[3].text())
            yield _job(url, "piano-midi.de", "midi", metadata)


def process_page(job, raw):
    """Yield discovered jobs from an already fetched catalogue/detail page."""
    document = _Document(raw)
    kind = job.get("kind")
    if kind == "mutopia-page":
        yield from _mutopia_list(job, document)
    elif kind == "mutopia-piece":
        yield from _mutopia_piece(job, document)
    elif kind == "piano-index":
        yield from _piano_index(job, document)
    elif kind == "piano-page":
        yield from _piano_page(job, document)
