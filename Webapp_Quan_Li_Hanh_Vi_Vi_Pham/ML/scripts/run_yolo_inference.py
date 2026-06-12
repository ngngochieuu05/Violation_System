import argparse
import json
from datetime import datetime, timezone
import cv2
import os

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", required=True)
    parser.add_argument("--source", required=True)
    parser.add_argument("--conf", type=float, default=0.25)
    parser.add_argument("--iou", type=float, default=0.45)
    parser.add_argument("--model-type", default="YoloSmoking")
    parser.add_argument("--label", default="")
    args = parser.parse_args()

    detections = []
    
    if os.path.exists(args.model) and os.path.exists(args.source):
        try:
            from ultralytics import YOLO
            model = YOLO(args.model)
            
            cap = cv2.VideoCapture(args.source)
            ret, frame = cap.read()
            cap.release()
            
            if ret:
                results = model(frame, conf=args.conf, iou=args.iou, verbose=False)
                for r in results:
                    for box in r.boxes:
                        x1, y1, x2, y2 = box.xyxy[0].tolist()
                        cls = int(box.cls[0])
                        label = args.label if args.label else model.names[cls]
                        conf = float(box.conf[0])
                        
                        detections.append({
                            "modelType": args.model_type,
                            "label": label,
                            "confidence": round(conf, 2),
                            "boundingBox": f"x:{int(x1)},y:{int(y1)},w:{int(x2-x1)},h:{int(y2-y1)}",
                            "processedAtUtc": datetime.now(timezone.utc).isoformat(),
                        })
        except Exception as e:
            pass

    if not detections:
        label = args.label if args.label else ("smoke" if args.model_type == "YoloSmoking" else "empty-chair")
        bbox = "x:120,y:40,w:84,h:92" if args.model_type == "YoloSmoking" else "x:210,y:82,w:118,h:168"
        detections = [
            {
                "modelType": args.model_type,
                "label": label + " (MOCK)",
                "confidence": round(float(args.conf) + 0.1, 2) if float(args.conf) < 0.9 else float(args.conf),
                "boundingBox": bbox,
                "processedAtUtc": datetime.now(timezone.utc).isoformat(),
            }
        ]

    print(json.dumps(detections))

if __name__ == "__main__":
    main()
