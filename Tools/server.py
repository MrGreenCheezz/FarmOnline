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
import http.server
import json
import os
import re
import secrets
import socket
import socketserver
import sqlite3
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
    "/api/register", "/api/login", "/api/friends/request", "/api/friends/accept",
    "/api/events/ack",
})

#: Схема из ONLINE.md. IF NOT EXISTS — миграций нет и до Ф4 не будет: база на этой
#: машине, при смене схемы проще перенести данные руками, чем содержать механизм.
SCHEMA = """
CREATE TABLE IF NOT EXISTS players(
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    name       TEXT NOT NULL COLLATE NOCASE UNIQUE,
    token_hash TEXT NOT NULL,
    created_at REAL NOT NULL,
    last_seen  REAL NOT NULL
);
CREATE INDEX IF NOT EXISTS players_token ON players(token_hash);
CREATE TABLE IF NOT EXISTS farms(
    player_id INTEGER PRIMARY KEY REFERENCES players(id),
    state     TEXT NOT NULL,
    saved_at  REAL NOT NULL,
    rev       INTEGER NOT NULL
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
        # Автокоммит: атомарность многошаговых операций даёт замок, а не транзакции,
        # и незакрытых транзакций, держащих файл, при таком режиме не бывает.
        self._conn.isolation_level = None
        with self._lock:
            self._conn.executescript(SCHEMA)

    # --- игроки ---

    def register(self, name, token_hash, now):
        """None — имя занято (сравнение без учёта регистра даёт COLLATE NOCASE)."""
        with self._lock:
            row = self._conn.execute(
                "SELECT id FROM players WHERE name = ?", (name,)).fetchone()
            if row is not None:
                return None
            cur = self._conn.execute(
                "INSERT INTO players(name, token_hash, created_at, last_seen)"
                " VALUES (?, ?, ?, ?)", (name, token_hash, now, now))
            return cur.lastrowid

    def auth(self, token_hash, now):
        """Игрок по хэшу токена; заодно отмечает last_seen — этим оно и живо."""
        with self._lock:
            row = self._conn.execute(
                "SELECT * FROM players WHERE token_hash = ?", (token_hash,)).fetchone()
            if row is not None:
                self._conn.execute(
                    "UPDATE players SET last_seen = ? WHERE id = ?", (now, row["id"]))
            return row

    def player_by_name(self, name):
        with self._lock:
            return self._conn.execute(
                "SELECT * FROM players WHERE name = ?", (name,)).fetchone()

    def player_by_id(self, player_id):
        with self._lock:
            return self._conn.execute(
                "SELECT * FROM players WHERE id = ?", (player_id,)).fetchone()

    # --- ферма ---

    def get_farm(self, player_id):
        with self._lock:
            return self._conn.execute(
                "SELECT * FROM farms WHERE player_id = ?", (player_id,)).fetchone()

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
        """Итоговый статус заявки: 'pending' или 'accepted'.

        Встречная pending-заявка превращается в дружбу обновлением существующей
        строки — обе стороны уже высказались, ждать нажатия «принять» нечего,
        а вторая строка на ту же пару только запутала бы выборки.
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
                return "accepted"
            if row["requester"] == me:
                return "pending"  # повторная своя заявка — не отказ, просто ждём
            self._conn.execute(
                "UPDATE friends SET status = 'accepted' WHERE requester = ? AND addressee = ?",
                (other, me))
            return "accepted"

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
            if path == "/api/friends/request":
                return self._api_friends_request()
            if path == "/api/friends/accept":
                return self._api_friends_accept()
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
        token = secrets.token_hex(24)
        now = time.time()
        player_id = self.db.register(name, _hash_token(token), now)
        if player_id is None:
            raise ApiError(409, "name_taken")
        return {"ok": True, "playerId": player_id, "name": name,
                "token": token, "serverNow": now}

    def _api_login(self):
        player = self._auth()
        return {"ok": True, "playerId": player["id"], "name": player["name"],
                "serverNow": time.time()}

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
            json.loads(state)  # результат не нужен: только защита от мусора в базе
        except (UnicodeDecodeError, ValueError):
            raise ApiError(400, "bad_json")

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
        ok, rev = self.db.put_farm(player["id"], state, expected, now)
        if not ok:
            # Текущий rev в ответе обязателен: по нему клиент понимает, насколько
            # он отстал, и громко останавливает автосейв («ферма открыта в другом окне»).
            raise ApiError(409, "rev_conflict", rev=rev)
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
        if not isinstance(requester, int):
            raise ApiError(400, "bad_request")
        if not self.db.friend_accept(player["id"], requester):
            raise ApiError(404, "no_request")
        return {"ok": True, "serverNow": time.time()}

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
        if not isinstance(to_player, int):
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
        if not isinstance(ids, list) or not all(isinstance(i, int) for i in ids):
            raise ApiError(400, "bad_request")
        self.db.ack_events(player["id"], ids)
        return {"ok": True, "serverNow": time.time()}

    # --- общее для обработчиков ---

    def _auth(self):
        """Игрок по X-Farm-Token; заодно обновляется last_seen (внутри Db.auth)."""
        token = self.headers.get("X-Farm-Token", "")
        if token:
            player = self.db.auth(_hash_token(token), time.time())
            if player is not None:
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
    args = parser.parse_args()

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

    host = LOOPBACK if args.local else ALL_INTERFACES

    try:
        httpd = ThreadedServer((host, args.port), FarmHandler)
    except OSError as e:
        print(f"Не удалось занять порт {args.port}: {e}")
        print("Похоже, сервер уже запущен. Останови его или возьми другой порт: --port 8001")
        return 1

    with httpd:
        url = f"http://localhost:{args.port}/"
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
