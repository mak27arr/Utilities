"""Search Google/Bing Images in a visible browser and download results in order.

The browser UI is used to discover image URLs. The script downloads each
candidate sequentially, deduplicates exact files, and records its source URL.
Search-engine license filters are only hints; each image's source page should
be checked before using it in a published or commercial dataset.
"""

from __future__ import annotations

import argparse
import asyncio
import csv
import hashlib
import json
import re
import sys
from datetime import datetime, timezone
from pathlib import Path
from urllib.parse import urlencode

from playwright.async_api import async_playwright


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
MIME_EXTENSIONS = {
    "image/jpeg": ".jpg",
    "image/png": ".png",
    "image/webp": ".webp",
}
RETRY_DELAYS = (2, 5, 8, 15)
MAX_RETRY_AFTER_SECONDS = 600
MAX_IMAGE_BYTES = 30 * 1024 * 1024
CHALLENGE_CHECK_INTERVAL_MS = 3000


async def wait_for_manual_challenge(page, timeout_seconds: float) -> None:
    """Keep the visible result page open while the user completes a CAPTCHA."""
    # Give a slow search page time to finish rendering before checking for a challenge.
    await page.wait_for_timeout(8000)
    challenge_detected = await page.evaluate(
        """() => {
            const text = (document.body?.innerText || '').toLowerCase();
            return location.pathname.includes('/sorry/') ||
                text.includes('unusual traffic') ||
                text.includes('verify you are human') ||
                text.includes('not a robot') ||
                Boolean(document.querySelector(
                    'form#captcha-form, form[action*="/sorry/"], iframe[src*="recaptcha"]'
                ));
        }"""
    )
    if not challenge_detected:
        return

    print(
        f"  Виявлена CAPTCHA. Пройди її у відкритому браузері; "
        f"скрипт чекатиме до {timeout_seconds:g} с без перезавантаження сторінки."
    )
    deadline = asyncio.get_running_loop().time() + timeout_seconds
    while asyncio.get_running_loop().time() < deadline:
        await page.wait_for_timeout(CHALLENGE_CHECK_INTERVAL_MS)
        challenge_detected = await page.evaluate(
            """() => {
                const text = (document.body?.innerText || '').toLowerCase();
                return location.pathname.includes('/sorry/') ||
                    text.includes('unusual traffic') ||
                    text.includes('verify you are human') ||
                    text.includes('not a robot') ||
                    Boolean(document.querySelector(
                        'form#captcha-form, form[action*="/sorry/"], iframe[src*="recaptcha"]'
                    ));
            }"""
        )
        if not challenge_detected:
            print("  Перевірку пройдено; продовжую пошук.")
            return
    raise TimeoutError("час очікування CAPTCHA вичерпано; запит пропущено")


def safe_name(value: str) -> str:
    value = re.sub(r"[^\w.-]+", "_", value, flags=re.UNICODE).strip("._")
    return (value or "image")[:80]


def load_manifest(path: Path) -> tuple[set[str], set[str]]:
    hashes: set[str] = set()
    urls: set[str] = set()
    if path.exists():
        with path.open("r", encoding="utf-8-sig", newline="") as file:
            for row in csv.DictReader(file):
                if row.get("sha256"):
                    hashes.add(row["sha256"])
                if row.get("image_url"):
                    urls.add(row["image_url"])
    return hashes, urls


async def extract_results(page, engine: str) -> list[dict[str, str]]:
    return await page.evaluate(
        """(engine) => {
            const found = [];
            const seen = new Set();
            const add = (imageUrl, fallbackUrl, sourcePage, title) => {
                if (!imageUrl || !/^https?:/i.test(imageUrl)) return;
                if (seen.has(imageUrl)) {
                    const existing = found.find(item => item.image_url === imageUrl);
                    if (existing && !existing.fallback_url && fallbackUrl && fallbackUrl !== imageUrl) {
                        existing.fallback_url = fallbackUrl;
                    }
                    return;
                }
                seen.add(imageUrl);
                found.push({
                    image_url: imageUrl,
                    fallback_url: fallbackUrl || '',
                    source_page: sourcePage || '',
                    title: title || ''
                });
            };

            if (engine === 'bing') {
                for (const tile of document.querySelectorAll('a.iusc[m], .iusc[m]')) {
                    try {
                        const meta = JSON.parse(tile.getAttribute('m') || '{}');
                        add(meta.murl, meta.turl, meta.purl, meta.t || meta.desc);
                    } catch (_) {}
                }
            } else {
                // Prefer Google's original image URL from the result link.
                for (const anchor of document.querySelectorAll('a[href*="imgurl="]')) {
                    try {
                        const link = new URL(anchor.href, location.href);
                        const imageUrl = link.searchParams.get('imgurl');
                        const thumbnail = anchor.querySelector('img')?.currentSrc || '';
                        add(
                            imageUrl,
                            thumbnail,
                            link.searchParams.get('imgrefurl') || '',
                            anchor.querySelector('img')?.alt || anchor.getAttribute('aria-label') || ''
                        );
                    } catch (_) {}
                }
            }

            // Google changes its result-card markup regularly. For cards without
            // an exposed original URL, keep the loaded result thumbnail as fallback.
            if (engine === 'google') {
                for (const img of document.querySelectorAll('img')) {
                    const url = img.currentSrc || img.src || img.getAttribute('data-src') || '';
                    if (!/^https?:/i.test(url) || img.naturalWidth < 70 || img.naturalHeight < 40) continue;
                    const anchor = img.closest('a');
                    let sourcePage = '';
                    let originalUrl = '';
                    if (anchor?.href) {
                        try {
                            const link = new URL(anchor.href, location.href);
                            originalUrl = link.searchParams.get('imgurl') || '';
                            sourcePage = link.searchParams.get('imgrefurl') ||
                                (link.hostname.includes('google.') ? '' : link.href);
                        } catch (_) {}
                    }
                    add(
                        originalUrl || url,
                        originalUrl ? url : '',
                        sourcePage,
                        img.alt || anchor?.getAttribute('aria-label') || ''
                    );
                }
            }
            return found;
        }""",
        engine,
    )


async def google_results(page, query: str, captcha_timeout: float) -> list[dict[str, str]]:
    # Do not apply Google's usage-rights filter: it can shrink the result set
    # to only a handful of images, even when many relevant images exist.
    params = urlencode({"tbm": "isch", "q": query, "hl": "uk", "gl": "ua"})
    await page.goto(f"https://www.google.com/search?{params}", wait_until="domcontentloaded", timeout=60000)
    await wait_for_manual_challenge(page, captcha_timeout)
    return await extract_results(page, "google")


async def bing_results(page, query: str, captcha_timeout: float) -> list[dict[str, str]]:
    params = urlencode({
        "q": query,
        "setlang": "uk",
        "cc": "UA",
    })
    await page.goto(f"https://www.bing.com/images/search?{params}", wait_until="domcontentloaded", timeout=60000)
    await wait_for_manual_challenge(page, captcha_timeout)
    return await extract_results(page, "bing")


async def collect_page_results(
    page, engine: str, query: str, max_scrolls: int, captcha_timeout: float
) -> list[dict[str, str]]:
    search = google_results if engine == "google" else bing_results
    all_results: list[dict[str, str]] = []
    seen_urls: set[str] = set()
    unchanged_scrolls = 0

    for scroll_index in range(max_scrolls + 1):
        current = (
            await search(page, query, captcha_timeout)
            if scroll_index == 0
            else await extract_results(page, engine)
        )
        before = len(seen_urls)
        for result in current:
            image_url = result.get("image_url", "")
            if image_url.startswith(("http://", "https://")) and image_url not in seen_urls:
                seen_urls.add(image_url)
                all_results.append(result)
        unchanged_scrolls = unchanged_scrolls + 1 if len(seen_urls) == before else 0
        if unchanged_scrolls >= 3:
            break
        await page.evaluate("window.scrollTo(0, document.body.scrollHeight)")
        await page.wait_for_timeout(1200)

    return all_results


def retry_wait_seconds(retry_after: str, attempt: int) -> float:
    try:
        return min(MAX_RETRY_AFTER_SECONDS, max(1.0, float(retry_after)))
    except (TypeError, ValueError):
        return float(RETRY_DELAYS[attempt])


async def fetch_image(context, image_url: str, referer: str) -> tuple[bytes, str]:
    for attempt in range(len(RETRY_DELAYS) + 1):
        response = await context.request.get(
            image_url,
            headers={"Referer": referer},
            timeout=60000,
        )
        if response.status in (429, 503) and attempt < len(RETRY_DELAYS):
            wait_seconds = retry_wait_seconds(response.headers.get("retry-after", ""), attempt)
            print(f"  HTTP {response.status}; повтор через {wait_seconds:.0f} с.", file=sys.stderr)
            await asyncio.sleep(wait_seconds)
            continue
        if not response.ok:
            raise RuntimeError(f"HTTP {response.status}")
        mime = response.headers.get("content-type", "").split(";", 1)[0].strip().lower()
        if mime not in MIME_EXTENSIONS:
            raise ValueError(f"непідтримуваний формат {mime or 'невідомий'}")
        content_length = int(response.headers.get("content-length", "0") or 0)
        if content_length > MAX_IMAGE_BYTES:
            raise ValueError("файл завеликий (понад 30 МБ)")
        content = await response.body()
        if len(content) > MAX_IMAGE_BYTES:
            raise ValueError("файл завеликий (понад 30 МБ)")
        if not content:
            raise ValueError("порожній файл")
        return content, mime
    raise RuntimeError("не вдалося завантажити після повторних спроб")


async def run(args) -> int:
    output_dir = args.output.expanduser().resolve()
    output_dir.mkdir(parents=True, exist_ok=True)
    manifest_path = output_dir / "manifest_browser.csv"
    known_hashes, known_urls = load_manifest(manifest_path)
    fieldnames = [
        "engine", "query_uk", "title", "filename", "image_url", "source_page",
        "license_filter", "sha256", "downloaded_at_utc",
    ]
    needs_header = not manifest_path.exists() or manifest_path.stat().st_size == 0
    downloaded = 0
    skipped = 0

    async with async_playwright() as playwright:
        browser_type = playwright.chromium
        channel = None if args.browser == "chromium" else args.browser
        browser = await browser_type.launch(headless=False, channel=channel)
        context = await browser.new_context(accept_downloads=True, locale="uk-UA")
        page = await context.new_page()

        try:
            with manifest_path.open("a", encoding="utf-8-sig", newline="") as manifest_file:
                writer = csv.DictWriter(manifest_file, fieldnames=fieldnames)
                if needs_header:
                    writer.writeheader()
                    manifest_file.flush()

                for query in DEFAULT_QUERIES:
                    if downloaded >= args.limit:
                        break
                    print(f"[{args.engine}] Пошук: {query}")
                    try:
                        results = await collect_page_results(
                            page, args.engine, query, args.max_scrolls, args.captcha_timeout
                        )
                    except Exception as error:
                        print(f"  Не вдалося прочитати результати: {error}", file=sys.stderr)
                        continue
                    print(f"  Знайдено карток: {len(results)}")

                    query_downloaded = 0
                    for result in results:
                        if downloaded >= args.limit or query_downloaded >= args.per_query_limit:
                            break
                        image_url = result["image_url"]
                        if image_url in known_urls:
                            skipped += 1
                            continue
                        content = None
                        mime = ""
                        downloaded_url = ""
                        errors = []
                        candidates = list(dict.fromkeys(
                            url for url in (image_url, result.get("fallback_url", "")) if url
                        ))
                        for candidate_url in candidates:
                            try:
                                try:
                                    content, mime = await fetch_image(context, candidate_url, page.url)
                                    downloaded_url = candidate_url
                                    break
                                finally:
                                    await asyncio.sleep(args.delay)
                            except Exception as error:
                                errors.append(f"{candidate_url}: {error}")
                        if content is None:
                            print(f"  Пропуск зображення; помилки: {'; '.join(errors)}", file=sys.stderr)
                            skipped += 1
                            continue

                        digest = hashlib.sha256(content).hexdigest()
                        if digest in known_hashes:
                            known_urls.add(image_url)
                            skipped += 1
                            continue

                        filename = f"{args.engine}_{digest[:16]}{MIME_EXTENSIONS[mime]}"
                        image_path = output_dir / filename
                        if image_path.exists():
                            known_hashes.add(digest)
                            known_urls.add(image_url)
                            skipped += 1
                            continue
                        image_path.write_bytes(content)
                        writer.writerow({
                            "engine": args.engine,
                            "query_uk": query,
                            "title": result.get("title", ""),
                            "filename": filename,
                            "image_url": downloaded_url,
                            "source_page": result.get("source_page", ""),
                            "license_filter": "No search license filter; source license unverified",
                            "sha256": digest,
                            "downloaded_at_utc": datetime.now(timezone.utc).isoformat(),
                        })
                        manifest_file.flush()
                        known_hashes.add(digest)
                        known_urls.add(image_url)
                        known_urls.add(downloaded_url)
                        downloaded += 1
                        query_downloaded += 1
                        print(f"  {downloaded}/{args.limit}: {filename}")

            print(f"Завантажено: {downloaded}; пропущено: {skipped}.")
            print(f"Папка з файлами: {output_dir}")
            print(f"Маніфест: {manifest_path}")
            return 0
        finally:
            await context.close()
            await browser.close()


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Послідовно завантажує зображення з Google Images або Bing Images через видимий браузер."
    )
    parser.add_argument("--engine", choices=("google", "bing"), default="google")
    parser.add_argument("--browser", choices=("msedge", "chrome", "chromium"), default="msedge")
    parser.add_argument("--output", type=Path, default=Path("data/meter_images_browser"))
    parser.add_argument("--limit", type=int, default=1000, help="Загальний ліміт нових зображень")
    parser.add_argument("--per-query-limit", type=int, default=100)
    parser.add_argument("--max-scrolls", type=int, default=25)
    parser.add_argument("--delay", type=float, default=2.0)
    parser.add_argument(
        "--captcha-timeout",
        type=float,
        default=600.0,
        help="максимальний час очікування ручного проходження CAPTCHA (секунди; за замовчуванням 600)",
    )
    args = parser.parse_args()
    if args.limit < 1 or args.per_query_limit < 1 or args.max_scrolls < 1:
        parser.error("ліміти та кількість прокручувань мають бути більшими за нуль")
    if args.delay < 0 or args.captcha_timeout < 0:
        parser.error("--delay і --captcha-timeout не можуть бути від'ємними")
    try:
        return asyncio.run(run(args))
    except Exception as error:
        print(f"Помилка браузерного збору: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
