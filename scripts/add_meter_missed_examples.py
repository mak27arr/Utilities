"""Add manually reviewed missed meter examples to the YOLO training split."""

from __future__ import annotations

import argparse
import csv
from pathlib import Path

from PIL import Image, ImageDraw


# Coordinates are normalized (left, top, right, bottom) and were placed by
# reviewing each clean source image. Images with two meters have two boxes.
ANNOTATIONS: dict[str, list[tuple[float, float, float, float]]] = {
    "bing_07577332971b2e68.jpg": [(0.52, 0.37, 0.68, 0.46)],
    "bing_179e6656b82b4ffe.jpg": [(0.40, 0.13, 0.72, 0.22)],
    "bing_19afa89f044dc829.jpg": [(0.42, 0.32, 0.71, 0.44)],
    "bing_1c214654c56bfded.jpg": [(0.36, 0.34, 0.60, 0.43)],
    "bing_1c362b3c318b0b6f.jpg": [(0.38, 0.39, 0.56, 0.45)],
    "bing_3f4ebdbb319a892d.jpg": [(0.52, 0.40, 0.59, 0.56)],
    "bing_4f934665d132ff99.jpg": [(0.48, 0.37, 0.61, 0.46)],
    "bing_6079049abf8e00d3.jpg": [(0.39, 0.35, 0.60, 0.48)],
    "bing_64b544f4c762a6af.jpg": [
        (0.25, 0.07, 0.47, 0.21),
        (0.61, 0.30, 0.83, 0.47),
    ],
    "bing_6e097ecc5f7e65c5.jpg": [(0.30, 0.20, 0.61, 0.39)],
    "bing_71e7cfd56ea4634a.jpg": [(0.28, 0.28, 0.42, 0.39)],
    "bing_7caa19825356c4fb.jpg": [(0.40, 0.15, 0.63, 0.26)],
    "bing_9ac232d788fe134d.jpg": [(0.37, 0.42, 0.53, 0.60)],
    "bing_a1057c5ba7b52173.jpg": [(0.39, 0.33, 0.51, 0.52)],
    "bing_c14477b9d5fa0e70.jpg": [(0.28, 0.25, 0.58, 0.44)],
    "bing_cea558e2ffeaba25.jpg": [(0.44, 0.35, 0.63, 0.44)],
    "google_0b2bf1567a92d3b1.jpg": [(0.44, 0.25, 0.76, 0.47)],
    "google_45bc6eb3befdb144.jpg": [(0.54, 0.15, 0.82, 0.35)],
    "google_6b62fb87fa83f080.jpg": [
        (0.10, 0.54, 0.35, 0.74),
        (0.53, 0.11, 0.77, 0.30),
    ],
    "google_6c2c898ed8c9a3d3.jpg": [
        (0.06, 0.31, 0.34, 0.57),
        (0.66, 0.19, 0.93, 0.44),
    ],
    "google_6da75007631996dc.jpg": [(0.30, 0.58, 0.52, 0.76)],
    "google_71cf7df043c582d1.jpg": [(0.36, 0.29, 0.66, 0.45)],
    "google_89db735717a03190.jpg": [(0.39, 0.30, 0.61, 0.43)],
    "google_934748a134663680.jpg": [(0.36, 0.45, 0.68, 0.57)],
    "google_a1166287b9200221.jpg": [(0.53, 0.12, 0.80, 0.27)],
    "google_b419792399d69409.jpg": [(0.38, 0.30, 0.65, 0.53)],
    "photo_2026-10-19_20-42-34.jpg": [(0.27, 0.27, 0.76, 0.33)],
}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apply", action="store_true", help="Copy images and labels into the training split")
    args = parser.parse_args()

    root = Path(__file__).resolve().parents[1]
    misses_dir = root / "runs" / "detect" / "predict"
    clean_dir = root / "data" / "meter_filter_results" / "keep"
    dataset_dir = root / "data" / "meter_reading_yolo_dataset"
    preview_dir = dataset_dir / "hard_examples_preview"
    preview_dir.mkdir(parents=True, exist_ok=True)

    csv_path = dataset_dir / "hard_examples_annotations.csv"
    with csv_path.open("w", newline="", encoding="utf-8") as file:
        writer = csv.writer(file)
        writer.writerow(("image", "box_index", "x_center", "y_center", "width", "height"))

        for filename, boxes in ANNOTATIONS.items():
            candidate = next((p for p in clean_dir.iterdir() if p.is_file() and p.stem == Path(filename).stem), None)
            source = candidate or misses_dir / filename
            if not source.is_file():
                raise FileNotFoundError(f"Missing source image: {filename}")

            with Image.open(source) as image_file:
                image = image_file.convert("RGB")
            width, height = image.size
            yolo_rows: list[str] = []
            draw = ImageDraw.Draw(image)
            line_width = max(3, round(min(width, height) * 0.006))
            for index, (left, top, right, bottom) in enumerate(boxes, start=1):
                if not (0 <= left < right <= 1 and 0 <= top < bottom <= 1):
                    raise ValueError(f"Invalid box for {filename}: {left, top, right, bottom}")
                center_x = (left + right) / 2
                center_y = (top + bottom) / 2
                box_width = right - left
                box_height = bottom - top
                yolo_rows.append(f"0 {center_x:.6f} {center_y:.6f} {box_width:.6f} {box_height:.6f}")
                writer.writerow((filename, index, f"{center_x:.6f}", f"{center_y:.6f}", f"{box_width:.6f}", f"{box_height:.6f}"))
                draw.rectangle(
                    (round(left * width), round(top * height), round(right * width), round(bottom * height)),
                    outline=(255, 0, 0),
                    width=line_width,
                )

            image.save(preview_dir / filename, quality=94)
            if args.apply:
                train_images = dataset_dir / "images" / "train"
                train_labels = dataset_dir / "labels" / "train"
                train_images.mkdir(parents=True, exist_ok=True)
                train_labels.mkdir(parents=True, exist_ok=True)
                clean = Image.open(source).convert("RGB")
                clean.save(train_images / filename, quality=95, optimize=True)
                (train_labels / f"{Path(filename).stem}.txt").write_text("\n".join(yolo_rows) + "\n", encoding="ascii")

    print(f"Reviewed images: {len(ANNOTATIONS)}")
    print(f"Reading-area boxes: {sum(map(len, ANNOTATIONS.values()))}")
    print(f"Preview: {preview_dir}")
    print(f"Annotation list: {csv_path}")
    if args.apply:
        readme_path = dataset_dir / "README.md"
        readme = readme_path.read_text(encoding="utf-8") if readme_path.exists() else "# Meter reading-area YOLO dataset\n"
        summary = f"- Additional manually reviewed hard examples: {len(ANNOTATIONS)} images, {sum(map(len, ANNOTATIONS.values()))} reading-area boxes; added to train."
        train_count = len(list((dataset_dir / "images" / "train").glob("*.jpg")))
        val_count = len(list((dataset_dir / "images" / "val").glob("*.jpg")))
        lines = [
            f"- Train images: {train_count}; validation images: {val_count}" if line.startswith("- Train images:") else line
            for line in readme.splitlines()
            if not line.startswith("- Additional manually reviewed hard examples:")
        ]
        lines.append(summary)
        readme_path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print("Dataset updated: yes" if args.apply else "Dataset updated: no (preview only)")


if __name__ == "__main__":
    main()
