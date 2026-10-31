#!/usr/bin/env python3
"""Download openly licensed meter photos from Wikimedia Commons and Openverse.

All search terms are Ukrainian. Images are saved directly into a folder and a
CSV manifest records their source and license. This script uses only Python's
standard library.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import html
import http.client
import json
import re
import ssl
import sys
import time
from datetime import datetime, timezone
from email.utils import parsedate_to_datetime
from html.parser import HTMLParser
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from threading import Lock
from typing import Any
from urllib.error import HTTPError, URLError
from urllib.parse import urlencode
from urllib.request import Request, urlopen


API_URL = "https://commons.wikimedia.org/w/api.php"
OPENVERSE_API_URL = "https://api.openverse.org/v1/images/"
USER_AGENT = "UkrainianMeterDatasetCollector/1.0 (open licensed image research)"
DEFAULT_QUERIES = (
    "лічильник води Україна",
    "лічильник води",
    "водомір",
    "показники лічильника води",
    "лічильник газу Україна",
    "лічильник газу",
    "газовий лічильник",
    "показники лічильника газу",
    "лічильник електроенергії Україна",
    "лічильник електроенергії",
    "електролічильник",
    "лічильник електрики",
    "показники електролічильника",
)
ALLOWED_FORMATS = {
    "image/jpeg": ".jpg",
    "image/png": ".png",
    "image/webp": ".webp",
}
MAX_IMAGE_BYTES = 30 * 1024 * 1024
RETRY_DELAYS_SECONDS = (2, 5, 8, 15)
MAX_HTTP_ATTEMPTS = len(RETRY_DELAYS_SECONDS) + 1
MAX_RETRY_AFTER_SECONDS = 600


def make_ssl_context() -> ssl.SSLContext:
    """Use certifi's current CA bundle when installed, otherwise OS/Python roots."""
    try:
        import certifi

        return ssl.create_default_context(cafile=certifi.where())
    except ImportError:
        return ssl.create_default_context()


SSL_CONTEXT = make_ssl_context()


class _TextOnly(HTMLParser):
    def __init__(self) -> None:
        super().__init__()
        self.parts: list[str] = []

    def handle_data(self, data: str) -> None:
        self.parts.append(data)


def plain_text(value: str) -> str:
    parser = _TextOnly()
    try:
        parser.feed(value)
        return " ".join(" ".join(parser.parts).split())
    except Exception:
        return " ".join(html.unescape(re.sub(r"<[^>]+>", " ", value)).split())


def allowed_license(name: str) -> bool:
    """Accept public-domain, CC0, and attribution/share-alike licenses."""
    normalized = " ".join(name.strip().split()).casefold()
    if normalized in {"cc0", "cc0 1.0", "cc0 1.0 universal", "public domain", "pdm", "by", "by-sa"}:
        return True
    if normalized.startswith(("public domain", "pd-", "pd ")):
        return True
    return normalized.startswith(("cc by ", "cc by-sa "))


def get_json(base_url: str, params: dict[str, str]) -> dict[str, Any]:
    url = f"{base_url}?{urlencode(params)}"
    request = Request(url, headers={"User-Agent": USER_AGENT})
    with open_with_backoff(request, timeout=30) as response:
        return json.loads(response.read().decode("utf-8"))


def open_with_backoff(request: Request, timeout: int):
    """Retry throttled requests, honoring Retry-After up to ten minutes."""
    for attempt in range(MAX_HTTP_ATTEMPTS):
        try:
            return urlopen(request, timeout=timeout, context=SSL_CONTEXT)
        except HTTPError as error:
            if error.code not in (429, 503) or attempt == MAX_HTTP_ATTEMPTS - 1:
                raise
            retry_after = error.headers.get("Retry-After", "")
            try:
                wait_seconds = max(1.0, float(retry_after))
            except ValueError:
                try:
                    retry_time = parsedate_to_datetime(retry_after)
                    if retry_time.tzinfo is None:
                        retry_time = retry_time.replace(tzinfo=timezone.utc)
                    wait_seconds = max(1.0, (retry_time - datetime.now(timezone.utc)).total_seconds())
                except (TypeError, ValueError, OverflowError):
                    wait_seconds = RETRY_DELAYS_SECONDS[attempt]
            wait_seconds = min(wait_seconds, MAX_RETRY_AFTER_SECONDS)
            print(
                f"  Сервер обмежив запити (HTTP {error.code}); "
                f"повтор через {wait_seconds:.0f} с, спроба {attempt + 2}/{MAX_HTTP_ATTEMPTS}.",
                file=sys.stderr,
            )
            time.sleep(wait_seconds)
    raise RuntimeError("неочікуване завершення повторних HTTP-запитів")


def commons_results(query: str, delay: float):
    continuation: dict[str, str] = {}
    while True:
        params = {
            "action": "query",
            "format": "json",
            "formatversion": "2",
            "generator": "search",
            "gsrsearch": query,
            "gsrnamespace": "6",
            "gsrlimit": "50",
            "prop": "imageinfo",
            "iiprop": "url|size|mime|extmetadata",
            "iiurlwidth": "1920",
            "uselang": "uk",
        }
        params.update(continuation)
        if continuation:
            time.sleep(delay)
        data = get_json(API_URL, params)
        for page in data.get("query", {}).get("pages", []):
            image_info = (page.get("imageinfo") or [{}])[0]
            metadata = image_info.get("extmetadata") or {}
            title = str(page.get("title", "meter"))
            source_page = str(image_info.get("descriptionurl") or "")
            if not source_page:
                source_page = f"https://commons.wikimedia.org/wiki/{title.replace(' ', '_')}"
            yield {
                "provider": "commons",
                "id": str(page.get("pageid", "image")),
                "title": title,
                "query": query,
                "source_page": source_page,
                "image_url": str(image_info.get("thumburl") or image_info.get("url") or ""),
                "license": metadata_value(metadata, "LicenseShortName"),
                "license_url": metadata_value(metadata, "LicenseUrl"),
                "creator": metadata_value(metadata, "Artist"),
                "attribution": metadata_value(metadata, "Attribution") or metadata_value(metadata, "Artist"),
                "width": int(image_info.get("width") or 0),
                "height": int(image_info.get("height") or 0),
                "download_width": int(image_info.get("thumbwidth") or image_info.get("width") or 0),
                "download_height": int(image_info.get("thumbheight") or image_info.get("height") or 0),
            }
        continuation = data.get("continue") or {}
        if not continuation:
            return


def openverse_results(query: str, delay: float):
    page = 1
    while True:
        if page > 1:
            time.sleep(delay)
        params = {
            "q": query,
            "page": str(page),
            "page_size": "20",
            "license": "cc0,by,by-sa,pdm",
            "extension": "jpg,png,webp",
        }
        data = get_json(OPENVERSE_API_URL, params)
        for item in data.get("results", []):
            license_name = str(item.get("license", "")).lower()
            if license_name not in {"cc0", "pdm", "by", "by-sa"}:
                continue
            yield {
                "provider": "openverse",
                "id": str(item.get("id", "image")),
                "title": str(item.get("title") or "meter_image"),
                "query": query,
                "source_page": str(item.get("foreign_landing_url") or item.get("detail_url") or ""),
                "image_url": str(item.get("url") or item.get("thumbnail") or ""),
                "license": license_name,
                "license_url": str(item.get("license_url") or ""),
                "creator": str(item.get("creator") or ""),
                "attribution": str(item.get("attribution") or item.get("creator") or ""),
                "width": int(item.get("width") or 0),
                "height": int(item.get("height") or 0),
                "download_width": int(item.get("width") or 0),
                "download_height": int(item.get("height") or 0),
            }
        if page >= int(data.get("page_count") or page):
            return
        page += 1


def metadata_value(metadata: dict[str, Any], key: str) -> str:
    item = metadata.get(key) or {}
    return plain_text(str(item.get("value", "")))


def download(url: str) -> tuple[bytes, str]:
    request = Request(url, headers={"User-Agent": USER_AGENT})
    with open_with_backoff(request, timeout=60) as response:
        mime = response.headers.get_content_type().lower()
        if mime not in ALLOWED_FORMATS:
            raise ValueError(f"непідтримуваний формат {mime}")
        content = response.read(MAX_IMAGE_BYTES + 1)
    if len(content) > MAX_IMAGE_BYTES:
        raise ValueError("файл завеликий (понад 30 МБ)")
    if not content:
        raise ValueError("порожній файл")
    return content, mime


def safe_stem(title: str) -> str:
    stem = Path(title.rsplit(":", 1)[-1]).stem
    stem = re.sub(r"[^\w.-]+", "_", stem, flags=re.UNICODE).strip("._")
    return (stem or "meter_image")[:70]


def load_known_hashes(manifest_path: Path) -> set[str]:
    if not manifest_path.exists():
        return set()
    with manifest_path.open("r", encoding="utf-8-sig", newline="") as file:
        return {row.get("sha256", "") for row in csv.DictReader(file) if row.get("sha256")}


def collect_from_source(
    provider: str,
    limit: int,
    delay: float,
    output_dir: Path,
    writer: csv.DictWriter,
    manifest_file,
    shared_lock: Lock,
    known_hashes: set[str],
    known_pages: set[str],
) -> dict[str, int]:
    result_iterator = commons_results if provider == "commons" else openverse_results
    downloaded = 0
    skipped = 0
    errors = 0

    for query in DEFAULT_QUERIES:
        if downloaded >= limit:
            break
        print(f"[{provider}] Пошук: {query}")
        try:
            for item in result_iterator(query, delay):
                if downloaded >= limit:
                    break
                source_page = item["source_page"]
                image_url = item["image_url"]
                width = item["width"]
                height = item["height"]
                if not source_page or not image_url or min(width, height) < 320:
                    skipped += 1
                    continue
                if not allowed_license(item["license"]):
                    skipped += 1
                    continue

                with shared_lock:
                    already_seen = source_page in known_pages
                if already_seen:
                    skipped += 1
                    continue

                try:
                    try:
                        content, actual_mime = download(image_url)
                    finally:
                        time.sleep(delay)
                except (HTTPError, URLError, TimeoutError, OSError, http.client.HTTPException, ValueError) as error:
                    print(f"  [{provider}] Пропуск завантаження: {error}", file=sys.stderr)
                    skipped += 1
                    errors += 1
                    continue

                digest = hashlib.sha256(content).hexdigest()
                with shared_lock:
                    if digest in known_hashes or source_page in known_pages:
                        skipped += 1
                        continue

                    filename = (
                        f"{provider}_{safe_stem(item['id'])}_"
                        f"{safe_stem(item['title'])}{ALLOWED_FORMATS[actual_mime]}"
                    )
                    image_path = output_dir / filename
                    if image_path.exists():
                        filename = (
                            f"{provider}_{safe_stem(item['id'])}_"
                            f"{digest[:10]}{ALLOWED_FORMATS[actual_mime]}"
                        )
                        image_path = output_dir / filename
                    image_path.write_bytes(content)
                    writer.writerow({
                        "provider": provider,
                        "filename": filename,
                        "query_uk": query,
                        "source_page": source_page,
                        "source_file": image_url,
                        "license": item["license"],
                        "license_url": item["license_url"],
                        "artist": item["creator"],
                        "attribution": item["attribution"],
                        "sha256": digest,
                        "width": item["download_width"] or width,
                        "height": item["download_height"] or height,
                        "downloaded_at_utc": datetime.now(timezone.utc).isoformat(),
                    })
                    manifest_file.flush()
                    known_pages.add(source_page)
                    known_hashes.add(digest)
                    downloaded += 1
                    print(f"  [{provider}] {downloaded}/{limit}: {filename}")
        except (HTTPError, URLError, TimeoutError, json.JSONDecodeError) as error:
            errors += 1
            print(f"  [{provider}] Помилка пошуку «{query}»: {error}", file=sys.stderr)

    return {"downloaded": downloaded, "skipped": skipped, "errors": errors}


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Паралельно завантажує відкрито ліцензовані фото з Commons та Openverse."
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=Path("meter_images"),
        help="Папка для фотографій (за замовчуванням: ./meter_images)",
    )
    parser.add_argument(
        "--limit-per-source",
        "--limit",
        dest="limit_per_source",
        type=int,
        default=1500,
        help="Ліміт нових зображень з кожного джерела (за замовчуванням: 1500 з кожного)",
    )
    parser.add_argument(
        "--delay",
        type=float,
        default=2.0,
        help="Пауза між завантаженнями на кожному джерелі в секундах (за замовчуванням: 2)",
    )
    args = parser.parse_args()
    if args.limit_per_source < 1:
        parser.error("--limit-per-source має бути більшим за нуль")
    if args.delay < 0:
        parser.error("--delay не може бути від'ємним")

    output_dir = args.output.expanduser().resolve()
    output_dir.mkdir(parents=True, exist_ok=True)
    manifest_path = output_dir / "manifest.csv"
    known_hashes = load_known_hashes(manifest_path)
    known_pages: set[str] = set()
    if manifest_path.exists():
        with manifest_path.open("r", encoding="utf-8-sig", newline="") as file:
            known_pages = {row.get("source_page", "") for row in csv.DictReader(file)}

    fieldnames = [
        "provider", "filename", "query_uk", "source_page", "source_file", "license",
        "license_url", "artist", "attribution", "sha256", "width", "height",
        "downloaded_at_utc",
    ]
    needs_header = not manifest_path.exists() or manifest_path.stat().st_size == 0
    if not needs_header:
        with manifest_path.open("r", encoding="utf-8-sig", newline="") as existing_manifest:
            existing_fields = next(csv.reader(existing_manifest), [])
        # Preserve compatibility with manifests created by earlier versions.
        if "provider" not in existing_fields:
            fieldnames.remove("provider")
    shared_lock = Lock()
    results: dict[str, dict[str, int]] = {}

    try:
        with manifest_path.open("a", encoding="utf-8-sig", newline="") as manifest_file:
            writer = csv.DictWriter(manifest_file, fieldnames=fieldnames, extrasaction="ignore")
            if needs_header:
                writer.writeheader()
                manifest_file.flush()

            with ThreadPoolExecutor(max_workers=2, thread_name_prefix="meter-source") as executor:
                futures = {
                    executor.submit(
                        collect_from_source,
                        provider,
                        args.limit_per_source,
                        args.delay,
                        output_dir,
                        writer,
                        manifest_file,
                        shared_lock,
                        known_hashes,
                        known_pages,
                    ): provider
                    for provider in ("commons", "openverse")
                }
                for future, provider in ((future, futures[future]) for future in futures):
                    try:
                        results[provider] = future.result()
                    except OSError as error:
                        print(f"[{provider}] Помилка запису: {error}", file=sys.stderr)
                        results[provider] = {"downloaded": 0, "skipped": 0, "errors": 1}
    except OSError as error:
        print(f"Не вдалося записати результати: {error}", file=sys.stderr)
        return 1

    total_downloaded = sum(result["downloaded"] for result in results.values())
    total_skipped = sum(result["skipped"] for result in results.values())
    total_errors = sum(result["errors"] for result in results.values())
    for provider in ("commons", "openverse"):
        result = results.get(provider, {"downloaded": 0, "skipped": 0, "errors": 0})
        print(
            f"{provider}: {result['downloaded']}/{args.limit_per_source} нових фото, "
            f"пропущено {result['skipped']}, помилок {result['errors']}."
        )
    print(f"Разом завантажено: {total_downloaded}; пропущено: {total_skipped}.")
    print(f"Папка з файлами: {output_dir}")
    print(f"Маніфест джерел і ліцензій: {manifest_path}")
    return 2 if total_errors else 0


if __name__ == "__main__":
    raise SystemExit(main())
