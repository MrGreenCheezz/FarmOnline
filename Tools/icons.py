# Пересборка иконок ресурсов.
#
# Исходники — сырые превью редактора: полностью непрозрачные, с запечённым серым фоном
# (82,82,82) на 79% площади. На маленьком экране это делало из всех ресурсов одинаковые
# серые квадратики, и различались только руды — у них яркий силуэт.
#
# Здесь фон вырезается, а под картинку подкладывается фишка: тон по линии, светлота по
# ступени. Это ровно то, что делает руды читаемыми, — компактный силуэт и свой цвет.
#
# Абсолютные значения от нетронутых исходников в Tools/icon-source: запускать можно
# сколько угодно раз, результат один и тот же.
#
#   python Tools/icons.py
import colorsys
import pathlib
from PIL import Image, ImageDraw, ImageFilter

ROOT = pathlib.Path(__file__).resolve().parent.parent
ICONS = ROOT / "Assets/Game/UI/Icons"
SOURCE = ROOT / "Tools/icon-source"   # нетронутые превью редактора, вне Assets
SIZE = 128
BACKDROP = (82, 82, 82)

# Линия -> (тон, ресурсы по возрастанию ступени).
LINES = {
    "crop":      (0.28, ["wheat", "corn", "pumpkin", "melon", "bamboo", "ginseng", "goldbloom"]),
    "wood":      (0.08, ["wood", "oak", "ironwood", "maple", "yew", "ebony", "crimsonwood", "spiritwood"]),
    "ore":       (0.56, ["copper", "iron", "silver", "gold", "platinum", "mithril", "adamant", "starmetal"]),
    "livestock": (0.95, ["meat", "cheese", "bacon", "eggs", "honey", "down", "antler"]),
    "crafted":   (0.75, ["plank", "beam", "ingot_copper", "ingot_iron"]),
    "night":     (0.45, ["firefly", "moondew"]),
}


def cut_backdrop(image):
    """
    Убрать фон заливкой от краёв, а не сравнением цвета по всей картинке.
    Разница существенная: у серебра и платины сама модель почти серая, и глобальный
    ключ выел бы из них дыры. Заливка снимает только то, что связано с краем.
    """
    rgb = image.convert("RGB")
    marker = (255, 0, 255)

    for corner in [(0, 0), (SIZE - 1, 0), (0, SIZE - 1), (SIZE - 1, SIZE - 1)]:
        ImageDraw.floodfill(rgb, corner, marker, thresh=12)

    alpha = Image.new("L", image.size, 255)
    pixels, mask = rgb.load(), alpha.load()
    for y in range(SIZE):
        for x in range(SIZE):
            if pixels[x, y] == marker:
                mask[x, y] = 0

    # Сглаживаем срез на полпикселя: без этого по краю остаётся лесенка.
    alpha = alpha.filter(ImageFilter.GaussianBlur(0.6))

    out = image.copy()
    out.putalpha(alpha)
    return out


def plate_color(hue, step, steps):
    """Тон от линии, светлота — от ступени: линия читается градиентом снизу вверх."""
    k = step / max(1, steps - 1)
    r, g, b = colorsys.hls_to_rgb(hue, 0.30 + k * 0.42, 0.80 - k * 0.22)
    return int(r * 255), int(g * 255), int(b * 255)


def plate(color):
    image = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)

    box = [3, 3, SIZE - 4, SIZE - 4]
    draw.rounded_rectangle(box, 24, fill=color + (255,))
    draw.rounded_rectangle(box, 24, outline=(255, 255, 255, 70), width=3)
    return image


def outline(glyph):
    """Тонкий тёмный контур: светлая модель на светлой фишке иначе теряет силуэт."""
    ring = glyph.getchannel("A").filter(ImageFilter.MaxFilter(5))
    edge = Image.new("RGBA", glyph.size, (14, 16, 14, 255))
    edge.putalpha(ring)
    return edge


def build(name, hue, step, steps):
    source = SOURCE / f"icon_{name}.png"
    if not source.exists():
        return None

    glyph = cut_backdrop(Image.open(source).convert("RGBA"))

    # Поля под фишку: силуэт не должен упираться в кромку.
    inner = int(SIZE * 0.82)
    padded = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    padded.alpha_composite(glyph.resize((inner, inner), Image.LANCZOS),
                           ((SIZE - inner) // 2, (SIZE - inner) // 2))

    result = plate(plate_color(hue, step, steps))
    result.alpha_composite(outline(padded))
    result.alpha_composite(padded)
    return result


def main():
    made, skipped = [], []
    for hue, names in LINES.values():
        for step, name in enumerate(names):
            image = build(name, hue, step, len(names))
            if image is None:
                skipped.append(name)
                continue
            image.save(ICONS / f"icon_{name}.png")
            made.append(name)

    print("пересобрано:", len(made))
    if skipped:
        print("нет исходника:", ", ".join(skipped))


main()
