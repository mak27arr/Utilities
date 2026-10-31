"""Export the trained reading-area detector for the Android app."""

from __future__ import annotations

import argparse
import shutil
from pathlib import Path

from ultralytics import YOLO


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_MODEL = ROOT / "runs/detect/runs/meter/reading_area-2/weights/best.pt"
APP_MODEL = ROOT / "src/Utilities.Mobile/Resources/Raw/reading_area.onnx"


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model", type=Path, default=DEFAULT_MODEL, help="Trained YOLO .pt checkpoint")
    parser.add_argument("--output", type=Path, default=APP_MODEL, help="Destination ONNX asset")
    args = parser.parse_args()

    checkpoint = args.model.resolve()
    destination = args.output.resolve()
    if not checkpoint.is_file():
        raise SystemExit(f"Checkpoint not found: {checkpoint}")

    exported = Path(
        YOLO(str(checkpoint)).export(format="onnx", imgsz=640, nms=True, simplify=False, opset=17)
    ).resolve()
    destination.parent.mkdir(parents=True, exist_ok=True)
    if exported != destination:
        shutil.copy2(exported, destination)
    print(f"Android model asset: {destination}")


if __name__ == "__main__":
    main()
