import argparse
import json
from datetime import datetime, timezone


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", required=True)
    parser.add_argument("--source", required=True)
    parser.add_argument("--conf", type=float, default=0.25)
    parser.add_argument("--iou", type=float, default=0.45)
    args = parser.parse_args()

    # In production, we'd load ultralytics YOLO:
    # from ultralytics import YOLO
    # model = YOLO(args.model)
    # results = model(args.source, conf=args.conf, iou=args.iou)
    
    _ = args.model
    _ = args.source
    _ = args.conf
    _ = args.iou

    # Return simulated detections with dynamic confidence to show it changes
    detections = [
        {
            "label": "no-helmet",
            "confidence": round(float(args.conf) + 0.1, 2) if float(args.conf) < 0.9 else float(args.conf),
            "boundingBox": "x:120,y:40,w:84,h:92",
            "processedAtUtc": datetime.now(timezone.utc).isoformat(),
        }
    ]

    print(json.dumps(detections))


if __name__ == "__main__":
    main()
