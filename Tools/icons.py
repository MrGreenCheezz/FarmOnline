# Пересборка иконок ресурсов, товаров и декора.
#
# Исходники — сырые рендеры: часть с прозрачным фоном (наш рендер построек и декора),
# часть с запечённым серым (82,82,82) на 79% площади (старые превью редактора). На
# маленьком экране второе делало из всех ресурсов одинаковые серые квадратики.
#
# Здесь фон вырезается, силуэт нормируется по боксу, а под него кладётся плашка:
# тон — линия ресурса, ступень — насечки, привязка декора — значок постройки.
#
# Абсолютные значения от нетронутых исходников в Tools/icon-source: запускать можно
# сколько угодно раз, результат один и тот же.
#
#   python Tools/icons.py
#
# ---------------------------------------------------------------------------
# Что переделано 06.08.2026 по разбору арт-критика (замечания 7, 8, 9)
#
# 7. «Конфетти поверх дерева». Было: светлота плашки кодировала ступень и гуляла от
#    0.30 до 0.72 внутри одной линии — рядом стояли почти чёрная и почти белая руда,
#    и это читалось не как язык, а как случайность. Стало: у линии одна плашка,
#    уравненная по ЯРКОСТИ (не по числу L — синий и жёлтый при равном L различаются
#    по яркости вдвое), а ступень ушла на насечки. Плюс убрано белое кольцо 3px,
#    которое было самой яркой кромкой в строке магазина, и радиус приведён к общему.
#
#    Почему насечки, а не рамка и не уголок. Ступеней до восьми. Светлота рамки —
#    тот же градиент, только в кромке: порядок читается, а «какая именно ступень» —
#    нет, и на тёмном дереве светлая рамка возвращает ровно ту беду, ради которой
#    убрано кольцо. Уголок кодирует три-четыре значения, дальше срывается. Насечки
#    дают оба чтения сразу: издали работает ДЛИНА ряда (положение и длина — самые
#    сильные каналы восприятия, куда сильнее светлоты), вблизи ряд просто
#    пересчитывается пальцем. И ряд лежит в своей полосе внизу, не трогая ни фон,
#    ни силуэт, — конфетти из него не получится по построению.
#
# 8. Дубли. Восемь товаров декора из восемнадцати — это ТРИ предмета: три фонаря,
#    две бочки и три клумбы ссылаются на один и тот же префаб (совпадают и guid,
#    и fileID). Разводить их ракурсом значило бы соврать: предмет один. Но товары
#    разные по-настоящему — они отличаются местом, и место лежит в данных
#    (ImprovementDefinition._anchorBuilding). Поэтому в углу плашки стоит значок
#    постройки-хозяйки: «фонарь у хлева» и «шахтёрский фонарь» различаются тем же,
#    чем различаются в игре. Ничего не выдумано, показано существующее.
#
# 9. Размер силуэта. Было: весь кадр жался в 0.82 SIZE, и величина предмета
#    определялась тем, сколько пустоты оказалось в рендере, — от 30% плашки у валунов
#    до 85% у дыни. Стало: обрезка по непрозрачным пикселям и вписывание в общий бокс.
import colorsys
import pathlib
import re
from PIL import Image, ImageDraw, ImageFilter

ROOT = pathlib.Path(__file__).resolve().parent.parent
ICONS = ROOT / "Assets/Game/UI/Icons"
SOURCE = ROOT / "Tools/icon-source"   # нетронутые рендеры, вне Assets
FARMING = ROOT / "Assets/Game/Farming"
SIZE = 128

# Плашка. Радиус 8 экранных пикселей: иконка показывается в 56 px, значит в исходнике
# 8 * 128/56 ≈ 18. Раньше стояло 24 — это 10.5 на экране против 8 у строки товара и
# 4 у деревянной кнопки, три разных скругления в одной строке.
MARGIN = 3
RADIUS = 18

# Общий бокс силуэта — один на все иконки, вместе с полосой под насечки внизу.
# Полоса пустует у семей без лестницы (постройки, декор, эмоции), и это правильно:
# отсутствие насечек — само по себе сообщение «ступени тут нет».
BOX = 100
BOX_CENTER = (SIZE // 2, 57)
PIP_W, PIP_H, PIP_GAP, PIP_Y = 8, 6, 5, 112

# Медианное покрытие бокса по всему набору. Сплошное пятно (дыня, сыр) при том же
# боксе выглядит тяжелее скелета (плетень, пугало), поэтому плотные силуэты чуть
# ужимаются. Поправка только в минус: за бокс не выходит никто.
COVER_MEDIAN = 0.60
COVER_FLOOR = 0.85

# Целевая яркость плашки — середина между двумя уже проверенными на экране: тан
# построек давал 0.298, слива декора 0.240, и обе читались. Все остальные линии
# подгоняются под это число, поэтому светлее или темнее соседа не бывает ни одна.
TARGET_LUMA = 0.27

# Линия -> (тон, насыщенность, ресурсы по возрастанию ступени).
#
# Тон разводит линии, ступень — насечки. Насыщенность разная не по вкусу: у руды
# самородки различаются ТОЛЬКО цветом (медь, золото, платина, мифрил, адамант —
# один и тот же кусок), и сочная плашка съедала бы этот единственный признак,
# поэтому у руды почти серый сланец. У растений наоборот: стебли тусклые, плашке
# можно быть сочной.
LINES = {
    "crop":      (0.30, 0.40, ["wheat", "corn", "pumpkin", "melon", "bamboo", "ginseng", "goldbloom"]),
    "wood":      (0.055, 0.50, ["wood", "oak", "ironwood", "maple", "yew", "ebony", "crimsonwood", "spiritwood"]),
    "ore":       (0.58, 0.16, ["copper", "iron", "silver", "gold", "platinum", "mithril", "adamant", "starmetal"]),
    "livestock": (0.95, 0.36, ["meat", "cheese", "bacon", "eggs", "honey", "down", "antler"]),
    "crafted":   (0.72, 0.28, ["plank", "beam", "ingot_copper", "ingot_iron"]),
    "night":     (0.47, 0.32, ["firefly", "moondew"]),
}

# Семьи без лестницы: тон и насыщенность -> одна плашка на всех, ни одной насечки.
#
# У построек ступени нет: курятник не «выше» кухни, они просто разные. Дать им
# насечки значило бы соврать игроку про порядок, которого нет.
FLAT = {
    # Тёплый камень. Раньше тут стоял тан 0.075/0.42, а у древесины 0.06/0.44 —
    # при уравненной яркости это ОДИН цвет (разница 14 по сумме модулей RGB), и
    # плашка переставала называть семью. Постройка не ресурс, ей и незачем быть
    # сочной: обесцвеченный камень отпускает вперёд красные стены и серые крыши,
    # а древесина остаётся единственной тёплой линией. Зазор стережёт `check`.
    "building": (0.10, 0.13, [
        "well", "kitchen", "market", "silo", "coop", "cattleshed", "stable", "bigbarn",
        "apiary", "windmill", "sawmill", "smelter", "minehoist", "scarecrow", "fence",
        # Костёр продаётся и как постройка, и как декор — из одного и того же замысла.
        # Плашка обязана быть той, чья вкладка: во вкладке построек лиловая плашка декора
        # читалась как чужая вещь, случайно попавшая в ряд. Исходник тот же
        # (decor_yardfire), а тон берётся отсюда.
        "campfire",
    ]),
    # Эмоции — не товар и не ресурс: холодная синь держит их в стороне от всех
    # линий, чтобы пузырь над фермером не читался как значок чего-то покупаемого.
    # Совсем обесцветить нельзя: ровно серая плашка неотличима от того самого
    # запечённого фона (82,82,82), ради выведения которого этот скрипт и написан.
    # Тон почти сошёлся с рудой (0.58) — расходятся насыщенностью: там выцветший
    # сланец, тут сочная синь, зазор 52. Меньше 40 — и `check` заорёт.
    "emote": (0.55, 0.34, ["emote_food", "emote_drink"]),
}

# Иконка -> (линия, ресурс), чью ступень она берёт.
#
# Раньше эхо переносило ЦВЕТ плашки; теперь цвет и так один на линию, и переносить
# осталось ступень. Корова получает ступень сыра, свинья — бекона: у товара и у того,
# что с него идёт, одинаковый ряд насечек, и пара запоминается без единой строчки.
#
# Здесь стоял ещё "plant_corn" — иконка товара «Грядка кукурузы». Снят 06.08.2026: её
# исходник оказался тем же рендером кукурузы, что и у ресурса, только перекрашенным в
# бирюзу, — одна картинка на два товара (совпадение 0.995) плюс бирюзовый стебель на
# зелёной плашке. Все остальные грядки и так берут иконку своего ресурса
# (Shop_plant_wheat -> icon_wheat), корень был единственным исключением без причины.
# Теперь Shop_plant_corn смотрит на icon_corn, а icon_plant_corn нет ни в Icons, ни в
# icon-source. Захочешь вернуть отдельную иконку грядке — заводи РАЗНЫЕ картинки всем
# грядкам сразу, иначе исключение вернётся.
ECHO = {
    "cow":        ("livestock", "cheese"),
    "pig":        ("livestock", "bacon"),
    "sheep":      ("livestock", "meat"),
}

# Декор: пыльная слива. Список не пишется руками — он берётся с диска по decor_*.png,
# потому что декор это чистый контент: каждая новая безделушка иначе требовала бы
# правки этого файла, а контент не должен трогать системы.
#
# Тон приглушённый нарочно: сами вещи оранжево-охристые (бочки, доски, цветы),
# и слива работает дополнительным — вещь выступает вперёд, плашка держится позади.
DECOR = (0.86, 0.18)

# Значок привязки в углу плашки декора. Диаметр 38 при 128 — это 17 экранных
# пикселей: меньше не хватает, чтобы отличить амбар от колодца, больше — значок
# начинает спорить с самой вещью.
BADGE_R = 19
BADGE_CENTER = (SIZE - MARGIN - 8 - BADGE_R, PIP_Y - 4 - BADGE_R)
BADGE_INNER = 27


# --- цвет ---------------------------------------------------------------------

def _luma(rgb):
    """Относительная яркость sRGB. Именно она, а не L из HLS: при равном L синяя
    плашка вдвое темнее жёлтой, и «одна светлота на линию» вышла бы враньём."""
    def lin(c):
        c /= 255.0
        return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4
    r, g, b = (lin(c) for c in rgb)
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def plate_color(hue, sat):
    """Тон и насыщенность заданы, светлота подбирается под общую яркость.

    Двоичный поиск, а не формула: обратной функции у sRGB-яркости нет, а десяти
    шагов хватает на точность в один код цвета."""
    lo, hi = 0.0, 1.0
    for _ in range(24):
        mid = (lo + hi) / 2
        rgb = tuple(int(c * 255) for c in colorsys.hls_to_rgb(hue, mid, sat))
        if _luma(rgb) < TARGET_LUMA:
            lo = mid
        else:
            hi = mid
    return tuple(int(c * 255) for c in colorsys.hls_to_rgb(hue, (lo + hi) / 2, sat))


def shade(color, k):
    return tuple(max(0, min(255, int(c * k))) for c in color)


# --- части картинки -----------------------------------------------------------

def cut_backdrop(image):
    """
    Убрать фон заливкой от краёв, а не сравнением цвета по всей картинке.
    Разница существенная: у серебра и платины сама модель почти серая, и глобальный
    ключ выел бы из них дыры. Заливка снимает только то, что связано с краем.

    Исходник, у которого фон уже прозрачный (снятый нашим же рендером построек),
    проходит насквозь: вырезать в нём нечего, а заливка по чёрному фону как раз
    выела бы тёмные крыши.
    """
    if image.getchannel("A").getextrema()[0] < 250:
        return image

    rgb = image.convert("RGB")
    marker = (255, 0, 255)
    w, h = image.size

    for corner in [(0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1)]:
        ImageDraw.floodfill(rgb, corner, marker, thresh=12)

    alpha = Image.new("L", image.size, 255)
    pixels, mask = rgb.load(), alpha.load()
    for y in range(h):
        for x in range(w):
            if pixels[x, y] == marker:
                mask[x, y] = 0

    # Сглаживаем срез на полпикселя: без этого по краю остаётся лесенка.
    alpha = alpha.filter(ImageFilter.GaussianBlur(0.6))

    out = image.copy()
    out.putalpha(alpha)
    return out


def trim(glyph):
    """Обрезка по непрозрачным пикселям. Порог 16, а не 1: у среза после размытия
    остаётся кайма в единицы альфы, и по ней бокс уезжал бы на пару пикселей."""
    box = glyph.getchannel("A").point(lambda a: 255 if a > 16 else 0).getbbox()
    return glyph.crop(box) if box else glyph


def fit(glyph, box=BOX, center=BOX_CENTER, canvas=SIZE):
    """Вписать силуэт в общий бокс: обрезать по краям вещи и увеличить до бокса.

    Без обрезки величина предмета на плашке определялась тем, сколько пустоты
    оказалось в рендере, — валуны занимали треть плашки, дыня почти всю.
    """
    glyph = trim(glyph)
    w, h = glyph.size
    scale = box / max(w, h)

    # Оптическая поправка: при одинаковом боксе сплошное пятно тяжелее скелета.
    cover = (glyph.getchannel("A").point(lambda a: 255 if a > 16 else 0)
             .histogram()[255] / float(w * h))
    scale *= max(COVER_FLOOR, min(1.0, (COVER_MEDIAN / max(cover, 1e-6)) ** 0.5))

    size = (max(1, round(w * scale)), max(1, round(h * scale)))
    out = Image.new("RGBA", (canvas, canvas), (0, 0, 0, 0))
    out.alpha_composite(glyph.resize(size, Image.LANCZOS),
                        (center[0] - size[0] // 2, center[1] - size[1] // 2))
    return out


def plate(color):
    """Плашка без кольца.

    Белая обводка 3px была самой яркой кромкой в строке магазина — ярче названия
    товара. Вместо неё волосяная линия в 36 альфы: край плашки на светлом фоне
    контрольного листа виден, на тёмном дереве в глаза не лезет.
    """
    image = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)

    box = [MARGIN, MARGIN, SIZE - MARGIN - 1, SIZE - MARGIN - 1]
    draw.rounded_rectangle(box, RADIUS, fill=color + (255,))
    draw.rounded_rectangle(box, RADIUS, outline=(255, 255, 255, 36), width=1)
    return image


def pips(image, color, step):
    """Ряд насечек: сколько штук — такая ступень.

    Цвет берётся от плашки и втрое темнее её, а не задан числом: так контраст
    насечек одинаков на сливе, сланце и травяной зелени по построению, а не по
    везению с подбором.
    """
    if step <= 0:
        return

    draw = ImageDraw.Draw(image)
    width = step * PIP_W + (step - 1) * PIP_GAP
    x = (SIZE - width) // 2
    ink = shade(color, 0.30) + (235,)

    for _ in range(step):
        draw.rounded_rectangle([x, PIP_Y, x + PIP_W - 1, PIP_Y + PIP_H - 1], 2, fill=ink)
        x += PIP_W + PIP_GAP


def badge(image, color, anchor):
    """Значок постройки-хозяйки в углу плашки декора.

    Три фонаря, две бочки и три клумбы — это один префаб на несколько товаров
    (совпадают guid и fileID). Различает их только место, и место здесь показано
    тем же, чем оно задано в данных: значком постройки, к которой вещь привязана.
    Обод цветом плашки нужен, чтобы значок не слипся с самой вещью — та вписана
    в бокс и до угла достаёт.
    """
    if anchor is None or not anchor.exists():
        return

    cx, cy = BADGE_CENTER
    draw = ImageDraw.Draw(image)
    draw.ellipse([cx - BADGE_R - 3, cy - BADGE_R - 3, cx + BADGE_R + 3, cy + BADGE_R + 3],
                 fill=color + (255,))
    # Донце светлее плашки, а не темнее: на тёмном донце постройка со своим тёмным
    # контуром сливалась в пятно, и значок различал товары только цветом кляксы.
    draw.ellipse([cx - BADGE_R, cy - BADGE_R, cx + BADGE_R, cy + BADGE_R],
                 fill=shade(color, 1.34) + (255,))

    mark = fit(cut_backdrop(Image.open(anchor).convert("RGBA")),
               box=BADGE_INNER, center=(cx, cy), canvas=SIZE)
    image.alpha_composite(outline(mark, 3))
    image.alpha_composite(mark)


def outline(glyph, width=5):
    """Тонкий тёмный контур: светлая модель на светлой плашке иначе теряет силуэт."""
    ring = glyph.getchannel("A").filter(ImageFilter.MaxFilter(width))
    edge = Image.new("RGBA", glyph.size, (14, 16, 14, 255))
    edge.putalpha(ring)
    return edge


# --- сборка -------------------------------------------------------------------

def build(source, color, step=0, anchor=None):
    if not source.exists():
        return None

    raw = Image.open(source).convert("RGBA")
    if raw.size != (SIZE, SIZE):
        raw = raw.resize((SIZE, SIZE), Image.LANCZOS)

    glyph = fit(cut_backdrop(raw))

    result = plate(color)
    pips(result, color, step)
    result.alpha_composite(outline(glyph))
    result.alpha_composite(glyph)
    badge(result, color, anchor)
    return result


def anchors():
    """Товар декора -> исходник постройки, к которой он привязан.

    Читается с диска по тем же ассетам, что видит игра: правка привязки в редакторе
    доезжает до иконки сама, руками тут дописывать нечего.
    """
    ids = {}
    for meta in FARMING.glob("Building_*.asset.meta"):
        guid = re.search(r"guid: ([0-9a-f]{32})", meta.read_text(encoding="utf8"))
        name = re.search(r"^  _id: (.+)$", meta.with_suffix("").read_text(encoding="utf8"), re.M)
        if guid and name:
            ids[guid.group(1)] = name.group(1).strip()

    out = {}
    for asset in FARMING.glob("Improvement_*.asset"):
        text = asset.read_text(encoding="utf8")
        name = re.search(r"^  _id: (.+)$", text, re.M)
        link = re.search(r"^  _anchorBuilding: \{fileID: \d+, guid: ([0-9a-f]{32})", text, re.M)
        if name and link and link.group(1) in ids:
            out[name.group(1).strip()] = SOURCE / f"icon_{ids[link.group(1)]}.png"
    return out


def ladder():
    """Имя иконки -> (цвет плашки, ступень). Один список на всё."""
    out = {}
    for hue, sat, names in LINES.values():
        color = plate_color(hue, sat)
        for index, name in enumerate(names):
            out[name] = (color, index + 1)
    for hue, sat, names in FLAT.values():
        color = plate_color(hue, sat)
        for name in names:
            out[name] = (color, 0)
    for name, (line, member) in ECHO.items():
        hue, sat, names = LINES[line]
        out[name] = (plate_color(hue, sat), names.index(member) + 1)
    return out


def jobs():
    """Что во что превращать: (исходник, итоговое имя, цвет, ступень, привязка)."""
    for name, (color, step) in ladder().items():
        yield SOURCE / f"icon_{name}.png", f"icon_{name}.png", color, step, None

    # Декор снимает Unity (Farm → Онлайн → Отрисовать иконки декора); имя итоговой
    # иконки менять нельзя — на Decor_*.png уже ссылаются товары, и переименование
    # порвало бы ссылки по guid.
    color = plate_color(*DECOR)
    linked = anchors()
    for source in sorted(SOURCE.glob("decor_*.png")):
        name = source.stem[len("decor_"):]
        yield source, f"Decor_{name}.png", color, 0, linked.get(name)


def check():
    """Сколько семей — столько плашек, и ни одна пара не сливается.

    Плашка теперь единственное, что говорит «это руда, а не скот»: ступень ушла на
    насечки, светлота уравнена. Значит две близкие плашки — не косметика, а потеря
    смысла, и молчать о ней нельзя. Порог 40 по сумме модулей RGB взят с натуры:
    тан построек и бурый древесины разошлись на 14 и на экране читались одним
    цветом, разведённые — на 96.
    """
    plates = {name: plate_color(hue, sat) for name, (hue, sat, _) in LINES.items()}
    plates.update({name: plate_color(hue, sat) for name, (hue, sat, _) in FLAT.items()})
    plates["decor"] = plate_color(*DECOR)

    names = sorted(plates)
    close = []
    for i, a in enumerate(names):
        for b in names[i + 1:]:
            gap = sum(abs(x - y) for x, y in zip(plates[a], plates[b]))
            if gap < 40:
                close.append(f"{a}/{b} ({gap})")

    print("плашек:", len(plates), "| яркости:",
          " ".join(f"{n}={_luma(plates[n]):.3f}" for n in names))
    if close:
        print("ПЛАШКИ СЛИВАЮТСЯ:", ", ".join(close))


def main():
    made, skipped, mute = [], [], []
    for source, target, color, step, anchor in jobs():
        # Значок привязки берётся из снимка постройки. Нет снимка — значок молча не
        # рисуется, и два товара снова становятся одной картинкой. Про это надо
        # кричать: тихое «ничего не произошло» тут стоит ровно того же, что и
        # чёрный квадрат вместо иконки.
        if anchor is not None and not anchor.exists():
            mute.append(f"{target} ждёт {anchor.name}")

        image = build(source, color, step, anchor)
        if image is None:
            skipped.append(source.stem)
            continue
        image.save(ICONS / target)
        made.append(target)

    print("пересобрано:", len(made))
    if skipped:
        print("нет исходника:", ", ".join(skipped))
    if mute:
        print("БЕЗ ЗНАЧКА ПРИВЯЗКИ (запусти Farm → Онлайн → Отрисовать исходники построек):",
              ", ".join(mute))
    check()


main()
