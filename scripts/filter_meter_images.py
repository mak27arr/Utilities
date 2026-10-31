"""Classify images under a data folder with local zero-shot CLIP.

Source images are never modified or deleted. The script copies each readable
image into keep/, review/, or reject/ and writes a CSV with scores and paths.
"""

from __future__ import annotations

import argparse
import csv
import shutil
import sys
from pathlib import Path


MODEL_ID = "openai/clip-vit-base-patch32"
IMAGE_EXTENSIONS = {".jpg", ".jpeg", ".png", ".webp", ".bmp", ".tif", ".tiff", ".gif"}

# The original OpenAI CLIP checkpoint is documented for English prompts.
PROMPTS = (
    ("water_meter", True, "a real water meter, photographed in a home or utility room"),
    ("gas_meter", True, "a real household gas meter, photographed in a home or utility room"),
    ("electricity_meter", True, "a real electricity meter or electric power meter, photographed"),
    ("newspaper", False, "a page from a newspaper or magazine with printed text"),
    ("diagram", False, "a technical diagram, schematic, chart, or line drawing"),
    ("webpage", False, "a screenshot of a website, online shop, or advertisement"),
    ("unrelated", False, "a photo of something unrelated to utility meters"),
    ("text_document", False, "a scanned document or page of text, without a utility meter"),
)


def discover_images(input_dir: Path, output_dir: Path) -> list[Path]:
    images = []
    output_resolved = output_dir.resolve()
    for path in input_dir.rglob("*"):
        if not path.is_file() or path.suffix.lower() not in IMAGE_EXTENSIONS:
            continue
        try:
            path.resolve().relative_to(output_resolved)
            continue  # Never reprocess this script's previous output.
        except ValueError:
            pass
        images.append(path)
    return sorted(images, key=lambda path: str(path).casefold())


def choose_device(torch, requested: str) -> str:
    if requested == "auto":
        return "cuda" if torch.cuda.is_available() else "cpu"
    if requested == "cuda" and not torch.cuda.is_available():
        raise RuntimeError("Обрано CUDA, але PyTorch не бачить сумісну NVIDIA GPU.")
    return requested


def decision_for_score(score: float, keep_threshold: float, reject_threshold: float) -> str:
    if score >= keep_threshold:
        return "keep"
    if score <= reject_threshold:
        return "reject"
    return "review"


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Локально сортує фото за допомогою CLIP; вихідні файли не видаляються."
    )
    parser.add_argument("--input", type=Path, default=Path("data"), help="Коренева папка для рекурсивного пошуку")
    parser.add_argument(
        "--output", type=Path, default=Path("data/meter_filter_results"),
        help="Папка для копій keep/review/reject і CSV-звіту",
    )
    parser.add_argument("--model", default=MODEL_ID, help="ID або локальний шлях до сумісної CLIP-моделі")
    parser.add_argument("--device", choices=("auto", "cpu", "cuda"), default="auto")
    parser.add_argument("--batch-size", type=int, default=8)
    parser.add_argument("--keep-threshold", type=float, default=0.65)
    parser.add_argument("--reject-threshold", type=float, default=0.30)
    args = parser.parse_args()

    input_dir = args.input.expanduser().resolve()
    output_dir = args.output.expanduser().resolve()
    if not input_dir.is_dir():
        parser.error(f"папка не знайдена: {input_dir}")
    if args.batch_size < 1:
        parser.error("--batch-size має бути більшим за нуль")
    if not (0.0 <= args.reject_threshold < args.keep_threshold <= 1.0):
        parser.error("потрібно: 0 <= --reject-threshold < --keep-threshold <= 1")
    if input_dir == output_dir:
        parser.error("--output має відрізнятися від --input")

    images = discover_images(input_dir, output_dir)
    if not images:
        print(f"У {input_dir} не знайдено підтримуваних зображень.")
        return 0

    try:
        import torch
        from PIL import Image, ImageOps
        from transformers import CLIPModel, CLIPProcessor
    except ImportError as error:
        print(
            "Не встановлені залежності. Виконай: py -m pip install torch transformers pillow",
            file=sys.stderr,
        )
        print(f"Деталі: {error}", file=sys.stderr)
        return 2

    try:
        device = choose_device(torch, args.device)
        print(f"Завантажую CLIP {args.model} на {device}; перший запуск може завантажити ваги моделі.")
        processor = CLIPProcessor.from_pretrained(args.model)
        model = CLIPModel.from_pretrained(args.model).to(device)
        model.eval()
    except Exception as error:
        print(f"Не вдалося завантажити модель: {error}", file=sys.stderr)
        return 2

    output_dir.mkdir(parents=True, exist_ok=True)
    report_path = output_dir / "filter_results.csv"
    prompt_texts = [prompt for _, _, prompt in PROMPTS]
    meter_positions = [index for index, (_, is_meter, _) in enumerate(PROMPTS) if is_meter]
    non_meter_positions = [index for index, (_, is_meter, _) in enumerate(PROMPTS) if not is_meter]
    totals = {"keep": 0, "review": 0, "reject": 0, "error": 0}

    fields = [
        "source_path", "relative_path", "decision", "meter_score", "best_meter_class",
        "best_non_meter_class", "copied_to", "error",
    ]
    with report_path.open("w", encoding="utf-8-sig", newline="") as csv_file:
        writer = csv.DictWriter(csv_file, fieldnames=fields)
        writer.writeheader()

        for start in range(0, len(images), args.batch_size):
            batch_paths = images[start : start + args.batch_size]
            loaded_images = []
            valid_paths = []
            for path in batch_paths:
                try:
                    with Image.open(path) as image:
                        image = ImageOps.exif_transpose(image).convert("RGB")
                        loaded_images.append(image.copy())
                        valid_paths.append(path)
                except Exception as error:
                    relative = path.relative_to(input_dir)
                    writer.writerow({
                        "source_path": str(path), "relative_path": str(relative), "decision": "error",
                        "meter_score": "", "best_meter_class": "", "best_non_meter_class": "",
                        "copied_to": "", "error": str(error),
                    })
                    totals["error"] += 1

            if loaded_images:
                try:
                    inputs = processor(
                        text=prompt_texts,
                        images=loaded_images,
                        return_tensors="pt",
                        padding=True,
                    )
                    inputs = {key: value.to(device) for key, value in inputs.items()}
                    with torch.inference_mode():
                        logits = model(**inputs).logits_per_image.float()
                    meter_logits = logits[:, meter_positions]
                    non_meter_logits = logits[:, non_meter_positions]
                    binary_logits = torch.stack(
                        (meter_logits.max(dim=1).values, non_meter_logits.max(dim=1).values), dim=1
                    )
                    scores = binary_logits.softmax(dim=1)[:, 0].cpu().tolist()
                    meter_choices = meter_logits.argmax(dim=1).cpu().tolist()
                    non_meter_choices = non_meter_logits.argmax(dim=1).cpu().tolist()
                except Exception as error:
                    print(f"Помилка обробки групи зображень: {error}", file=sys.stderr)
                    return 2

                for path, score, meter_choice, non_meter_choice in zip(
                    valid_paths, scores, meter_choices, non_meter_choices
                ):
                    decision = decision_for_score(score, args.keep_threshold, args.reject_threshold)
                    relative = path.relative_to(input_dir)
                    destination = output_dir / decision / relative
                    destination.parent.mkdir(parents=True, exist_ok=True)
                    try:
                        shutil.copy2(path, destination)
                        copied_to = str(destination)
                        error_text = ""
                    except Exception as error:
                        copied_to = ""
                        error_text = f"copy failed: {error}"
                        decision = "error"
                    totals[decision] += 1
                    writer.writerow({
                        "source_path": str(path),
                        "relative_path": str(relative),
                        "decision": decision,
                        "meter_score": f"{score:.4f}",
                        "best_meter_class": PROMPTS[meter_positions[meter_choice]][0],
                        "best_non_meter_class": PROMPTS[non_meter_positions[non_meter_choice]][0],
                        "copied_to": copied_to,
                        "error": error_text,
                    })

            csv_file.flush()
            processed = min(start + len(batch_paths), len(images))
            print(f"Оброблено {processed}/{len(images)}")

    print(f"Готово. Лічильники: {totals['keep']}; перевірити: {totals['review']}; відхилено: {totals['reject']}; помилки: {totals['error']}.")
    print(f"Копії за категоріями: {output_dir}")
    print(f"Звіт: {report_path}")
    print("Оригінали не змінено й не видалено.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
