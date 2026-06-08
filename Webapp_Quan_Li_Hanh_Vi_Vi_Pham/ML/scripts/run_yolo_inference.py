import argparse
import json
from datetime import datetime, timezone


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", required=True)
    parser.add_argument("--source", required=True)
    args = parser.parse_args()

    # Placeholder runner. Replace this block with ultralytics YOLO inference:
    # from ultralytics import YOLO
    # model = YOLO(args.model)
    # results = model(args.source)
    _ = args.model
    _ = args.source

    detections = [
        {
            "label": "no-helmet",
            "confidence": 0.91,
            "boundingBox": "x:120,y:40,w:84,h:92",
            "processedAtUtc": datetime.now(timezone.utc).isoformat(),
        }
    ]

    print(json.dumps(detections))


if __name__ == "__main__":
    main()
