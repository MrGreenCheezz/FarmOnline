"""Сервер онлайн-фермы: статика веб-сборки, REST API и SQLite — один процесс, один порт.

Почему всё в одном: API и статика на одном origin — вопроса CORS не существует, а
PlayerPrefs в WebGL привязаны к паре домен+порт, поэтому порт 8000 менять нельзя
(см. docs/ONLINE.md). Раздача статики повторяет Tools/serve.py дословно: у встроенного
http.server нет типов для .wasm и .data, а сжатую сборку кто-то должен объявить браузеру
через Content-Encoding. serve.py остаётся жить для «только статики», но файлы друг друга
не импортируют — сервер обязан подниматься сам по себе, без соседей по папке.

Контракт API — docs/ONLINE.md, раздел «Контракт API». Клиент пишется по тому же
документу, поэтому меняешь ответ — правь контракт в том же коммите.

Запуск:
    python Tools/server.py                 # виден в сети, http://localhost:8000
    python Tools/server.py --local         # только на этой машине
    python Tools/server.py --db Tools/test.db
    python Tools/server.py --dir Build/Web

Сборки может не быть — тогда сервер честно предупредит и поднимет только API:
это нормальный режим для отладки из редактора Unity. Остановка — Ctrl+C.
"""

import argparse
import hashlib
import hmac
import http.server
import json
import os
import re
import secrets
import socket
import socketserver
import sqlite3
import ssl
import sys
import threading
import time
import traceback
import urllib.parse
import webbrowser
from pathlib import Path

DEFAULT_DIR = "Build/Web"
DEFAULT_PORT = 8000
DEFAULT_DB = "Tools/farm.db"

#: Слушаем все интерфейсы: сборку нужно показывать с других машин и телефонов, а не
#: только с этой. Имя, по которому придут, не проверяется — заголовок Host никого здесь
#: не интересует, поэтому годится любой домен, ведущий на эту машину.
#: Обратно в «только для себя» — ключ --local.
ALL_INTERFACES = "0.0.0.0"
LOOPBACK = "127.0.0.1"

#: Чем сжаты файлы сборки и что об этом сказать браузеру.
ENCODINGS = {".br": "br", ".gz": "gzip"}

#: Ферма — непрозрачный документ: сервер его не разбирает и не валидирует (модель
#: доверия Ф1, см. ONLINE.md). Единственные ворота — потолок размера и синтаксическая
#: проверка JSON, чтобы мусор не долетал до чужого клиента при визите.
MAX_STATE_BYTES = 2 * 1024 * 1024

#: Остальные тела — имена и списки id, им и килобайта много. Потолок нужен не для
#: экономии, а чтобы опечатка клиента не превращалась в бесконечное чтение сокета.
MAX_BODY_BYTES = 64 * 1024

FARM_VISIT_RE = re.compile(r"/api/farm/(\d+)\Z")

#: Все известные пути API — чтобы отличать «нет такого пути» (404) от
#: «путь есть, метод не тот» (405): честный статус и здесь.
KNOWN_API_PATHS = frozenset({
    "/api/time", "/api/farm", "/api/friends", "/api/events",
    "/api/register", "/api/login", "/api/session", "/api/password", "/api/logout",
    "/api/friends/request", "/api/friends/accept", "/api/friends/remove",
    "/api/events/ack", "/api/daily",
})

#: Ежедневная награда. Сутки считаются по UTC-календарю, а не «24 часа с прошлого раза»:
#: скользящее окно наказывает за то, что вчера зашёл вечером, а сегодня утром, — игрок
#: начинает подгадывать время вместо того, чтобы просто заходить.
DAILY_BASE_GOLD = 120

#: Прибавка за каждый день подряд, до потолка. Растёт линейно и упирается быстро: смысл
#: серии — вернуть завтра, а не наказать того, кто пропустил неделю.
DAILY_STREAK_BONUS = 60
DAILY_MAX_STREAK = 7

#: Поле password — не пароль, а предварительный хеш sha256(имя_в_нижнем_регистре + ':' + пароль):
#: сам пароль по сети не ходит никогда (docs/ONLINE.md, «Пароли»). Ровно 64 hex в нижнем
#: регистре — что угодно другое пришло не от нашего клиента, и молчать об этом нельзя.
PASSWORD_RE = re.compile(r"[0-9a-f]{64}\Z")

#: PBKDF2 поверх присланного хеша. 600 000 итераций — примерно 0.3 с на попытку: столько
#: же стоит и подбор. Число и соль лежат в базе рядом с хешем, поэтому поднять стоимость
#: завтра можно, не ломая сегодняшние записи, — они пересчитаются при следующем входе.
PBKDF2_ITERATIONS = 600_000
PBKDF2_SALT_BYTES = 16

#: Вторая стена против подбора: PBKDF2 делает попытку дорогой, счётчик — редкой.
#: Ключ — имя в нижнем регистре, а не id: имени в базе может и не быть, а долбить
#: несуществующее имя ничем не лучше существующего.
LOGIN_MAX_FAILURES = 10
LOGIN_LOCK_SECONDS = 60

#: Схема из ONLINE.md. IF NOT EXISTS — миграций нет и до Ф4 не будет: база на этой
#: машине, при смене схемы проще перенести данные руками, чем содержать механизм.
#: Новые колонки к существующей базе доращиваются ALTER'ами в Db.__init__.
#:
#: players.token_hash — покойник эпохи гостевых токенов: токены переехали в sessions,
#: и никто эту колонку больше не читает. Оставлена намеренно: ALTER TABLE DROP COLUMN
#: старые sqlite не умеют, а ронять сервер ради удаления мёртвого поля незачем. На
#: старой базе она объявлена NOT NULL без умолчания — потому INSERT и пишет туда ''.
SCHEMA = """
CREATE TABLE IF NOT EXISTS players(
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    name          TEXT NOT NULL COLLATE NOCASE UNIQUE,
    token_hash    TEXT NOT NULL,
    pw_hash       TEXT,
    pw_salt       TEXT,
    pw_iterations INTEGER,
    created_at    REAL NOT NULL,
    last_seen     REAL NOT NULL,
    daily_day     TEXT,
    daily_streak  INTEGER NOT NULL DEFAULT 0
);
CREATE TABLE IF NOT EXISTS sessions(
    token_hash TEXT PRIMARY KEY,
    player_id  INTEGER NOT NULL REFERENCES players(id),
    created_at REAL NOT NULL,
    last_seen  REAL NOT NULL
);
CREATE INDEX IF NOT EXISTS sessions_player ON sessions(player_id);
CREATE TABLE IF NOT EXISTS farms(
    player_id  INTEGER PRIMARY KEY REFERENCES players(id),
    state      TEXT NOT NULL,
    saved_at   REAL NOT NULL,
    rev        INTEGER NOT NULL,
    suspicious INTEGER NOT NULL DEFAULT 0
);
CREATE TABLE IF NOT EXISTS friends(
    requester  INTEGER NOT NULL REFERENCES players(id),
    addressee  INTEGER NOT NULL REFERENCES players(id),
    status     TEXT NOT NULL CHECK (status IN ('pending', 'accepted')),
    created_at REAL NOT NULL,
    PRIMARY KEY (requester, addressee)
);
CREATE TABLE IF NOT EXISTS events(
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    to_player   INTEGER NOT NULL REFERENCES players(id),
    from_player INTEGER NOT NULL REFERENCES players(id),
    type        TEXT NOT NULL,
    payload     TEXT NOT NULL,
    created_at  REAL NOT NULL,
    consumed    INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS events_inbox ON events(to_player, consumed);
"""


#: Потолки здравого смысла для снимка фермы. Это не баланс, а граница мусора:
#: честный клиент до них не доберётся никогда, а сломанный или подделанный —
#: не должен доехать до базы.
MAX_GOLD = 1_000_000_000
MAX_PLOTS = 500
MAX_LEVEL = 30


def _reject_const(name):
    """NaN и Infinity в снимке — всегда порча: JsonUtility их не пишет."""
    raise ValueError("bad constant: " + name)


class Catalog:
    """Цены и тайминги из Tools/catalog.json — те же числа, что в ассетах клиента.

    Экспортируется из редактора (Farm → Онлайн → Экспортировать каталог для сервера).
    Без него сервер хранит снимки вслепую; с ним — умеет оценить, мог ли игрок честно
    заработать столько за столько. Оценка нарочно мягкая: она ПОМЕЧАЕТ (suspicious),
    а не отклоняет — подарки друзей и разовые распродажи дают всплески, и резать по
    эвристике живых игроков нельзя. Решение по помеченным — за человеком.
    """

    def __init__(self, path):
        self.prices = {}   # resourceId -> цена продажи за единицу
        self.rates = {}    # growableId -> золото/сек с грядки 1-го уровня
        self.loaded = False
        self.generated = "?"

        try:
            data = json.loads(Path(path).read_text(encoding="utf-8"))
        except (OSError, ValueError):
            return

        for row in data.get("resources", []):
            self.prices[row.get("id", "")] = row.get("sellPrice", 0)

        for row in data.get("growables", []):
            grow = row.get("growSeconds") or 0
            price = self.prices.get(row.get("resourceId", ""), 0)
            if grow > 0:
                self.rates[row.get("id", "")] = price * row.get("baseYield", 1) / grow

        self.generated = data.get("generatedAtUtc", "?")
        self.loaded = True

    def wealth(self, doc):
        """Богатство снимка: золото + склад по ценам продажи."""
        total = float(doc.get("Gold") or 0)
        storage = doc.get("Storage") or {}
        for entry in storage.get("Entries") or []:
            if not isinstance(entry, dict):
                continue
            amount = entry.get("Amount", 0)
            if isinstance(amount, (int, float)) and amount > 0:
                total += self.prices.get(entry.get("ResourceId", ""), 0) * amount
        return total

    def income_ceiling(self, doc):
        """Потолок честного дохода фермы, золота в секунду.

        Урожай удваивается с уровнем слияния — как в GrowableDefinition.YieldFor,
        и это главный множитель; ауры и рынок покрываются общим запасом снаружи.
        """
        rate = 0.0
        for plot in doc.get("Plots") or []:
            if not isinstance(plot, dict):
                continue
            level = plot.get("Level", 1)
            if not is_int(level):
                level = 1
            level = max(1, min(MAX_LEVEL, level))
            rate += self.rates.get(plot.get("GrowableId", ""), 0.0) * (2 ** (level - 1))
        return rate


def is_int(value):
    """Целое — и не булево.

    `isinstance(True, int)` в Python истинно: bool наследует int, и обычная проверка
    молча пропускает `{"playerId": true}`, превращая его в единицу. Для чисел, пришедших
    из чужого JSON, это разница между «отказано» и «событие ушло игроку №1».
    """
    return type(value) is int


def validate_state(doc):
    """Жёсткая проверка снимка. None — годен; иначе код отказа для 400."""
    if not isinstance(doc, dict):
        return "bad_state"

    gold = doc.get("Gold", 0)
    if not is_int(gold) or gold < 0 or gold > MAX_GOLD:
        return "bad_gold"

    plots = doc.get("Plots") or []
    if not isinstance(plots, list) or len(plots) > MAX_PLOTS:
        return "bad_plots"

    for plot in plots:
        if not isinstance(plot, dict):
            return "bad_plots"
        level = plot.get("Level", 1)
        if not is_int(level) or level < 1 or level > MAX_LEVEL:
            return "bad_plots"

    return None


class ApiError(Exception):
    """Отказ API, который поедет клиенту как {"ok":false,"error":"код"}.

    Исключение, а не возврат кода: почти каждый обработчик начинается с двух-трёх
    проверок, и ранний выход исключением держит счастливый путь ровным.
    """

    def __init__(self, status, code, **extra):
        super().__init__(code)
        self.status = status
        self.code = code
        self.extra = extra


class LoginGuard:
    """Счётчик неудачных входов — в памяти процесса, не в базе.

    В базе ему делать нечего: переживать перезапуск сервера этой защите не нужно
    (перезапуск и так реже, чем минута блокировки), а лишняя запись на каждую
    опечатку пароля — трата под глобальным замком БД. Свой замок нужен потому,
    что сервер многопоточный, а dict под одновременной правкой не обещает ничего.
    """

    def __init__(self):
        self._lock = threading.Lock()
        self._fails = {}  # имя в нижнем регистре -> (число неудач подряд, время последней)

    def check(self, key, now):
        """Бросает 429, пока имя в блокировке. Истёкшую блокировку заодно снимает."""
        with self._lock:
            count, last = self._fails.get(key, (0, 0.0))
            if count < LOGIN_MAX_FAILURES:
                return
            if now - last < LOGIN_LOCK_SECONDS:
                raise ApiError(429, "too_many_attempts")
            self._fails.pop(key, None)

    def failed(self, key, now):
        with self._lock:
            count, _ = self._fails.get(key, (0, 0.0))
            self._fails[key] = (count + 1, now)

    def passed(self, key):
        """Удачный вход стирает счётчик: серия оборвалась, значит это был хозяин."""
        with self._lock:
            self._fails.pop(key, None)


def _check_password_field(value):
    """Присланный предварительный хеш или 400 bad_password.

    Молчаливое «не подошло» тут было бы худшим отказом: клиент со сломанным
    хешированием получал бы неотличимое от неверного пароля, и искали бы годами.
    """
    if not isinstance(value, str) or PASSWORD_RE.fullmatch(value) is None:
        raise ApiError(400, "bad_password")
    return value


def _derive_password(password_hex, salt_hex, iterations):
    """PBKDF2 от присланного хеша. Зовётся ВНЕ замка БД — он держит весь сервер,
    а 600 тысяч итераций под ним заперли бы ферму всем на треть секунды за вход."""
    return hashlib.pbkdf2_hmac(
        "sha256", password_hex.encode("ascii"), bytes.fromhex(salt_hex), iterations).hex()


def _new_password_record(password_hex):
    """(хеш, соль, итерации) для записи в базу. Соль своя у каждого игрока —
    иначе одинаковые пароли видны по одинаковым хешам."""
    salt = secrets.token_bytes(PBKDF2_SALT_BYTES).hex()
    return _derive_password(password_hex, salt, PBKDF2_ITERATIONS), salt, PBKDF2_ITERATIONS


def _sql_lower(value):
    """lower() питона внутри sqlite: тот же, каким клиент солит хеш имени."""
    return value.lower() if isinstance(value, str) else value


def _password_matches(row, password_hex):
    """Сверка с записью игрока. False и для аккаунтов без пароля (гости старой эпохи).

    compare_digest, а не ==: сравнение по первому несовпавшему байту рассказывает
    время ответа, а хеш подбирается посимвольно.
    """
    if row is None:
        return False
    stored, salt = row["pw_hash"], row["pw_salt"]
    iterations = row["pw_iterations"]
    if not stored or not salt or not iterations:
        return False
    try:
        actual = _derive_password(password_hex, salt, int(iterations))
    except ValueError:
        # Соль в базе испорчена — войти нельзя, но и упасть 500-й нельзя.
        sys.stderr.write("ОШИБКА: испорченная соль у игрока %s\n" % row["id"])
        return False
    return hmac.compare_digest(actual, stored)


class Db:
    """Одно соединение на процесс и глобальный замок вокруг каждой операции.

    Сервер многопоточный, а соединение sqlite3 в несколько потоков не умеет —
    check_same_thread=False лишь снимает проверку, безопасность остаётся на нас.
    Замок грубый, на всю операцию целиком, и это сознательно: очередь на нём при
    нашей нагрузке (автосейвы раз в десятки секунд) не вырастет, а вот гонка
    «прочитал rev — записал ферму» без него превратила бы защиту от двух вкладок
    в лотерею.
    """

    def __init__(self, path):
        self._lock = threading.Lock()
        self._conn = sqlite3.connect(str(path), check_same_thread=False)
        self._conn.row_factory = sqlite3.Row
        # COLLATE NOCASE в sqlite складывает регистр только у латиницы: «Алиса» и
        # «алиса» для него разные имена. Игра русская, имя фермы — оно же логин, и
        # игрок, набравший своё имя с другой буквы, получил бы «неверный пароль»
        # навсегда. Поэтому все поиски по имени идут через питоновский lower().
        # Сканирование без индекса тут не жалко: игроков десятки, а не миллионы.
        self._conn.create_function("pylower", 1, _sql_lower, deterministic=True)
        # Автокоммит: атомарность многошаговых операций даёт замок, а не транзакции,
        # и незакрытых транзакций, держащих файл, при таком режиме не бывает.
        self._conn.isolation_level = None
        with self._lock:
            self._conn.executescript(SCHEMA)
            # База, заведённая до появления колонки: доращиваем на месте.
            # На свежей базе ALTER честно падает «уже есть» — это и есть успех.
            for ddl in (
                "ALTER TABLE farms ADD COLUMN suspicious INTEGER NOT NULL DEFAULT 0",
                "ALTER TABLE players ADD COLUMN pw_hash TEXT",
                "ALTER TABLE players ADD COLUMN pw_salt TEXT",
                "ALTER TABLE players ADD COLUMN pw_iterations INTEGER",
                "ALTER TABLE players ADD COLUMN daily_day TEXT",
                "ALTER TABLE players ADD COLUMN daily_streak INTEGER NOT NULL DEFAULT 0",
            ):
                try:
                    self._conn.execute(ddl)
                except sqlite3.OperationalError:
                    pass

    # --- игроки ---

    def passwordless_count(self):
        """Сколько аккаунтов осталось от эпохи гостевых токенов — им вход закрыт."""
        with self._lock:
            return self._conn.execute(
                "SELECT COUNT(*) AS n FROM players"
                " WHERE pw_hash IS NULL OR pw_hash = ''").fetchone()["n"]

    def create_player(self, name, pw_hash, pw_salt, iterations, now):
        """None — имя занято (без учёта регистра, кириллица тоже — см. pylower).

        Проверка и вставка под одним замком — иначе двое с одинаковым именем,
        нажавшие «войти» разом, оба прошли бы SELECT. UNIQUE в схеме тут не
        страховка: он умеет только латиницу.

        token_hash = '' — дань мёртвой колонке: на старой базе она NOT NULL без
        умолчания, а значение её никто не читает (см. комментарий к SCHEMA).
        """
        with self._lock:
            row = self._conn.execute(
                "SELECT id FROM players WHERE pylower(name) = pylower(?)", (name,)).fetchone()
            if row is not None:
                return None
            cur = self._conn.execute(
                "INSERT INTO players(name, token_hash, pw_hash, pw_salt, pw_iterations,"
                " created_at, last_seen) VALUES (?, '', ?, ?, ?, ?, ?)",
                (name, pw_hash, pw_salt, iterations, now, now))
            return cur.lastrowid

    def credentials(self, name):
        """Запись игрока для сверки пароля. Считать хеш — уже снаружи, без замка.

        ORDER BY id — на случай старой базы, где до pylower успели завестись
        «Алиса» и «алиса»: право на имя остаётся за тем, кто пришёл первым.
        """
        with self._lock:
            return self._conn.execute(
                "SELECT * FROM players WHERE pylower(name) = pylower(?)"
                " ORDER BY id LIMIT 1", (name,)).fetchone()

    def set_password(self, player_id, pw_hash, pw_salt, iterations):
        with self._lock:
            self._conn.execute(
                "UPDATE players SET pw_hash = ?, pw_salt = ?, pw_iterations = ?"
                " WHERE id = ?", (pw_hash, pw_salt, iterations, player_id))

    # --- сессии ---

    def open_session(self, player_id, token_hash, now):
        """Новая строка на устройство: вход с телефона не выкидывает из игры за столом."""
        with self._lock:
            self._conn.execute(
                "INSERT OR REPLACE INTO sessions(token_hash, player_id, created_at, last_seen)"
                " VALUES (?, ?, ?, ?)", (token_hash, player_id, now, now))

    def close_session(self, token_hash):
        with self._lock:
            self._conn.execute("DELETE FROM sessions WHERE token_hash = ?", (token_hash,))

    def close_all_sessions(self, player_id):
        """Смена пароля обязана выгонять того, кто его подсмотрел, — со всех устройств."""
        with self._lock:
            self._conn.execute("DELETE FROM sessions WHERE player_id = ?", (player_id,))

    def auth(self, token_hash, now):
        """Игрок по хэшу токена сессии; заодно отмечает last_seen — этим оно и живо."""
        with self._lock:
            row = self._conn.execute(
                "SELECT p.* FROM sessions s JOIN players p ON p.id = s.player_id"
                " WHERE s.token_hash = ?", (token_hash,)).fetchone()
            if row is not None:
                self._conn.execute(
                    "UPDATE sessions SET last_seen = ? WHERE token_hash = ?", (now, token_hash))
                self._conn.execute(
                    "UPDATE players SET last_seen = ? WHERE id = ?", (now, row["id"]))
            return row

    def player_by_name(self, name):
        """Поиск друга по имени — тем же регистронезависимым правилом, что и вход:
        игрок зовёт друга ровно тем именем, под которым тот входит."""
        with self._lock:
            return self._conn.execute(
                "SELECT * FROM players WHERE pylower(name) = pylower(?)"
                " ORDER BY id LIMIT 1", (name,)).fetchone()

    def player_by_id(self, player_id):
        with self._lock:
            return self._conn.execute(
                "SELECT * FROM players WHERE id = ?", (player_id,)).fetchone()

    # --- ферма ---

    def get_farm(self, player_id):
        with self._lock:
            return self._conn.execute(
                "SELECT * FROM farms WHERE player_id = ?", (player_id,)).fetchone()

    def mark_suspicious(self, player_id):
        """Пометить ферму: снимок разбогател быстрее теоретического потолка."""
        with self._lock:
            self._conn.execute(
                "UPDATE farms SET suspicious = suspicious + 1 WHERE player_id = ?",
                (player_id,))

    def claim_daily(self, player_id, today, yesterday):
        """Забрать ежедневную награду. Возвращает (взято_сейчас, длина_серии).

        Проверка и запись — под одним замком: две вкладки, нажавшие «забрать»
        одновременно, иначе получили бы награду дважды за один день.
        """
        with self._lock:
            row = self._conn.execute(
                "SELECT daily_day, daily_streak FROM players WHERE id = ?",
                (player_id,)).fetchone()
            if row is None:
                return False, 0

            last = row["daily_day"]
            streak = row["daily_streak"] or 0

            if last == today:
                return False, streak

            # Серия продолжается только со вчерашнего дня; пропуск начинает её заново.
            streak = streak + 1 if last == yesterday else 1
            self._conn.execute(
                "UPDATE players SET daily_day = ?, daily_streak = ? WHERE id = ?",
                (today, streak, player_id))
            return True, streak

    def put_farm(self, player_id, state, expected_rev, now):
        """(True, новый rev) или (False, текущий rev) при расхождении ревизий.

        Ревизия несуществующей фермы — 0: тогда «-1 — не проверять» и «жду 0»
        одинаково приводят к первой записи с rev = 1, без особого случая.
        """
        with self._lock:
            row = self._conn.execute(
                "SELECT rev FROM farms WHERE player_id = ?", (player_id,)).fetchone()
            current = row["rev"] if row is not None else 0
            if expected_rev is not None and expected_rev != current:
                return False, current
            self._conn.execute(
                "INSERT OR REPLACE INTO farms(player_id, state, saved_at, rev)"
                " VALUES (?, ?, ?, ?)", (player_id, state, now, current + 1))
            return True, current + 1

    # --- друзья ---

    def friendship(self, a, b):
        """Статус строки в любой из двух ориентаций: accepted с любой стороны = дружба."""
        with self._lock:
            row = self._conn.execute(
                "SELECT status FROM friends WHERE (requester = ? AND addressee = ?)"
                " OR (requester = ? AND addressee = ?)", (a, b, b, a)).fetchone()
            return row["status"] if row is not None else None

    def friend_request(self, me, other, now):
        """Итоговый статус: 'pending', 'incoming_exists' или 'already_friends'.

        Заявка НИКОГДА не создаёт дружбу сама. Раньше встречная заявка молча
        превращалась в accepted — «обе стороны высказались, чего ждать», — и это
        оказалось дефектом: игрок нажимал «добавить» на том, кто уже стучался, и
        получал друга, не увидев ни его имени, ни решения. Дружбу заводит ровно
        одно действие — accept; здесь мы только называем, что мешает.
        """
        with self._lock:
            row = self._conn.execute(
                "SELECT requester, status FROM friends WHERE (requester = ? AND addressee = ?)"
                " OR (requester = ? AND addressee = ?)", (me, other, other, me)).fetchone()
            if row is None:
                self._conn.execute(
                    "INSERT INTO friends(requester, addressee, status, created_at)"
                    " VALUES (?, ?, 'pending', ?)", (me, other, now))
                return "pending"
            if row["status"] == "accepted":
                return "already_friends"
            if row["requester"] == me:
                return "pending"  # повторная своя заявка — не отказ, просто ждём
            # Встречная заявка лежит и ждёт ответа — база не меняется вовсе, клиенту
            # остаётся показать её и предложить принять.
            return "incoming_exists"

    def friend_remove(self, me, other):
        """Рвёт ЛЮБУЮ связь и говорит какую: 'friend', 'incoming', 'outgoing'.

        None — связи не было. Один эндпоинт на три жеста (расторгнуть дружбу,
        отклонить чужую заявку, отозвать свою) потому, что строка в базе одна и та
        же; называть удалённое обязан ответ — иначе клиент не знает, что сказать игроку.
        """
        with self._lock:
            row = self._conn.execute(
                "SELECT requester, status FROM friends WHERE (requester = ? AND addressee = ?)"
                " OR (requester = ? AND addressee = ?)", (me, other, other, me)).fetchone()
            if row is None:
                return None
            self._conn.execute(
                "DELETE FROM friends WHERE (requester = ? AND addressee = ?)"
                " OR (requester = ? AND addressee = ?)", (me, other, other, me))
            if row["status"] == "accepted":
                return "friend"
            return "outgoing" if row["requester"] == me else "incoming"

    def friend_accept(self, me, requester):
        """False — принимать нечего (нет pending-заявки от этого игрока ко мне)."""
        with self._lock:
            cur = self._conn.execute(
                "UPDATE friends SET status = 'accepted'"
                " WHERE requester = ? AND addressee = ? AND status = 'pending'",
                (requester, me))
            return cur.rowcount > 0

    def friend_lists(self, me):
        """(друзья, входящие заявки, исходящие) — каждая строка это «другая сторона»."""
        with self._lock:
            friends = self._conn.execute(
                "SELECT p.id, p.name, p.last_seen FROM friends f JOIN players p"
                " ON p.id = CASE WHEN f.requester = :me THEN f.addressee ELSE f.requester END"
                " WHERE f.status = 'accepted' AND :me IN (f.requester, f.addressee)",
                {"me": me}).fetchall()
            incoming = self._conn.execute(
                "SELECT p.id, p.name, p.last_seen FROM friends f JOIN players p"
                " ON p.id = f.requester WHERE f.status = 'pending' AND f.addressee = ?",
                (me,)).fetchall()
            outgoing = self._conn.execute(
                "SELECT p.id, p.name, p.last_seen FROM friends f JOIN players p"
                " ON p.id = f.addressee WHERE f.status = 'pending' AND f.requester = ?",
                (me,)).fetchall()
            return friends, incoming, outgoing

    # --- события ---

    def add_event(self, to_player, from_player, event_type, payload, now):
        with self._lock:
            self._conn.execute(
                "INSERT INTO events(to_player, from_player, type, payload, created_at)"
                " VALUES (?, ?, ?, ?, ?)", (to_player, from_player, event_type, payload, now))

    def events_for(self, me):
        with self._lock:
            return self._conn.execute(
                "SELECT e.id, e.from_player, p.name AS from_name, e.type, e.payload,"
                " e.created_at FROM events e JOIN players p ON p.id = e.from_player"
                " WHERE e.to_player = ? AND e.consumed = 0 ORDER BY e.id", (me,)).fetchall()

    def ack_events(self, me, ids):
        """Гасит только свои события: чужой id в списке молча пропускается —
        это не отказ, а защита от подделки, ругаться тут не на что."""
        if not ids:
            return
        with self._lock:
            marks = ", ".join("?" for _ in ids)
            self._conn.execute(
                "UPDATE events SET consumed = 1 WHERE to_player = ? AND id IN (%s)" % marks,
                [me] + list(ids))


class ThreadedServer(socketserver.ThreadingMixIn, http.server.HTTPServer):
    """Каждое соединение — свой поток.

    Однопоточный `TCPServer` для этой задачи не годится, и не из-за скорости.
    Браузеры открывают соединение заранее, ещё не зная, что по нему запросят
    (preconnect). Однопоточный сервер принимает такое соединение и садится ждать
    строку запроса, которой не будет, — и перестаёт принимать все остальные.
    Снаружи это выглядит как намертво зависшая страница загрузки, а через минуту,
    когда переполнится очередь, — как «сервер не отвечает», хотя процесс жив и
    порт слушается. Один раз на этом уже потеряли вечер.
    """

    daemon_threads = True

    # SO_REUSEADDR только там, где он значит «переиспользовать TIME_WAIT». На Windows
    # он значит другое — «делить порт с уже слушающим», — и второй запуск не падает с
    # ошибкой, а молча встаёт рядом. Дальше соединения уходят то одному серверу, то
    # другому, и отладка превращается в гадание. Пусть лучше честно скажет «порт занят».
    allow_reuse_address = os.name != "nt"


class FarmHandler(http.server.SimpleHTTPRequestHandler):
    """Пути /api/* — JSON API, всё остальное — файлы сборки, как их ждёт браузер."""

    #: Ставятся в main() до старта сервера. static_root = None — режима статики нет,
    #: сервер работает «только API» (сборки на диске может не быть вовсе).
    static_root = None
    db = None
    catalog = None
    #: Счётчик неудачных входов общий на процесс — блокировка по имени, а не по соединению.
    guard = LoginGuard()

    extensions_map = {
        **http.server.SimpleHTTPRequestHandler.extensions_map,
        ".wasm": "application/wasm",
        ".data": "application/octet-stream",
        ".js": "text/javascript",
        ".symbols.json": "application/json",
    }

    #: Открытое, но молчащее соединение не должно держать поток вечно. Раздача идёт
    #: по петле, где 7 МБ уходят за доли секунды, так что минуты хватает с запасом.
    timeout = 60

    def __init__(self, *args, **kwargs):
        self._encoding = None
        self._session_hash = None
        directory = str(self.static_root) if self.static_root is not None else None
        super().__init__(*args, directory=directory, **kwargs)

    # --- маршрутизация ---

    def do_GET(self):
        path = urllib.parse.urlsplit(self.path).path
        if path == "/api" or path.startswith("/api/"):
            self._handle_api("GET", path)
        elif self.static_root is None:
            self.send_error(404, "Not Found",
                            "Сервер поднят без сборки — здесь отвечает только /api/*. "
                            "Собери веб-версию или укажи --dir.")
        else:
            super().do_GET()

    def do_HEAD(self):
        path = urllib.parse.urlsplit(self.path).path
        if path == "/api" or path.startswith("/api/"):
            self.send_error(405, "Method Not Allowed")
        elif self.static_root is None:
            self.send_error(404, "Not Found")
        else:
            super().do_HEAD()

    def do_POST(self):
        path = urllib.parse.urlsplit(self.path).path
        if path == "/api" or path.startswith("/api/"):
            self._handle_api("POST", path)
        else:
            self.send_error(404, "Not Found", "POST принимает только /api/*.")

    def do_PUT(self):
        path = urllib.parse.urlsplit(self.path).path
        if path == "/api" or path.startswith("/api/"):
            self._handle_api("PUT", path)
        else:
            self.send_error(404, "Not Found", "PUT принимает только /api/*.")

    def _handle_api(self, method, path):
        # _encoding — атрибут статической раздачи; JSON-ответу заголовок
        # Content-Encoding не положен, каким бы ни был прошлый запрос соединения.
        self._encoding = None
        try:
            self._send_json(200, self._route(method, path))
        except ApiError as err:
            body = {"ok": False, "error": err.code}
            body.update(err.extra)
            # Правило проекта: отказ обязан быть слышен. Штатный лог покажет лишь
            # HTTP-статус, а причину называет только эта строка.
            sys.stderr.write("%s api %s %s -> %d %s\n" % (
                self.address_string(), method, path, err.status, err.code))
            self._send_json(err.status, body)
        except Exception:
            # Молча съеденное исключение — худший из отказов: клиент видит вечную
            # крутилку, сервер — ничего. Полный traceback в stderr, клиенту — 500.
            traceback.print_exc(file=sys.stderr)
            self._send_json(500, {"ok": False, "error": "server_error"})

    def _route(self, method, path):
        if method == "GET":
            if path == "/api/time":
                return {"ok": True, "serverNow": time.time()}
            if path == "/api/farm":
                return self._api_farm_get()
            match = FARM_VISIT_RE.fullmatch(path)
            if match is not None:
                return self._api_farm_visit(int(match.group(1)))
            if path == "/api/friends":
                return self._api_friends()
            if path == "/api/events":
                return self._api_events_get()
        elif method == "POST":
            if path == "/api/register":
                return self._api_register()
            if path == "/api/login":
                return self._api_login()
            if path == "/api/session":
                return self._api_session()
            if path == "/api/password":
                return self._api_password()
            if path == "/api/logout":
                return self._api_logout()
            if path == "/api/daily":
                return self._api_daily()
            if path == "/api/friends/request":
                return self._api_friends_request()
            if path == "/api/friends/accept":
                return self._api_friends_accept()
            if path == "/api/friends/remove":
                return self._api_friends_remove()
            if path == "/api/events":
                return self._api_events_post()
            if path == "/api/events/ack":
                return self._api_events_ack()
        elif method == "PUT":
            if path == "/api/farm":
                return self._api_farm_put()

        # Путь знаком, но метод не тот — честный 405, а не «нет такого пути».
        if path in KNOWN_API_PATHS or FARM_VISIT_RE.fullmatch(path) is not None:
            raise ApiError(405, "method_not_allowed")
        raise ApiError(404, "not_found")

    # --- обработчики ---

    def _api_register(self):
        data = self._json_body()
        name = data.get("name")
        if not isinstance(name, str):
            raise ApiError(400, "bad_name")
        name = name.strip()
        # isprintable() отсекает управляющие символы: имя поедет в списки друзей
        # чужих клиентов, и \n или \x1b там — это уже не имя, а инъекция в чужой HUD.
        if not (2 <= len(name) <= 24) or not name.isprintable():
            raise ApiError(400, "bad_name")
        password = _check_password_field(data.get("password"))

        now = time.time()
        # Регистрация под тем же счётчиком, что и вход, и по той же причине наоборот:
        # каждая попытка стоит 0.3 с процессорного времени ДО всякой проверки прав, а
        # сервер раздаёт с этого же процесса саму игру. Без счётчика сотня запросов
        # на занятое имя укладывает раздачу сборки — отказ в регистрации дешевле.
        key = name.lower()
        self.guard.check(key, now)

        # Считаем ДО обращения к базе: замок БД и 600 тысяч итераций рядом стоять
        # не должны. Занятое имя обойдётся в лишние 0.3 с — плата за то, что
        # регистрация никого не задерживает.
        pw_hash, salt, iterations = _new_password_record(password)
        player_id = self.db.create_player(name, pw_hash, salt, iterations, now)
        if player_id is None:
            self.guard.failed(key, now)
            raise ApiError(409, "name_taken")

        self.guard.passed(key)
        return self._start_session(player_id, name, now)

    def _api_login(self):
        data = self._json_body()
        name = data.get("name")
        if not isinstance(name, str) or not name.strip():
            raise ApiError(400, "bad_name")
        name = name.strip()
        password = _check_password_field(data.get("password"))

        now = time.time()
        key = name.lower()
        self.guard.check(key, now)

        # Достать запись — под замком БД, посчитать PBKDF2 — уже без него: 600 тысяч
        # итераций под глобальным замком заперли бы весь сервер на каждый вход.
        # Несуществующее имя отвечает быстрее существующего, и это принято: то же
        # самое и так рассказывает /api/friends/request своим 404 no_such_player.
        row = self.db.credentials(name)
        if not _password_matches(row, password):
            self.guard.failed(key, now)
            # Имя и пароль в одном коде намеренно: «такого игрока нет» — подсказка
            # тому, кто перебирает имена. Пароли и хеши не логируем нигде.
            raise ApiError(401, "bad_credentials")

        self.guard.passed(key)
        return self._start_session(row["id"], row["name"], time.time())

    def _api_session(self):
        """Продление сеанса по токену — вход без пароля с уже знакомого устройства."""
        player = self._auth()
        return {"ok": True, "playerId": player["id"], "name": player["name"],
                "serverNow": time.time()}

    def _api_daily(self):
        """Ежедневная награда: раз в календарные сутки UTC, с серией за возвращения.

        Считает сервер, а не клиент: «день» на клиенте — это местные часы, которые
        игрок переводит, а суточный цикл самой фермы (240 секунд) к календарю
        отношения не имеет вовсе.

        Золото начисляет КЛИЕНТ по ответу — так же, как он начисляет всё остальное
        (модель доверия Ф1, docs/ONLINE.md): сервер здесь сторож календаря, а не
        бухгалтер. Повторно за день он не даст при любом числе нажатий.
        """
        player = self._auth()
        now = time.time()
        today = time.strftime("%Y-%m-%d", time.gmtime(now))
        yesterday = time.strftime("%Y-%m-%d", time.gmtime(now - 86400))

        claimed, streak = self.db.claim_daily(player["id"], today, yesterday)
        gold = DAILY_BASE_GOLD + DAILY_STREAK_BONUS * (min(streak, DAILY_MAX_STREAK) - 1) if claimed else 0

        return {"ok": True, "claimed": claimed, "gold": gold, "streak": streak,
                "serverNow": now}

    def _api_password(self):
        player = self._auth()
        data = self._json_body()
        current = _check_password_field(data.get("current"))
        following = _check_password_field(data.get("next"))

        # Сверяемся с записью, которую принёс токен, а не с повторным поиском по
        # имени: меняем пароль ровно тому аккаунту, чья это сессия.
        if not _password_matches(player, current):
            raise ApiError(401, "bad_credentials")

        pw_hash, salt, iterations = _new_password_record(following)
        self.db.set_password(player["id"], pw_hash, salt, iterations)
        # Гасим ВСЕ сессии, включая свою: смена пароля тем и ценна, что выгоняет
        # подсмотревшего. Взамен тут же выдаём новый токен — хозяин не заметит выхода.
        self.db.close_all_sessions(player["id"])
        answer = self._start_session(player["id"], player["name"], time.time())
        # Знание старого пароля доказано — держать на имени блокировку не за что.
        self.guard.passed(player["name"].lower())
        return answer

    def _api_logout(self):
        """Гасит только эту сессию: остальные устройства игрока продолжают играть."""
        self._auth()
        self.db.close_session(self._session_hash)
        return {"ok": True, "serverNow": time.time()}

    def _start_session(self, player_id, name, now):
        token = secrets.token_hex(24)
        self.db.open_session(player_id, _hash_token(token), now)
        return {"ok": True, "playerId": player_id, "name": name,
                "token": token, "serverNow": now}

    def _api_farm_get(self):
        player = self._auth()
        return self._farm_answer(self.db.get_farm(player["id"]))

    def _api_farm_visit(self, owner_id):
        player = self._auth()
        owner = self.db.player_by_id(owner_id)
        if owner is None:
            raise ApiError(404, "no_such_player")
        # Сам себе — тоже «визит»: клиенту не нужен особый путь для зеркала.
        if owner_id != player["id"] and self.db.friendship(player["id"], owner_id) != "accepted":
            raise ApiError(403, "not_friends")
        answer = self._farm_answer(self.db.get_farm(owner_id))
        answer["ownerName"] = owner["name"]
        return answer

    def _farm_answer(self, row):
        now = time.time()
        if row is None:
            # Отсутствие фермы — не отказ: у новичка её и не должно быть.
            return {"ok": True, "found": False, "serverNow": now}
        return {"ok": True, "found": True, "state": row["state"],
                "savedAt": row["saved_at"], "rev": row["rev"], "serverNow": now}

    def _api_farm_put(self):
        player = self._auth()
        raw = self._read_body(MAX_STATE_BYTES)
        try:
            state = raw.decode("utf-8")
            # parse_constant режет NaN/Infinity: JsonUtility их не пишет, а в базе
            # они дождались бы чужого клиента на визите.
            doc = json.loads(state, parse_constant=_reject_const)
        except (UnicodeDecodeError, ValueError):
            raise ApiError(400, "bad_json")

        # Жёсткая граница мусора: снимок за потолками не принимается вовсе.
        bad = validate_state(doc)
        if bad is not None:
            raise ApiError(400, bad)

        expected = None
        header = self.headers.get("X-Farm-Rev")
        if header is not None:
            try:
                expected = int(header)
            except ValueError:
                raise ApiError(400, "bad_rev")
            if expected == -1:
                expected = None  # «-1 — не проверять» из контракта

        now = time.time()

        # Мягкая экономическая проверка — ДО записи, пока прежний снимок жив.
        # Потолок дохода считается по СТАРОМУ составу фермы: заработать могли только
        # те грядки, что стояли в начале периода. Запас ×3 покрывает ауры и рынок,
        # добавка — стартовый капитал и мелкие подарки; крупный подарок даст ложное
        # срабатывание, и потому это пометка с логом, а не отказ.
        suspicion = None
        if self.catalog is not None and self.catalog.loaded:
            prev = self.db.get_farm(player["id"])
            if prev is not None:
                try:
                    prev_doc = json.loads(prev["state"])
                except ValueError:
                    prev_doc = None
                if isinstance(prev_doc, dict):
                    dt = max(0.0, now - prev["saved_at"])
                    allowance = self.catalog.income_ceiling(prev_doc) * dt * 3.0 + 500.0
                    gain = self.catalog.wealth(doc) - self.catalog.wealth(prev_doc)
                    if gain > allowance:
                        suspicion = (gain, allowance, dt)

        ok, rev = self.db.put_farm(player["id"], state, expected, now)
        if not ok:
            # Текущий rev в ответе обязателен: по нему клиент понимает, насколько
            # он отстал, и громко останавливает автосейв («ферма открыта в другом окне»).
            raise ApiError(409, "rev_conflict", rev=rev)

        if suspicion is not None:
            self.db.mark_suspicious(player["id"])
            sys.stderr.write(
                "ПОДОЗРЕНИЕ: игрок %d (%s) разбогател на %.0f при потолке %.0f за %.0f с\n"
                % (player["id"], player["name"], suspicion[0], suspicion[1], suspicion[2]))

        return {"ok": True, "rev": rev, "savedAt": now, "serverNow": now}

    def _api_friends_request(self):
        player = self._auth()
        data = self._json_body()
        name = data.get("name")
        if not isinstance(name, str) or not name.strip():
            raise ApiError(400, "bad_name")
        other = self.db.player_by_name(name.strip())
        if other is None:
            raise ApiError(404, "no_such_player")
        if other["id"] == player["id"]:
            raise ApiError(400, "self_request")
        status = self.db.friend_request(player["id"], other["id"], time.time())
        return {"ok": True, "status": status, "serverNow": time.time()}

    def _api_friends_accept(self):
        player = self._auth()
        data = self._json_body()
        requester = data.get("playerId")
        if not is_int(requester):
            raise ApiError(400, "bad_request")
        if not self.db.friend_accept(player["id"], requester):
            raise ApiError(404, "no_request")
        return {"ok": True, "serverNow": time.time()}

    def _api_friends_remove(self):
        player = self._auth()
        data = self._json_body()
        other = data.get("playerId")
        if not is_int(other):
            raise ApiError(400, "bad_request")
        removed = self.db.friend_remove(player["id"], other)
        if removed is None:
            # Отказ обязан быть заметным: «кнопка нажалась, и ничего» клиент
            # обязан отличить от «связь удалена».
            raise ApiError(404, "no_relation")
        return {"ok": True, "removed": removed, "serverNow": time.time()}

    def _api_friends(self):
        player = self._auth()
        friends, incoming, outgoing = self.db.friend_lists(player["id"])
        return {"ok": True,
                "friends": [_friend_json(row) for row in friends],
                "incoming": [_friend_json(row) for row in incoming],
                "outgoing": [_friend_json(row) for row in outgoing],
                "serverNow": time.time()}

    def _api_events_post(self):
        player = self._auth()
        data = self._json_body()
        to_player = data.get("toPlayerId")
        if not is_int(to_player):
            raise ApiError(400, "bad_request")
        if data.get("type") not in ("gift", "help", "help_reward"):
            raise ApiError(400, "bad_type")
        payload = data.get("payload")
        if not isinstance(payload, str):
            raise ApiError(400, "bad_payload")
        if self.db.player_by_id(to_player) is None:
            raise ApiError(404, "no_such_player")
        # События — только друзьям: подарок от незнакомца — это спам-канал.
        # Себе — можно: так гость в гостях откладывает собственную награду за помощь
        # (help_reward), которую его ферма заберёт при следующем входе домой.
        if to_player != player["id"] and self.db.friendship(player["id"], to_player) != "accepted":
            raise ApiError(403, "not_friends")
        self.db.add_event(to_player, player["id"], data["type"], payload, time.time())
        return {"ok": True, "serverNow": time.time()}

    def _api_events_get(self):
        player = self._auth()
        events = [{"id": row["id"], "fromPlayerId": row["from_player"],
                   "fromName": row["from_name"], "type": row["type"],
                   "payload": row["payload"], "createdAt": row["created_at"]}
                  for row in self.db.events_for(player["id"])]
        return {"ok": True, "events": events, "serverNow": time.time()}

    def _api_events_ack(self):
        player = self._auth()
        data = self._json_body()
        ids = data.get("ids")
        if not isinstance(ids, list) or not all(is_int(i) for i in ids):
            raise ApiError(400, "bad_request")
        self.db.ack_events(player["id"], ids)
        return {"ok": True, "serverNow": time.time()}

    # --- общее для обработчиков ---

    def _auth(self):
        """Игрок по X-Farm-Token; заодно обновляется last_seen (внутри Db.auth).

        Хеш токена запоминается в _session_hash: logout гасит именно эту строку
        сессий, и второй раз считать его неоткуда — сам токен дальше не нужен.
        """
        token = self.headers.get("X-Farm-Token", "")
        if token:
            token_hash = _hash_token(token)
            player = self.db.auth(token_hash, time.time())
            if player is not None:
                self._session_hash = token_hash
                return player
        raise ApiError(401, "bad_token")

    def _read_body(self, limit):
        try:
            length = int(self.headers.get("Content-Length", 0))
        except ValueError:
            raise ApiError(400, "bad_request")
        if length < 0:
            raise ApiError(400, "bad_request")
        if length > limit:
            # Тело читать не будем — а недочитанное соединение дальше не жилец.
            self.close_connection = True
            raise ApiError(413, "too_large")
        return self.rfile.read(length) if length else b""

    def _json_body(self):
        raw = self._read_body(MAX_BODY_BYTES)
        try:
            data = json.loads(raw.decode("utf-8"))
        except (UnicodeDecodeError, ValueError):
            raise ApiError(400, "bad_json")
        if not isinstance(data, dict):
            raise ApiError(400, "bad_json")
        return data

    def _send_json(self, status, obj):
        body = json.dumps(obj, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    # --- статика: дословно из serve.py ---

    def accepts_encoding(self, encoding):
        """Готов ли клиент принять такое сжатие.

        Ответ важен: если отдать brotli тому, кто его не просил, он получит не
        игру, а мусор. Firefox, например, соглашается на brotli только по https,
        и по http://localhost честно этого не просит.
        """
        for part in self.headers.get("Accept-Encoding", "").split(","):
            token, _, params = part.strip().partition(";")
            if token.strip().lower() not in (encoding, "*"):
                continue

            quality = 1.0
            for param in params.split(";"):
                key, _, value = param.partition("=")
                if key.strip().lower() == "q":
                    try:
                        quality = float(value)
                    except ValueError:
                        quality = 0.0
            return quality > 0

        return False

    def send_head(self):
        """Перед отдачей сжатого файла — проверить, что его есть кому распаковать.

        Иначе это ровно тот отказ, который ничего о себе не сообщает: браузер
        молча давится непонятными байтами, а на экране остаётся полоса загрузки.
        Лучше внятная ошибка в консоли и в ответе.
        """
        path = self.translate_path(self.path)
        for suffix, encoding in ENCODINGS.items():
            if path.endswith(suffix) and not self.accepts_encoding(encoding):
                self.send_error(
                    406,
                    "Not Acceptable",
                    f"Сборка сжата ({encoding}), а браузер прислал "
                    f"Accept-Encoding: {self.headers.get('Accept-Encoding', '(пусто)')} — "
                    "такого сжатия он не принимает. Обычно это brotli по обычному http: "
                    "браузеры просят br только по https (и по localhost). Лечится либо "
                    "сертификатом, либо пересборкой на gzip — WebBuild.ApplySettings, "
                    "compressionFormat.",
                )
                return None

        return super().send_head()

    def guess_type(self, path):
        """Тип берётся у имени под сжатием: Web.wasm.br — это wasm, а не «файл .br».

        Здесь же запоминаем кодировку: этот метод зовётся при сборке заголовков,
        до end_headers, и другого удобного места узнать её нет.
        """
        for suffix, encoding in ENCODINGS.items():
            if str(path).endswith(suffix):
                self._encoding = encoding
                return super().guess_type(str(path)[: -len(suffix)])

        self._encoding = None
        return super().guess_type(path)

    def end_headers(self):
        # Без этого заголовка браузер примет сжатый wasm за настоящий и упадёт
        # на разборе — с белым экраном и без внятной ошибки.
        if self._encoding:
            self.send_header("Content-Encoding", self._encoding)

        # Сборка меняется на каждой пересборке — кеш браузера тут только мешает
        # смотреть свежее. Ответам API кеш тем более противопоказан.
        self.send_header("Cache-Control", "no-store")
        super().end_headers()

    def log_message(self, fmt, *args):
        # Тише штатного: интересны ошибки, а не каждая картинка.
        status = args[1] if len(args) > 1 else ""
        if str(status).startswith(("4", "5")):
            sys.stderr.write("%s %s\n" % (self.address_string(), fmt % args))

    def log_error(self, fmt, *args):
        # А вот ошибки — всегда: молчащий отказ здесь и был бы главной бедой.
        sys.stderr.write("%s %s\n" % (self.address_string(), fmt % args))


def _hash_token(token):
    """В базе живёт только sha256: утёкший файл farm.db не должен раздавать доступ."""
    return hashlib.sha256(token.encode("utf-8")).hexdigest()


def _friend_json(row):
    return {"playerId": row["id"], "name": row["name"], "lastSeen": row["last_seen"]}


def local_addresses():
    """Адреса, по которым до нас достучатся из сети, — чтобы не искать их вручную."""
    found = set()

    try:
        # UDP-сокет ничего не отправляет: connect только выбирает исходящий маршрут,
        # а вместе с ним и адрес, которым нас увидят соседи по сети.
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as probe:
            probe.connect(("8.8.8.8", 80))
            found.add(probe.getsockname()[0])
    except OSError:
        pass

    try:
        found.update(socket.gethostbyname_ex(socket.gethostname())[2])
    except OSError:
        pass

    found.discard(LOOPBACK)
    return sorted(found)


def main():
    parser = argparse.ArgumentParser(
        description="Сервер онлайн-фермы: статика сборки + REST API + SQLite")
    parser.add_argument("--dir", default=DEFAULT_DIR, help="папка со сборкой")
    # 8000 — не привычка, а привязка: PlayerPrefs в WebGL живут на паре домен+порт,
    # смена порта обнуляет игрокам токены (см. ONLINE.md).
    parser.add_argument("--port", type=int, default=DEFAULT_PORT, help="порт")
    parser.add_argument("--db", default=DEFAULT_DB, help="файл базы SQLite")
    parser.add_argument("--no-open", action="store_true", help="не открывать браузер")
    parser.add_argument("--local", action="store_true",
                        help="слушать только 127.0.0.1 — никто снаружи не достучится")
    # TLS: оба ключа сразу — и сервер говорит по https. Сертификат добывает владелец
    # (win-acme/certbot), см. docs/ONLINE.md «Переезд на https».
    parser.add_argument("--cert", help="fullchain.pem — включает https (вместе с --key)")
    parser.add_argument("--key", help="privkey.pem к сертификату")
    args = parser.parse_args()

    if bool(args.cert) != bool(args.key):
        print("Для https нужны оба ключа сразу: --cert и --key.")
        return 1

    repo_root = Path(__file__).resolve().parent.parent

    root = Path(args.dir)
    if not root.is_absolute():
        root = repo_root / args.dir

    db_path = Path(args.db)
    if not db_path.is_absolute():
        db_path = repo_root / args.db
    db_path.parent.mkdir(parents=True, exist_ok=True)

    # Сборки может не быть — это не повод не поднимать API: редактору Unity для
    # отладки сети статика не нужна. Но предупреждение обязано быть громким, иначе
    # «белая страница» после билда будет искаться совсем не там.
    static_root = root if root.is_dir() else None
    if static_root is None:
        print(f"ВНИМАНИЕ: папки со сборкой нет: {root}")
        print("Поднимаю только API — для отладки из редактора этого достаточно.")
        print("Для игры в браузере собери веб-версию (Farm → Собрать веб-версию) "
              "и перезапусти сервер.")
    elif not (static_root / "index.html").exists():
        print(f"В {static_root} нет index.html — на сборку WebGL не похоже, но раздаю как есть.")

    FarmHandler.static_root = static_root
    FarmHandler.db = Db(db_path)

    # Аккаунты эпохи гостевых токенов паролем не откроешь: пароля у них нет и взяться
    # ему неоткуда (хеш нельзя восстановить из токена). Молчать об этом нельзя — иначе
    # владелец узнает о них от игрока, который не может войти.
    orphans = FarmHandler.db.passwordless_count()
    if orphans:
        print(f"ВНИМАНИЕ: в базе {orphans} аккаунт(ов) без пароля — из эпохи гостевых токенов.")
        print("Войти паролем они не смогут никогда. Если фермы не жалко, "
              f"проще завести базу заново: удали {db_path}.")

    # Каталог цен и таймингов — для экономической проверки снимков. Его отсутствие —
    # не ошибка, но сказано вслух: сервер-хранилище и сервер-сторож — разные режимы.
    FarmHandler.catalog = Catalog(repo_root / "Tools" / "catalog.json")
    if FarmHandler.catalog.loaded:
        print(f"Каталог загружен ({FarmHandler.catalog.generated}): "
              f"{len(FarmHandler.catalog.rates)} растимых — экономическая проверка включена.")
    else:
        print("ВНИМАНИЕ: Tools/catalog.json не найден — экономическая проверка выключена.")
        print("Экспортируй из редактора: Farm → Онлайн → Экспортировать каталог для сервера.")

    host = LOOPBACK if args.local else ALL_INTERFACES

    try:
        httpd = ThreadedServer((host, args.port), FarmHandler)
    except OSError as e:
        print(f"Не удалось занять порт {args.port}: {e}")
        print("Похоже, сервер уже запущен. Останови его или возьми другой порт: --port 8001")
        return 1

    scheme = "http"
    if args.cert:
        try:
            context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
            context.load_cert_chain(args.cert, args.key)
        except (OSError, ssl.SSLError) as e:
            print(f"Сертификат не загрузился: {e}")
            return 1
        httpd.socket = context.wrap_socket(httpd.socket, server_side=True)
        scheme = "https"

    with httpd:
        url = f"{scheme}://localhost:{args.port}/"
        if static_root is not None:
            print(f"Раздаю {static_root}")
        print(f"База: {db_path}")
        print(f"API: {url}api/time")
        print(f"Открой {url}   (Ctrl+C — остановить)")

        if args.local:
            print("Только эта машина (--local).")
        else:
            print("Виден в сети. Кто дотянется до порта — тот и скачает сборку.")
            for address in local_addresses():
                print(f"  http://{address}:{args.port}/")
            print("  ...и по любому доменному имени, ведущему сюда: имя не проверяется.")
            print("Не пускает Windows? Правило брандмауэра для порта заводится отдельно.")

        # Без сборки браузеру показывать нечего — открылась бы страница 404.
        if not args.no_open and static_root is not None:
            webbrowser.open(url)

        try:
            httpd.serve_forever()
        except KeyboardInterrupt:
            print("\nОстановлено.")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
