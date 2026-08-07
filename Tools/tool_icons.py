# -*- coding: utf-8 -*-
"""Иконки инструментов для панели снизу: рука, перенос, ведро, корм, удобрение.

Почему рисуем, а не рендерим префабы, как остальные иконки (icons.py): у инструментов
нет вещей в мире. Ведро и мешок корма — это НЕ предметы фермы, а способ действия, и
снимать их нечего. Рисуем силуэтами того же кубического склада, что и вся игра.

Фон прозрачный, без плашки: иконка ложится на деревянную карточку панели, и вторая
подложка под ней читалась бы как наклейка на кнопке.

Запуск:  python Tools/tool_icons.py
Кладёт PNG в Assets/Game/UI/Icons/tool_*.png; спрайт-импорт делает редактор
(Farm → Онлайн → Досоздать иконки инструментов) — он же дописывает .meta.
"""

import io
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

OUT = Path(__file__).resolve().parent.parent / "Assets/Game/UI/Icons"
SIZE = 128

# Палитра игры (Theme.uss): тёплое дерево, зелень травы, вода, солома, кость.
WOOD = (146, 96, 52)
WOOD_DARK = (104, 66, 34)
SKIN = (226, 178, 128)
STEEL = (150, 158, 166)
STEEL_DARK = (104, 112, 122)
WATER = (86, 154, 196)
STRAW = (214, 174, 84)
STRAW_DARK = (170, 132, 56)
LEAF = (122, 168, 74)
SACK = (198, 176, 140)
SACK_DARK = (150, 128, 94)
INK = (44, 32, 22)


def canvas():
    return Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))


def outline(glyph, width=4, color=INK):
    """Тёмная кайма по контуру: иконка ложится на дерево карточки, и без каймы
    силуэт сливается с подложкой ровно там, где тон совпал."""
    alpha = glyph.getchannel("A")
    grown = alpha.filter(ImageFilter.MaxFilter(width * 2 + 1))
    ring = Image.new("RGBA", glyph.size, color + (255,))
    ring.putalpha(grown)
    out = Image.alpha_composite(ring, glyph)
    return out


def hand():
    """Рука: ладонь с пальцами. Жест по умолчанию — «собрать»."""
    img = canvas()
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([38, 54, 92, 104], 12, fill=SKIN)          # ладонь
    for i, x in enumerate((44, 58, 72)):                            # три пальца
        top = 30 if i == 1 else 38
        d.rounded_rectangle([x, top, x + 12, 62], 6, fill=SKIN)
    d.rounded_rectangle([84, 52, 100, 78], 8, fill=SKIN)            # большой палец
    return outline(img)


def move():
    """Перенос: четыре стрелки от центра — «возьми и переставь»."""
    img = canvas()
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([56, 56, 72, 72], 4, fill=WOOD)             # центр
    d.rectangle([60, 24, 68, 104], fill=WOOD)                       # вертикаль
    d.rectangle([24, 60, 104, 68], fill=WOOD)                       # горизонталь
    d.polygon([(64, 12), (48, 32), (80, 32)], fill=WOOD)            # вверх
    d.polygon([(64, 116), (48, 96), (80, 96)], fill=WOOD)           # вниз
    d.polygon([(12, 64), (32, 48), (32, 80)], fill=WOOD)            # влево
    d.polygon([(116, 64), (96, 48), (96, 80)], fill=WOOD)           # вправо
    return outline(img)


def bucket():
    """Ведро с водой: трапеция, дужка и вода внутри."""
    img = canvas()
    d = ImageDraw.Draw(img)
    d.arc([34, 24, 94, 76], start=180, end=360, fill=STEEL_DARK, width=7)   # дужка
    d.polygon([(34, 52), (94, 52), (84, 108), (44, 108)], fill=STEEL)       # корпус
    d.polygon([(38, 60), (90, 60), (86, 76), (42, 76)], fill=WATER)         # вода
    d.rectangle([34, 48, 94, 56], fill=STEEL_DARK)                          # обод
    return outline(img)


def feed():
    """Корм: сноп сена, перевязанный жгутом.

    Соломины разнесены с зазором и обведены каждая: слитые в один блок, они читались
    как жёлтый кирпич — иконка обязана называть себя с одного взгляда, иначе игрок
    ищет корм среди пяти карточек глазами, а не рукой.
    """
    img = canvas()
    d = ImageDraw.Draw(img)

    # Веер соломин от общего низа: наклон разный, поэтому сноп читается снопом,
    # а не забором. Каждая рисуется на своём слое и обводится отдельно.
    straws = ((36, 20, 54, 100), (52, 12, 62, 100), (70, 14, 70, 100), (86, 22, 78, 100))
    for x_top, y_top, x_bottom, y_bottom in straws:
        piece = canvas()
        p = ImageDraw.Draw(piece)
        p.polygon([(x_top - 7, y_top), (x_top + 7, y_top),
                   (x_bottom + 8, y_bottom), (x_bottom - 8, y_bottom)], fill=STRAW)
        img.alpha_composite(outline(piece, 3))

    d.rounded_rectangle([28, 66, 100, 82], 4, fill=STRAW_DARK)               # перевязь
    d.rounded_rectangle([32, 96, 96, 110], 4, fill=STRAW_DARK)               # основание
    return outline(img)


def fertilizer():
    """Удобрение: мешок с зелёным ростком — прибавка урожая, а не ускорение."""
    img = canvas()
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([34, 46, 94, 108], 10, fill=SACK)                    # мешок
    d.rounded_rectangle([44, 34, 84, 52], 8, fill=SACK_DARK)                 # горловина
    d.rectangle([60, 62, 68, 96], fill=LEAF)                                 # стебель
    d.ellipse([38, 60, 62, 78], fill=LEAF)                                   # левый лист
    d.ellipse([66, 60, 90, 78], fill=LEAF)                                   # правый лист
    return outline(img)


TOOLS = {
    "tool_hand": hand,
    "tool_move": move,
    "tool_water": bucket,
    "tool_feed": feed,
    "tool_fert": fertilizer,
}


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    for name, draw in TOOLS.items():
        path = OUT / (name + ".png")
        draw().save(path)
        print("нарисовано:", path.name)
    print("Готово. Дальше — Farm → Онлайн → Досоздать иконки инструментов (спрайт-импорт).")


if __name__ == "__main__":
    main()
