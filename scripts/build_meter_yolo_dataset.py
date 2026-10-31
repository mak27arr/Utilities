"""Build a leakage-free YOLO reading_area dataset from red-boxed meter photos."""

from __future__ import annotations

import argparse
import math
import random
import re
from collections import deque
from pathlib import Path

from PIL import Image


IMAGE_EXTENSIONS = {".jpg", ".jpeg", ".png", ".webp"}
GEN_PATTERN = re.compile(r"^(?P<stem>.+)_rot_(?P<angle>[+-]\d+)\.jpg$", re.IGNORECASE)


def find_by_stem(folder: Path, stem: str) -> Path | None:
    return next((p for p in folder.iterdir() if p.is_file() and p.stem == stem and p.suffix.lower() in IMAGE_EXTENSIONS), None)


def annotation_box(marked_path: Path, clean_path: Path) -> tuple[int, int, int, int]:
    """Recover the red markup as a bounding box by comparing it with the clean source."""
    marked = Image.open(marked_path).convert("RGB")
    clean = Image.open(clean_path).convert("RGB").resize(marked.size)
    width, height = marked.size
    marked_pixels, clean_pixels = marked.load(), clean.load()
    mask = bytearray(width * height)

    for y in range(height):
        for x in range(width):
            red, green, blue = marked_pixels[x, y]
            source_red, source_green, source_blue = clean_pixels[x, y]
            difference = (
                abs(red - source_red)
                + abs(green - source_green)
                + abs(blue - source_blue)
            )
            if (
                red > 25
                and red > green * 1.2
                and red > blue * 1.2
                and difference > 70
            ):
                mask[y * width + x] = 1

    components: list[tuple[int, tuple[int, int, int, int]]] = []
    for index, active in enumerate(mask):
        if not active:
            continue
        mask[index] = 0
        queue = deque([index])
        min_x = max_x = index % width
        min_y = max_y = index // width
        area = 0
        while queue:
            current = queue.popleft()
            x, y = current % width, current // width
            area += 1
            min_x, max_x = min(min_x, x), max(max_x, x)
            min_y, max_y = min(min_y, y), max(max_y, y)
            for neighbor_y in range(max(0, y - 1), min(height, y + 2)):
                for neighbor_x in range(max(0, x - 1), min(width, x + 2)):
                    neighbor = neighbor_y * width + neighbor_x
                    if mask[neighbor]:
                        mask[neighbor] = 0
                        queue.append(neighbor)
        box = (min_x, min_y, max_x + 1, max_y + 1)
        box_width, box_height = box[2] - box[0], box[3] - box[1]
        if area >= 100 and box_width >= 20 and box_height >= 10 and box_width / box_height >= 1.1:
            components.append((area, box))

    if not components:
        raise ValueError(f"Could not find a red annotation on {marked_path.name}")
    return max(components, key=lambda item: item[0])[1]


def normalized_label(box: tuple[float, float, float, float], width: int, height: int) -> str:
    left, top, right, bottom = box
    center_x = (left + right) / (2 * width)
    center_y = (top + bottom) / (2 * height)
    box_width = (right - left) / width
    box_height = (bottom - top) / height
    values = (center_x, center_y, box_width, box_height)
    if not all(0 < value <= 1 for value in values):
        raise ValueError(f"Invalid normalized YOLO box: {values}")
    return f"0 {center_x:.6f} {center_y:.6f} {box_width:.6f} {box_height:.6f}\n"


def rotate_box(box: tuple[float, float, float, float], width: int, height: int, angle: float):
    left, top, right, bottom = box
    center_x, center_y = width / 2, height / 2
    radians = math.radians(angle)
    cosine, sine = math.cos(radians), math.sin(radians)
    rotated = []
    for x, y in ((left, top), (right, top), (right, bottom), (left, bottom)):
        dx, dy = x - center_x, y - center_y
        rotated.append((center_x + cosine * dx + sine * dy, center_y - sine * dx + cosine * dy))
    return (
        max(0.0, min(point[0] for point in rotated)),
        max(0.0, min(point[1] for point in rotated)),
        min(float(width), max(point[0] for point in rotated)),
        min(float(height), max(point[1] for point in rotated)),
    )


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--marked-dir", type=Path, default=Path("data/meter_filter_results/mark"))
    parser.add_argument("--clean-dir", type=Path, default=Path("data/meter_filter_results/keep"))
    parser.add_argument("--output-dir", type=Path, default=Path("data/meter_reading_yolo_dataset"))
    parser.add_argument("--val-fraction", type=float, default=0.2)
    parser.add_argument("--seed", type=int, default=42)
    args = parser.parse_args()

    marked_dir = args.marked_dir.resolve()
    clean_dir = args.clean_dir.resolve()
    gen_dir = marked_dir / "gen"
    output_dir = args.output_dir.resolve()
    if not 0 < args.val_fraction < 1:
        parser.error("--val-fraction must be between 0 and 1")
    if output_dir.exists() and any(output_dir.iterdir()):
        parser.error(f"Output directory is not empty: {output_dir}")

    base_records: dict[str, tuple[Path, Path, tuple[int, int, int, int]]] = {}
    for marked_path in sorted(marked_dir.iterdir()):
        if not marked_path.is_file() or marked_path.suffix.lower() not in IMAGE_EXTENSIONS:
            continue
        clean_path = find_by_stem(clean_dir, marked_path.stem)
        if clean_path is None:
            raise FileNotFoundError(f"No clean source image found for {marked_path.name}")
        try:
            box = annotation_box(marked_path, clean_path)
        except ValueError as error:
            print(f"Skipping {marked_path.name}: {error}")
            continue
        base_records[marked_path.stem] = (marked_path, clean_path, box)

    if not base_records:
        raise RuntimeError("No marked source images found")

    samples: list[tuple[str, str, Path, tuple[int, int, int, int], float | None]] = []
    for stem, (marked_path, clean_path, box) in base_records.items():
        samples.append((stem, stem, clean_path, box, None))

    if gen_dir.is_dir():
        for generated_path in sorted(gen_dir.iterdir()):
            match = GEN_PATTERN.match(generated_path.name)
            if not match:
                continue
            stem, angle = match.group("stem"), float(match.group("angle"))
            if stem in base_records:
                _, clean_path, box = base_records[stem]
                samples.append((f"{stem}_rot_{int(angle):+03d}", stem, clean_path, box, angle))

    groups = sorted({sample[1] for sample in samples})
    random.Random(args.seed).shuffle(groups)
    validation_count = max(1, round(len(groups) * args.val_fraction))
    validation_groups = set(groups[:validation_count])

    for split in ("train", "val"):
        (output_dir / "images" / split).mkdir(parents=True, exist_ok=True)
        (output_dir / "labels" / split).mkdir(parents=True, exist_ok=True)

    for output_stem, source_group, clean_path, marked_box, angle in samples:
        split = "val" if source_group in validation_groups else "train"
        with Image.open(clean_path) as source:
            clean = source.convert("RGB")
        source_width, source_height = clean.size
        marked_path = base_records[source_group][0]
        marked_width, marked_height = Image.open(marked_path).size
        box = (
            marked_box[0] * source_width / marked_width,
            marked_box[1] * source_height / marked_height,
            marked_box[2] * source_width / marked_width,
            marked_box[3] * source_height / marked_height,
        )
        if angle is not None:
            clean = clean.rotate(angle, resample=Image.Resampling.BICUBIC, expand=False, fillcolor=(255, 255, 255))
            box = rotate_box(box, source_width, source_height, angle)

        image_path = output_dir / "images" / split / f"{output_stem}.jpg"
        label_path = output_dir / "labels" / split / f"{output_stem}.txt"
        clean.save(image_path, quality=95, optimize=True)
        label_path.write_text(normalized_label(box, source_width, source_height), encoding="ascii")

    (output_dir / "data.yaml").write_text(
        f"path: {output_dir.as_posix()}\ntrain: images/train\nval: images/val\nnames:\n  0: reading_area\n",
        encoding="utf-8",
    )
    train_count = len(list((output_dir / "images" / "train").glob("*.jpg")))
    val_count = len(list((output_dir / "images" / "val").glob("*.jpg")))
    (output_dir / "README.md").write_text(
        "# Meter reading-area YOLO dataset\n\n"
        f"- Source images with recovered red-box annotations: {len(base_records)}\n"
        f"- Rotation variants from mark/gen: {len(samples) - len(base_records)}\n"
        f"- Train images: {train_count}; validation images: {val_count}\n"
        "- Class 0: `reading_area`\n\n"
        "Images are copied from the clean originals in `meter_filter_results/keep`; red markup is used only to create labels and is not present in training pixels. "
        "Originals and their generated rotations are assigned to the same split to prevent leakage.\n",
        encoding="utf-8",
    )
    print(f"Dataset created: {output_dir}")
    print(f"Base annotated photos: {len(base_records)}; rotation variants: {len(samples) - len(base_records)}")
    print(f"Train: {train_count}; validation: {val_count}; total: {train_count + val_count}")


if __name__ == "__main__":
    main()
