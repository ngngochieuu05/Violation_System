import argparse
import base64
import sys
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')
if hasattr(sys.stderr, 'reconfigure'):
    sys.stderr.reconfigure(encoding='utf-8')

import json
import time
from datetime import datetime, timezone
from pathlib import Path

import cv2


def resize_frame(frame, max_side: int = 960):
    height, width = frame.shape[:2]
    longest_side = max(height, width)
    if longest_side <= max_side:
        return frame

    scale = max_side / float(longest_side)
    resized_width = max(1, int(width * scale))
    resized_height = max(1, int(height * scale))
    return cv2.resize(frame, (resized_width, resized_height), interpolation=cv2.INTER_AREA)


def iter_candidate_frames(source_path: str, max_frames: int = 8):
    source = Path(source_path)
    image_extensions = {".jpg", ".jpeg", ".png", ".bmp", ".webp"}

    if source.suffix.lower() in image_extensions:
        frame = cv2.imread(str(source))
        if frame is not None:
            yield 0, resize_frame(frame)
        return

    capture = cv2.VideoCapture(str(source))
    if not capture.isOpened():
        return

    total_frames = int(capture.get(cv2.CAP_PROP_FRAME_COUNT) or 0)
    step = max(1, total_frames // max_frames) if total_frames > 0 else 5
    frame_index = 0
    yielded = 0

    while yielded < max_frames:
        ok, frame = capture.read()
        if not ok:
            break

        if frame_index % step == 0:
            yield frame_index, resize_frame(frame)
            yielded += 1

        frame_index += 1

    capture.release()


def encode_jpeg_base64(frame):
    ok, encoded = cv2.imencode(".jpg", frame, [int(cv2.IMWRITE_JPEG_QUALITY), 85])
    if not ok:
        return ""
    return base64.b64encode(encoded.tobytes()).decode("utf-8")


def build_detection(model_type: str, class_name: str, confidence: float, bbox, model_name: str):
    x1, y1, x2, y2 = bbox
    return {
        "modelType": model_type,
        "label": class_name,
        "displayLabel": f"{model_name}: {class_name}" if model_name else class_name,
        "confidence": round(float(confidence), 2),
        "boundingBox": f"x:{int(x1)},y:{int(y1)},w:{int(x2 - x1)},h:{int(y2 - y1)}",
        "processedAtUtc": datetime.now(timezone.utc).isoformat(),
    }


def render_mock_frame(model_type: str, model_name: str):
    frame = 255 * (cv2.UMat(480, 640, cv2.CV_8UC3).get() * 0)
    cv2.putText(frame, "ComplianceHub Monitoring", (24, 40), cv2.FONT_HERSHEY_SIMPLEX, 0.9, (255, 255, 255), 2)

    if model_type == "YoloSmoking":
        label = "Cigarette"
        bbox = (120, 70, 240, 220)
        color = (0, 102, 255)
    else:
        label = "un-occupied_desk"
        bbox = (260, 100, 430, 330)
        color = (0, 200, 120)

    x1, y1, x2, y2 = bbox
    cv2.rectangle(frame, (x1, y1), (x2, y2), color, 3)
    cv2.putText(
        frame,
        f"{model_name or model_type}: {label} 0.92",
        (x1, max(24, y1 - 10)),
        cv2.FONT_HERSHEY_SIMPLEX,
        0.7,
        color,
        2,
    )

    return {
        "detections": [
            build_detection(model_type, label, 0.92, bbox, model_name),
        ],
        "annotatedBase64": encode_jpeg_base64(frame),
        "imageMimeType": "image/jpeg",
        "frameIndex": 0,
        "framesExamined": 0,
        "isMock": True,
    }


def build_real_payload(model_type: str, model_name: str, frame_index: int, frames_examined: int, frame, result):
    detections = []
    names = result.names if result is not None else {}

    if result is not None:
        for box in result.boxes:
            x1, y1, x2, y2 = box.xyxy[0].tolist()
            cls = int(box.cls[0])
            class_name = names[cls] if isinstance(names, list) else names.get(cls, str(cls))
            detections.append(
                build_detection(
                    model_type,
                    str(class_name),
                    float(box.conf[0]),
                    (x1, y1, x2, y2),
                    model_name,
                )
            )

    annotated_frame = result.plot() if result is not None else frame
    if not detections:
        cv2.putText(
            annotated_frame,
            f"{model_name or model_type}: khong co doi tuong phu hop",
            (24, 36),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.8,
            (255, 255, 255),
            2,
        )

    return {
        "detections": detections,
        "annotatedBase64": encode_jpeg_base64(annotated_frame if annotated_frame is not None else frame),
        "imageMimeType": "image/jpeg",
        "frameIndex": frame_index,
        "framesExamined": frames_examined,
        "isMock": False,
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", required=True)
    parser.add_argument("--source", required=True)
    parser.add_argument("--conf", type=float, default=0.25)
    parser.add_argument("--iou", type=float, default=0.45)
    parser.add_argument("--model-type", default="YoloSmoking")
    parser.add_argument("--label", default="")
    parser.add_argument("--max-frames", type=int, default=8)
    args = parser.parse_args()

    if not Path(args.model).exists() or not Path(args.source).exists():
        print(json.dumps(render_mock_frame(args.model_type, args.label)))
        return

    try:
        from ultralytics import YOLO

        started_at = time.perf_counter()
        model = YOLO(args.model)
        best_payload = None
        fallback_payload = None
        frames_examined = 0

        for frame_index, frame in iter_candidate_frames(args.source, max_frames=max(1, args.max_frames)):
            frames_examined += 1
            results = model.predict(source=frame, conf=args.conf, iou=args.iou, verbose=False)
            result = results[0] if results else None
            payload = build_real_payload(args.model_type, args.label, frame_index, frames_examined, frame, result)

            if fallback_payload is None:
                fallback_payload = payload

            if best_payload is None:
                best_payload = payload
                continue

            current_score = (
                len(payload["detections"]),
                sum(item["confidence"] for item in payload["detections"]),
            )
            best_score = (
                len(best_payload["detections"]),
                sum(item["confidence"] for item in best_payload["detections"]),
            )

            if current_score > best_score:
                best_payload = payload

        if best_payload is None:
            best_payload = fallback_payload or render_mock_frame(args.model_type, args.label)

        best_payload["framesExamined"] = frames_examined
        best_payload["elapsedMs"] = int((time.perf_counter() - started_at) * 1000)
        print(json.dumps(best_payload))
    except Exception:
        print(json.dumps(render_mock_frame(args.model_type, args.label)))


if __name__ == "__main__":
    main()
