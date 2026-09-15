"""Build the distributable PDF Correctorium design document from its Markdown sources."""

from __future__ import annotations

import argparse
import html
import re
import subprocess
import tempfile
import time
from pathlib import Path

from PIL import Image as PillowImage, ImageChops
from reportlab.lib import colors
from reportlab.lib.enums import TA_CENTER
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.units import mm
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import (
    BaseDocTemplate,
    Frame,
    Image,
    KeepTogether,
    ListFlowable,
    ListItem,
    PageBreak,
    PageTemplate,
    Paragraph,
    Preformatted,
    Spacer,
    Table,
    TableStyle,
)
from reportlab.platypus.tableofcontents import TableOfContents


LINK_RE = re.compile(r"\[([^]]+)]\(([^)]+)\)")
INLINE_CODE_RE = re.compile(r"`([^`]+)`")
EMPHASIS_RE = re.compile(r"\*\*([^*]+)\*\*")


def inline_markup(value: str) -> str:
    """Convert the small Markdown subset used by the design sources to ReportLab markup."""
    placeholders: list[str] = []

    def preserve(markup: str) -> str:
        placeholders.append(markup)
        return f"\x00{len(placeholders) - 1}\x00"

    value = LINK_RE.sub(
        lambda match: preserve(
            f'<link href="{html.escape(match.group(2), quote=True)}" color="#1565C0">'
            f'{html.escape(match.group(1))}</link>'
        ),
        value,
    )
    value = INLINE_CODE_RE.sub(
        lambda match: preserve(f'<font name="DocMono" color="#7A2533">{html.escape(match.group(1))}</font>'),
        value,
    )
    value = EMPHASIS_RE.sub(
        lambda match: preserve(f"<b>{html.escape(match.group(1))}</b>"), value
    )
    value = html.escape(value)
    for index, markup in enumerate(placeholders):
        value = value.replace(f"\x00{index}\x00", markup)
    return value.replace("  ", " &nbsp;")


class DesignDocTemplate(BaseDocTemplate):
    def __init__(self, path: Path, styles: dict[str, ParagraphStyle]):
        super().__init__(
            str(path), pagesize=A4, rightMargin=18 * mm, leftMargin=18 * mm,
            topMargin=18 * mm, bottomMargin=17 * mm,
            title="PDF Correctorium 開発・設計ドキュメント",
            author="PDF Correctorium Project",
            subject="PDF Correctorium dev.161 implementation and design specification",
        )
        self.styles = styles
        frame = Frame(self.leftMargin, self.bottomMargin, self.width, self.height, id="normal")
        self.addPageTemplates(PageTemplate(id="design", frames=[frame], onPage=self.draw_page))

    def draw_page(self, canvas, doc) -> None:
        canvas.saveState()
        canvas.setFont("DocSans", 7.5)
        canvas.setFillColor(colors.HexColor("#64748B"))
        canvas.drawString(18 * mm, 9 * mm, "PDF Correctorium 開発・設計ドキュメント - dev.161")
        canvas.drawRightString(A4[0] - 18 * mm, 9 * mm, str(doc.page))
        canvas.restoreState()

    def afterFlowable(self, flowable) -> None:
        if isinstance(flowable, Paragraph) and flowable.style.name in {"Heading1", "Heading2"}:
            level = 0 if flowable.style.name == "Heading1" else 1
            text = flowable.getPlainText()
            key = f"heading-{self.seq.nextf('heading')}"
            self.canv.bookmarkPage(key)
            self.canv.addOutlineEntry(text, key, level=level, closed=level > 0)
            self.notify("TOCEntry", (level, text, self.page, key))


def make_styles() -> dict[str, ParagraphStyle]:
    samples = getSampleStyleSheet()
    return {
        "Title": ParagraphStyle(
            "Title", parent=samples["Title"], fontName="DocSansBold", fontSize=25,
            leading=34, textColor=colors.HexColor("#C62828"), alignment=TA_CENTER,
            spaceAfter=12 * mm,
        ),
        "Subtitle": ParagraphStyle(
            "Subtitle", parent=samples["Normal"], fontName="DocSans", fontSize=11,
            leading=18, textColor=colors.HexColor("#475569"), alignment=TA_CENTER,
        ),
        "Heading1": ParagraphStyle(
            "Heading1", parent=samples["Heading1"], fontName="DocSansBold", fontSize=17,
            leading=23, textColor=colors.HexColor("#B91C1C"), spaceBefore=4 * mm,
            spaceAfter=3 * mm, keepWithNext=True,
        ),
        "Heading2": ParagraphStyle(
            "Heading2", parent=samples["Heading2"], fontName="DocSansBold", fontSize=13,
            leading=19, textColor=colors.HexColor("#263746"), spaceBefore=4 * mm,
            spaceAfter=2 * mm, keepWithNext=True,
        ),
        "Heading3": ParagraphStyle(
            "Heading3", parent=samples["Heading3"], fontName="DocSansBold", fontSize=10.5,
            leading=16, textColor=colors.HexColor("#334155"), spaceBefore=3 * mm,
            spaceAfter=1.5 * mm, keepWithNext=True,
        ),
        "Body": ParagraphStyle(
            "Body", parent=samples["BodyText"], fontName="DocSans", fontSize=8.6,
            leading=14, textColor=colors.HexColor("#1F2937"), spaceAfter=1.7 * mm,
            wordWrap="CJK",
        ),
        "Small": ParagraphStyle(
            "Small", parent=samples["BodyText"], fontName="DocSans", fontSize=7.2,
            leading=11, textColor=colors.HexColor("#334155"), wordWrap="CJK",
        ),
        "Code": ParagraphStyle(
            "Code", parent=samples["Code"], fontName="DocMono", fontSize=6.7,
            leading=9, leftIndent=3 * mm, rightIndent=3 * mm, spaceBefore=1.5 * mm,
            spaceAfter=2 * mm, backColor=colors.HexColor("#F1F5F9"),
            borderPadding=2 * mm,
        ),
        "Quote": ParagraphStyle(
            "Quote", parent=samples["BodyText"], fontName="DocSans", fontSize=8.3,
            leading=13, leftIndent=5 * mm, borderColor=colors.HexColor("#CBD5E1"),
            borderWidth=1, borderPadding=2 * mm, textColor=colors.HexColor("#475569"),
            wordWrap="CJK",
        ),
    }


def paragraph(text: str, style: ParagraphStyle) -> Paragraph:
    return Paragraph(inline_markup(text.strip()), style)


def markdown_story(path: Path, styles: dict[str, ParagraphStyle]) -> list:
    lines = path.read_text(encoding="utf-8").splitlines()
    story: list = []
    index = 0
    while index < len(lines):
        raw = lines[index].rstrip()
        stripped = raw.strip()
        if not stripped:
            index += 1
            continue
        if stripped.startswith("```"):
            language = stripped[3:].strip()
            index += 1
            code: list[str] = []
            while index < len(lines) and not lines[index].strip().startswith("```"):
                code.append(lines[index])
                index += 1
            index += 1
            prefix = f"[{language}]\n" if language else ""
            story.append(Preformatted(prefix + "\n".join(code), styles["Code"], maxLineLength=110))
            continue
        if stripped.startswith("#"):
            level = len(stripped) - len(stripped.lstrip("#"))
            text = stripped[level:].strip()
            story.append(paragraph(text, styles["Heading1" if level == 1 else "Heading2" if level == 2 else "Heading3"]))
            index += 1
            continue
        if stripped.startswith("|") and index + 1 < len(lines) and re.match(r"^\s*\|?\s*:?-+", lines[index + 1]):
            rows: list[list[str]] = []
            rows.append([cell.strip() for cell in stripped.strip("|").split("|")])
            index += 2
            while index < len(lines) and lines[index].strip().startswith("|"):
                rows.append([cell.strip() for cell in lines[index].strip().strip("|").split("|")])
                index += 1
            columns = max(len(row) for row in rows)
            data = []
            for row_index, row in enumerate(rows):
                row += [""] * (columns - len(row))
                style = styles["Small"]
                data.append([Paragraph(inline_markup(cell), style) for cell in row])
            table = Table(data, colWidths=[(A4[0] - 36 * mm) / columns] * columns, repeatRows=1, hAlign="LEFT")
            table.setStyle(TableStyle([
                ("FONTNAME", (0, 0), (-1, 0), "DocSansBold"),
                ("BACKGROUND", (0, 0), (-1, 0), colors.HexColor("#E2E8F0")),
                ("GRID", (0, 0), (-1, -1), 0.35, colors.HexColor("#CBD5E1")),
                ("VALIGN", (0, 0), (-1, -1), "TOP"),
                ("LEFTPADDING", (0, 0), (-1, -1), 3),
                ("RIGHTPADDING", (0, 0), (-1, -1), 3),
                ("TOPPADDING", (0, 0), (-1, -1), 3),
                ("BOTTOMPADDING", (0, 0), (-1, -1), 3),
            ]))
            story.extend([table, Spacer(1, 2 * mm)])
            continue
        if re.match(r"^[-*]\s+", stripped) or re.match(r"^\d+[.)]\s+", stripped):
            ordered = bool(re.match(r"^\d+[.)]\s+", stripped))
            items = []
            pattern = r"^\d+[.)]\s+" if ordered else r"^[-*]\s+"
            while index < len(lines) and re.match(pattern, lines[index].strip()):
                items.append(ListItem(paragraph(re.sub(pattern, "", lines[index].strip()), styles["Body"]), leftIndent=3 * mm))
                index += 1
            story.append(ListFlowable(items, bulletType="1" if ordered else "bullet", start="1", leftIndent=6 * mm, bulletFontName="DocSans"))
            story.append(Spacer(1, 1.5 * mm))
            continue
        if stripped.startswith(">"):
            story.append(paragraph(stripped.lstrip("> "), styles["Quote"]))
            index += 1
            continue
        if stripped in {"---", "***"}:
            story.append(Spacer(1, 3 * mm))
            index += 1
            continue
        parts = [stripped]
        index += 1
        while index < len(lines):
            candidate = lines[index].strip()
            if not candidate or candidate.startswith(("#", "```", "|", ">")) or re.match(r"^[-*]\s+|^\d+[.)]\s+", candidate):
                break
            parts.append(candidate)
            index += 1
        story.append(paragraph(" ".join(parts), styles["Body"]))
    return story


def render_svg(svg: Path, destination: Path, chrome: Path) -> bool:
    command = [
        str(chrome), "--headless=new", "--disable-gpu", "--disable-software-rasterizer",
        "--no-sandbox", f"--user-data-dir={destination.parent / (destination.stem + '-profile')}", "--hide-scrollbars",
        "--window-size=1800,1200", f"--screenshot={destination}", svg.resolve().as_uri(),
    ]
    subprocess.run(
        command, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=45, check=False
    )
    for _ in range(50):
        if destination.exists() and destination.stat().st_size > 0:
            return True
        time.sleep(0.1)
    return False


def build(source_root: Path, output: Path, chrome: Path) -> None:
    regular_font = Path(r"C:\Windows\Fonts\meiryo.ttc")
    bold_font = Path(r"C:\Windows\Fonts\meiryob.ttc")
    if not regular_font.exists() or not bold_font.exists():
        raise FileNotFoundError("The Meiryo Japanese fonts are required to build the design PDF.")
    pdfmetrics.registerFont(TTFont("DocSans", str(regular_font), subfontIndex=0))
    pdfmetrics.registerFont(TTFont("DocSansBold", str(bold_font), subfontIndex=0))
    pdfmetrics.registerFont(TTFont("DocMono", r"C:\Windows\Fonts\consola.ttf"))
    styles = make_styles()
    story: list = [
        Spacer(1, 38 * mm),
        Paragraph("PDF Correctorium", styles["Title"]),
        Paragraph("開発・設計ドキュメント", styles["Title"]),
        Paragraph("実装基準: v1.0.0-dev.161 / プロジェクト形式 1.5", styles["Subtitle"]),
        Spacer(1, 12 * mm),
        Paragraph("2026-09-15 再生成版", styles["Subtitle"]),
        PageBreak(),
        Paragraph("目次", styles["Heading1"]),
    ]
    toc = TableOfContents()
    toc.levelStyles = [
        ParagraphStyle("TOC1", fontName="DocSans", fontSize=9, leading=14, leftIndent=0, firstLineIndent=0, textColor=colors.HexColor("#1F2937")),
        ParagraphStyle("TOC2", fontName="DocSans", fontSize=8, leading=12, leftIndent=8 * mm, firstLineIndent=0, textColor=colors.HexColor("#475569")),
    ]
    story.extend([toc, PageBreak()])

    sources = [source_root / "README.md"] + sorted((source_root / "docs").rglob("*.md"))
    for source_index, source in enumerate(sources):
        if source_index:
            story.append(PageBreak())
        story.extend(markdown_story(source, styles))

    svg_files = sorted((source_root / "assets" / "svg").glob("*.svg"))
    if svg_files:
        story.extend([PageBreak(), Paragraph("図版", styles["Heading1"])])
        with tempfile.TemporaryDirectory(prefix="pdf-correctorium-doc-") as temporary:
            temp = Path(temporary)
            for svg_index, svg in enumerate(svg_files):
                png = temp / f"{svg.stem}.png"
                if svg_index:
                    story.append(PageBreak())
                story.append(Paragraph(svg.stem, styles["Heading2"]))
                if render_svg(svg, png, chrome):
                    with PillowImage.open(png).convert("RGB") as image:
                        difference = ImageChops.difference(image, PillowImage.new("RGB", image.size, "white"))
                        bounds = difference.getbbox()
                        if bounds:
                            left, top, right, bottom = bounds
                            margin = 20
                            image = image.crop((max(0, left - margin), max(0, top - margin),
                                                min(image.width, right + margin), min(image.height, bottom + margin)))
                            image.save(png)
                        width, height = image.size
                    scale = min((A4[0] - 36 * mm) / width, (A4[1] - 55 * mm) / height, 1)
                    story.append(KeepTogether([Image(str(png), width=width * scale, height=height * scale), Spacer(1, 3 * mm)]))
                else:
                    story.append(Paragraph(f"図版を描画できませんでした: {html.escape(svg.name)}", styles["Body"]))

            output.parent.mkdir(parents=True, exist_ok=True)
            DesignDocTemplate(output, styles).multiBuild(story)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--chrome", type=Path, required=True)
    arguments = parser.parse_args()
    build(arguments.source_root.resolve(), arguments.output.resolve(), arguments.chrome.resolve())


if __name__ == "__main__":
    main()
