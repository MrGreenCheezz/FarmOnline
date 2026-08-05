# Контрольный лист иконок магазина.
#
# Иконку товара нельзя оценить поштучно: в витрине они стоят рядом, и вопрос всегда
# один — не выпадает ли что-то из общей семьи. Лист собирает все Shop_*.asset так же,
# как их видит игрок: по вкладкам, с подписями, в реальном размере.
#
# Иконка берётся по тем же правилам, что и в игре (ShopItemDefinition.Icon):
# своё поле _icon, а если пусто — иконка постройки. Расхождение с игрой сделало бы
# лист бесполезным.
#
#   python Tools/icons-preview.py
import pathlib
import re
from PIL import Image, ImageDraw, ImageFont

ROOT = pathlib.Path(__file__).resolve().parent.parent
FARMING = ROOT / "Assets/Game/Farming"
OUT = ROOT / "Tools/icons-preview.png"

CELL, PAD, LABEL, COLS = 128, 10, 26, 8
TABS = ["Растения", "Животные", "Постройки", "Руда", "Декор"]

PAPER = (247, 244, 238)
INK = (38, 36, 33)
FADED = (120, 116, 110)
RULE = (214, 208, 198)


def font(size, bold=False):
    for name in (["seguisb.ttf", "arialbd.ttf"] if bold else ["segoeui.ttf", "arial.ttf"]):
        path = pathlib.Path("C:/Windows/Fonts") / name
        if path.exists():
            return ImageFont.truetype(str(path), size)
    return ImageFont.load_default()


def unescape(text):
    text = text.strip().strip('"')
    return re.sub(r"\\u([0-9a-fA-F]{4})", lambda m: chr(int(m.group(1), 16)), text)


def guid_map(pattern):
    """guid -> путь ассета, чтобы ходить по ссылкам как это делает Unity."""
    out = {}
    for meta in ROOT.rglob(pattern):
        m = re.search(r"guid: ([0-9a-f]{32})", meta.read_text(encoding="utf8"))
        if m:
            out[m.group(1)] = meta.with_suffix("")
    return out


def field(text, name):
    m = re.search(rf"^  {name}: (.*)$", text, re.M)
    return m.group(1) if m else ""


def ref(text, name):
    m = re.search(rf"^  {name}: \{{fileID: \d+(?:, guid: ([0-9a-f]{{32}}))?", text, re.M)
    return m.group(1) if m and m.group(1) else None


def collect():
    pngs = guid_map("Assets/Game/UI/Icons/*.png.meta")
    assets = guid_map("Assets/Game/Farming/*.asset.meta")

    items = []
    for path in sorted(FARMING.glob("Shop_*.asset")):
        text = path.read_text(encoding="utf8")
        icon = ref(text, "_icon")

        # Тот же откат, что в ShopItemDefinition.Icon: постройку рисуют один раз.
        if icon is None:
            building = ref(text, "_building")
            if building and building in assets:
                icon = ref(assets[building].read_text(encoding="utf8"), "_icon")

        items.append({
            "name": unescape(field(text, "_displayName")) or path.stem,
            "tab": int(field(text, "_category") or 0),
            "png": pngs.get(icon),
        })
    return items


def main():
    items = collect()
    title_f, tab_f, name_f = font(30, True), font(20, True), font(15)

    groups = [(TABS[t], [i for i in items if i["tab"] == t]) for t in range(len(TABS))]
    groups = [g for g in groups if g[1]]

    height = 74
    for _, group in groups:
        rows = (len(group) + COLS - 1) // COLS
        height += 42 + rows * (CELL + LABEL + PAD)
    width = COLS * (CELL + PAD) + PAD

    sheet = Image.new("RGB", (width, height), PAPER)
    draw = ImageDraw.Draw(sheet)
    draw.text((PAD, 22), f"Иконки магазина — {len(items)} товаров", INK, title_f)

    y = 74
    for tab, group in groups:
        draw.text((PAD, y), f"{tab} · {len(group)}", INK, tab_f)
        draw.line([(PAD, y + 30), (width - PAD, y + 30)], RULE, 1)
        y += 42

        for index, item in enumerate(group):
            x = PAD + (index % COLS) * (CELL + PAD)
            row = y + (index // COLS) * (CELL + LABEL + PAD)

            if item["png"] is None:
                # Дырка в витрине обязана быть видна на листе, а не тихо съесть клетку.
                draw.rectangle([x, row, x + CELL, row + CELL], outline=(200, 60, 60), width=2)
                draw.text((x + 8, row + CELL // 2), "нет иконки", (200, 60, 60), name_f)
            else:
                glyph = Image.open(item["png"]).convert("RGBA")
                if glyph.size != (CELL, CELL):
                    glyph = glyph.resize((CELL, CELL), Image.LANCZOS)
                sheet.paste(glyph, (x, row), glyph)

            label = item["name"]
            while draw.textlength(label, name_f) > CELL and len(label) > 4:
                label = label[:-2] + "…"
            draw.text((x, row + CELL + 5), label, FADED, name_f)

        y += ((len(group) + COLS - 1) // COLS) * (CELL + LABEL + PAD)

    sheet.save(OUT)
    missing = [i["name"] for i in items if i["png"] is None]
    print("товаров:", len(items), "без иконки:", len(missing))
    if missing:
        print("без иконки:", ", ".join(missing))


main()
