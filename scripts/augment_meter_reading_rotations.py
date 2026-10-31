"""Create rotation augmentations and YOLO labels for the seven manually boxed meters."""

from __future__ import annotations

import argparse
import math
from collections import deque
from pathlib import Path

from PIL import Image


SAMPLES = (
    "bing_1d1db698e615eaa9",
    "bing_ac20a008556cf4aa",
    "google_9e7289a90720574f",
    "google_9b2acb7177725f40",
    "google_c14dc7cd36f49fa7",
    "google_bae7b40bcc322608",
    "google_aa342f86e9dbef34",
)
ANGLES = (-15, -12, -9, -6, -3, 3, 6, 9, 12, 15)


def red_rectangle_bounds(image_path: Path) -> tuple[int, int, int, int]:
    """Return the bounds of the largest connected red annotation in the marked image."""
    image = Image.open(image_path).convert("RGB")
    width, height = image.size
    pixels = image.load()
    mask = bytearray(width * height)
    for y in range(height):
        for x in range(width):
            red, green, blue = pixels[x, y]
            if red > 65 and red > green * 1.3 and red > blue * 1.3 and green < 150 and blue < 150:
                mask[y * width + x] = 1

    largest: list[int] = []
    for index, active in enumerate(mask):
        if not active:
            continue
        mask[index] = 0
        queue = deque([index])
        component: list[int] = []
        while queue:
            current = queue.popleft()
            component.append(current)
            x, y = current % width, current // width
            for neighbor_y in range(max(0, y - 1), min(height, y + 2)):
                for neighbor_x in range(max(0, x - 1), min(width, x + 2)):
                    neighbor = neighbor_y * width + neighbor_x
                    if mask[neighbor]:
                        mask[neighbor] = 0
                        queue.append(neighbor)
        if len(component) > len(largest):
            largest = component

    if len(largest) < 100:
        raise ValueError(f"No clear red rectangle annotation found in {image_path.name}")
    xs = [index % width for index in largest]
    ys = [index // width for index in largest]
    return min(xs), min(ys), max(xs) + 1, max(ys) + 1


def rotate_box(box: tuple[int, int, int, int], width: int, height: int, angle: float):
    left, top, right, bottom = box
    cx, cy = width / 2, height / 2
    radians = math.radians(angle)
    cosine, sine = math.cos(radians), math.sin(radians)
    points = []
    for x, y in ((left, top), (right, top), (right, bottom), (left, bottom)):
        dx, dy = x - cx, y - cy
        points.append((cx + cosine * dx + sine * dy, cy - sine * dx + cosine * dy))
    x1 = max(0.0, min(point[0] for point in points))
    y1 = max(0.0, min(point[1] for point in points))
    x2 = min(float(width), max(point[0] for point in points))
    y2 = min(float(height), max(point[1] for point in points))
    return x1, y1, x2, y2


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--marked-dir", type=Path, default=Path("data/meter_filter_results/mark"))
    parser.add_argument("--source-dir", type=Path, default=Path("data/meter_filter_results/keep"))
    parser.add_argument("--output-dir", type=Path, default=Path("data/meter_filter_results/mark/gen"))
    args = parser.parse_args()
    args.output_dir.mkdir(parents=True, exist_ok=True)

    created = 0
    for stem in SAMPLES:
        marked_path = next((args.marked_dir / f"{stem}{ext}" for ext in (".jpg", ".jpeg", ".png", ".webp")
                            if (args.marked_dir / f"{stem}{ext}").is_file()), None)
        if marked_path is None:
            raise FileNotFoundError(f"Missing marked source image for {stem}")

        box = red_rectangle_bounds(marked_path)
        with Image.open(marked_path) as source:
            marked = source.convert("RGB")
        width, height = marked.size

        for angle in ANGLES:
            output_stem = f"{stem}_rot_{angle:+03d}"
            output_image = args.output_dir / f"{output_stem}.jpg"
            output_label = args.output_dir / f"{output_stem}.txt"
            rotated = marked.rotate(angle, resample=Image.Resampling.BICUBIC, expand=False, fillcolor=(255, 255, 255))
            rotated.save(output_image, quality=95, optimize=True)
            x1, y1, x2, y2 = rotate_box(box, width, height, angle)
            center_x, center_y = (x1 + x2) / (2 * width), (y1 + y2) / (2 * height)
            box_width, box_height = (x2 - x1) / width, (y2 - y1) / height
            output_label.write_text(
                f"0 {center_x:.6f} {center_y:.6f} {box_width:.6f} {box_height:.6f}\n",
                encoding="ascii",
            )
            created += 1

    print(f"Created {created} rotated images and matching YOLO labels in {args.output_dir.resolve()}")
    print("Class id 0 is reading_area. The red annotation rectangles are preserved in the images.")


if __name__ == "__main__":
    main()
