"""Статический сервер для веб-версии «Фермы».

Зачем свой, а не `python -m http.server`: у встроенного нет типов для .wasm и .data,
и браузер отказывается компилировать модуль потоком — игра грузится заметно дольше,
а иногда не грузится вовсе. Здесь это три строки таблицы типов. Плюс сборка сжата
brotli, и кто-то должен сказать браузеру `Content-Encoding` — см. ENCODINGS.

Запуск:
    python Tools/serve.py                 # виден в сети, http://localhost:8000
    python Tools/serve.py --local         # только на этой машине
    python Tools/serve.py --port 9000
    python Tools/serve.py --dir Build/Web

Остановка — Ctrl+C.
"""

import argparse
import http.server
import os
import socket
import socketserver
import sys
import webbrowser
from pathlib import Path

DEFAULT_DIR = "Build/Web"
DEFAULT_PORT = 8000

#: Слушаем все интерфейсы: сборку нужно показывать с других машин и телефонов, а не
#: только с этой. Имя, по которому придут, не проверяется — заголовок Host никого здесь
#: не интересует, поэтому годится любой домен, ведущий на эту машину.
#: Обратно в «только для себя» — ключ --local.
ALL_INTERFACES = "0.0.0.0"
LOOPBACK = "127.0.0.1"


#: Чем сжаты файлы сборки и что об этом сказать браузеру.
ENCODINGS = {".br": "br", ".gz": "gzip"}


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


class UnityHandler(http.server.SimpleHTTPRequestHandler):
    """Отдаёт файлы сборки с типами и кодировками, которые ждёт от них браузер."""

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
        super().__init__(*args, **kwargs)

    def accepts_encoding(self, encoding: str) -> bool:
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

        # Сборка меняется на каждой пересборке — кеш браузера тут только мешает смотреть свежее.
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


def local_addresses() -> list:
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


def main() -> int:
    parser = argparse.ArgumentParser(description="Простой сервер для веб-версии игры")
    parser.add_argument("--dir", default=DEFAULT_DIR, help="папка со сборкой")
    parser.add_argument("--port", type=int, default=DEFAULT_PORT, help="порт")
    parser.add_argument("--no-open", action="store_true", help="не открывать браузер")
    parser.add_argument("--local", action="store_true",
                        help="слушать только 127.0.0.1 — никто снаружи не достучится")
    args = parser.parse_args()

    root = Path(args.dir)
    if not root.is_absolute():
        root = Path(__file__).resolve().parent.parent / args.dir

    if not root.is_dir():
        print(f"Нет папки со сборкой: {root}")
        print("Собери веб-версию (в Unity: меню Farm → Собрать веб-версию) и запусти снова.")
        return 1

    if not (root / "index.html").exists():
        print(f"В {root} нет index.html — это не похоже на сборку WebGL.")
        return 1

    os.chdir(root)

    host = LOOPBACK if args.local else ALL_INTERFACES

    try:
        httpd = ThreadedServer((host, args.port), UnityHandler)
    except OSError as e:
        print(f"Не удалось занять порт {args.port}: {e}")
        print("Похоже, сервер уже запущен. Останови его или возьми другой порт: --port 8001")
        return 1

    with httpd:
        url = f"http://localhost:{args.port}/"
        print(f"Раздаю {root}")
        print(f"Открой {url}   (Ctrl+C — остановить)")

        if args.local:
            print("Только эта машина (--local).")
        else:
            print("Виден в сети. Кто дотянется до порта — тот и скачает сборку.")
            for address in local_addresses():
                print(f"  http://{address}:{args.port}/")
            print("  ...и по любому доменному имени, ведущему сюда: имя не проверяется.")
            print("Не пускает Windows? Правило брандмауэра для порта заводится отдельно.")

        if not args.no_open:
            webbrowser.open(url)

        try:
            httpd.serve_forever()
        except KeyboardInterrupt:
            print("\nОстановлено.")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
