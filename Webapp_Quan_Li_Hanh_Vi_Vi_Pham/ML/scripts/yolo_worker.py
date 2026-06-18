import argparse
import base64
import json
import sys
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')
if hasattr(sys.stderr, 'reconfigure'):
    sys.stderr.reconfigure(encoding='utf-8')

import time
from datetime import datetime, timezone
from pathlib import Path

import cv2
import numpy as np


def resize_frame(frame, max_side=960):
    height, width = frame.shape[:2]
    longest_side = max(height, width)
    if longest_side <= max_side:
        return frame

    scale = max_side / float(longest_side)
    resized_width = max(1, int(width * scale))
    resized_height = max(1, int(height * scale))
    return cv2.resize(frame, (resized_width, resized_height), interpolation=cv2.INTER_AREA)


def iter_candidate_frames(source_path, max_frames):
    if str(source_path).lower().startswith("camera:"):
        raw_index = str(source_path).split(":", 1)[1]
        try:
            camera_index = int(raw_index)
        except ValueError:
            camera_index = 0

        capture = cv2.VideoCapture(camera_index, cv2.CAP_DSHOW)
        if not capture.isOpened():
            capture = cv2.VideoCapture(camera_index)
        if not capture.isOpened():
            return

        yielded = 0
        frame_index = 0
        while yielded < max_frames:
            ok, frame = capture.read()
            if not ok or frame is None:
                break
            yield frame_index, resize_frame(frame)
            yielded += 1
            frame_index += 1

        capture.release()
        return

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
    if total_frames <= 0:
        # Fallback to sequential read if total_frames is unknown
        frame_index = 0
        yielded = 0
        while yielded < max_frames:
            ok, frame = capture.read()
            if not ok: break
            if frame_index % 5 == 0:
                yield frame_index, resize_frame(frame)
                yielded += 1
            frame_index += 1
        capture.release()
        return

    # Use seek (CAP_PROP_POS_FRAMES) for fast extraction
    step = max(1, total_frames // max_frames)
    for i in range(max_frames):
        target_frame = i * step
        if target_frame >= total_frames:
            break
        capture.set(cv2.CAP_PROP_POS_FRAMES, target_frame)
        ok, frame = capture.read()
        if ok:
            yield target_frame, resize_frame(frame)
        else:
            # If seek fails, just read next available
            ok, frame = capture.read()
            if ok:
                yield target_frame, resize_frame(frame)

    capture.release()


def encode_jpeg_base64(frame_bgr):
    ok, encoded = cv2.imencode(".jpg", frame_bgr, [int(cv2.IMWRITE_JPEG_QUALITY), 85])
    if not ok:
        return ""
    return base64.b64encode(encoded.tobytes()).decode("utf-8")


def normalize_mojibake(value):
    if not isinstance(value, str) or not value:
        return value

    try:
        repaired = value.encode("latin1", errors="ignore").decode("utf-8", errors="ignore")
        return repaired or value
    except Exception:
        return value


def bbox_iou(box_a, box_b):
    ax1, ay1, ax2, ay2 = box_a
    bx1, by1, bx2, by2 = box_b
    inter_x1 = max(ax1, bx1)
    inter_y1 = max(ay1, by1)
    inter_x2 = min(ax2, bx2)
    inter_y2 = min(ay2, by2)
    if inter_x2 <= inter_x1 or inter_y2 <= inter_y1:
        return 0.0
    inter_area = (inter_x2 - inter_x1) * (inter_y2 - inter_y1)
    area_a = max(1.0, (ax2 - ax1) * (ay2 - ay1))
    area_b = max(1.0, (bx2 - bx1) * (by2 - by1))
    return inter_area / float(area_a + area_b - inter_area)


def build_detection(model_type, class_name, confidence, bbox, model_name):
    x1, y1, x2, y2 = bbox
    normalized_class_name = normalize_mojibake(str(class_name))
    normalized_model_name = normalize_mojibake(str(model_name)) if model_name else ""
    return {
        "modelType": model_type,
        "label": normalized_class_name,
        "displayLabel": f"{normalized_model_name}: {normalized_class_name}" if normalized_model_name else normalized_class_name,
        "confidence": round(float(confidence), 4),
        "boundingBox": f"x:{int(x1)},y:{int(y1)},w:{int(x2 - x1)},h:{int(y2 - y1)}",
        "processedAtUtc": datetime.now(timezone.utc).isoformat(),
    }


class StreamTracker:
    def __init__(self, iou_threshold=0.4):
        self.iou_threshold = iou_threshold
        self.entries = []
        self.next_id = 1

    def assign(self, detections):
        remaining_entries = list(self.entries)
        updated_entries = []
        for detection in detections:
            values = {}
            for part in detection["boundingBox"].split(","):
                key, raw = part.split(":", 1)
                values[key] = int(raw)

            current_box = (
                values.get("x", 0),
                values.get("y", 0),
                values.get("x", 0) + values.get("w", 1),
                values.get("y", 0) + values.get("h", 1),
            )
            normalized_label = str(detection.get("label", "")).strip().lower()

            best_index = -1
            best_iou = 0.0
            for index, entry in enumerate(remaining_entries):
                if entry["label"] != normalized_label:
                    continue
                current_iou = bbox_iou(entry["box"], current_box)
                if current_iou >= self.iou_threshold and current_iou > best_iou:
                    best_iou = current_iou
                    best_index = index

            if best_index >= 0:
                matched = remaining_entries.pop(best_index)
                track_id = matched["trackId"]
            else:
                track_id = f"T{self.next_id:04d}"
                self.next_id += 1

            detection["trackId"] = track_id
            updated_entries.append(
                {
                    "trackId": track_id,
                    "label": normalized_label,
                    "box": current_box,
                }
            )

        self.entries = updated_entries
        return detections


def resolve_device(device_mode):
    try:
        import torch

        gpu_available = bool(torch.cuda.is_available())
        normalized = (device_mode or "auto").strip().lower()
        if normalized in {"gpu", "cuda"}:
            return ("0" if gpu_available else "cpu"), gpu_available
        if normalized == "cpu":
            return "cpu", gpu_available
        return ("0" if gpu_available else "cpu"), gpu_available
    except Exception:
        return "cpu", False


def maybe_enable_half(use_half, resolved_device):
    return bool(use_half) and resolved_device != "cpu"


def resolve_detection_color(detection):
    normalized_label = str(detection.get("label", "")).strip().lower()
    if normalized_label == "person":
        return (255, 191, 0)
    if normalized_label == "cigarette":
        return (249, 115, 22)
    if normalized_label in {"un-occupied_desk", "empty-chair", "non-human", "empty_seat"}:
        return (16, 185, 129)

    model_type = str(detection.get("modelType", "")).strip()
    return (249, 115, 22) if model_type == "YoloSmoking" else (168, 85, 247)


def draw_detections(frame_bgr, detections, model_name, no_detection_text):
    if not detections:
        display_text = normalize_mojibake(no_detection_text) or "Đang giám sát..."
        cv2.rectangle(frame_bgr, (16, 16), (500, 46), (42, 23, 15), -1)
        cv2.putText(frame_bgr, display_text, (24, 36), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (255, 255, 255), 1, cv2.LINE_AA)
        return frame_bgr

    for detection in detections:
        values = {}
        for part in detection["boundingBox"].split(","):
            key, raw = part.split(":", 1)
            values[key] = int(raw)

        x1 = values.get("x", 0)
        y1 = values.get("y", 0)
        width = values.get("w", 1)
        height = values.get("h", 1)
        x2 = x1 + width
        y2 = y1 + height

        caption = f"{normalize_mojibake(detection['displayLabel'])} {float(detection['confidence']):.2f}"
        if detection.get("trackId"):
            caption = f"{caption} #{detection['trackId']}"
        (text_width, text_height), baseline = cv2.getTextSize(caption, cv2.FONT_HERSHEY_SIMPLEX, 0.48, 1)
        text_y = max(18, y1 - 8)
        accent = resolve_detection_color(detection)

        cv2.rectangle(frame_bgr, (x1, y1), (x2, y2), accent, 2)
        cv2.rectangle(
            frame_bgr,
            (x1, text_y - text_height - baseline - 6),
            (x1 + text_width + 10, text_y + 4),
            accent,
            -1,
        )
        cv2.putText(frame_bgr, caption, (x1 + 4, text_y - 4), cv2.FONT_HERSHEY_SIMPLEX, 0.48, (255, 255, 255), 1, cv2.LINE_AA)

    cv2.putText(frame_bgr, model_name or "YOLO Monitoring", (18, 24), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (255, 255, 255), 1, cv2.LINE_AA)
    return frame_bgr


class YoloWorker:
    def __init__(self, model_path, model_type, device_mode, use_half):
        self.model_path = model_path
        self.model_type = model_type
        self.device_mode = device_mode
        self.use_half = use_half
        self.model = None
        self.model_load_ms = 0
        self.resolved_device, self.gpu_available = resolve_device(device_mode)
        self.half = maybe_enable_half(use_half, self.resolved_device)
        self.tracker = StreamTracker()
        self._load_model()

    def _load_model(self):
        started_at = time.perf_counter()
        from ultralytics import YOLO

        self.model = YOLO(self.model_path)
        warmup_frame = np.zeros((320, 320, 3), dtype=np.uint8)
        self.model.predict(
            source=warmup_frame,
            conf=0.25,
            iou=0.45,
            imgsz=640,
            device=self.resolved_device,
            half=self.half,
            verbose=False,
            save=False,
        )
        self.model_load_ms = int((time.perf_counter() - started_at) * 1000)

    def run(self, payload):
        if payload.get("command") == "shutdown":
            return {"status": "bye"}

        source_path = payload.get("sourcePath", "")
        conf = float(payload.get("conf", 0.25))
        iou = float(payload.get("iou", 0.45))
        label = payload.get("label", "")
        max_frames = max(1, int(payload.get("maxFrames", 8)))
        image_size = max(320, int(payload.get("imageSize", 640)))
        model_type = payload.get("modelType") or self.model_type

        if not str(source_path).lower().startswith("camera:") and not Path(source_path).exists():
            return {
                "detections": [],
                "annotatedBase64": None,
                "imageMimeType": "image/jpeg",
                "frameIndex": 0,
                "framesExamined": 0,
                "elapsedMs": 0,
                "modelLoadMs": self.model_load_ms,
                "isMock": True,
                "gpuAvailable": self.gpu_available,
                "modelLoadedFromCache": True,
                "deviceRequested": self.device_mode,
                "deviceResolved": self.resolved_device,
                "errorMessage": f"Không tìm thấy nguồn input: {source_path}",
            }

        started_at = time.perf_counter()
        fallback_payload = None
        best_payload = None
        frames_examined = 0

        for frame_index, frame in iter_candidate_frames(source_path, max_frames):
            frames_examined += 1
            results = self.model.predict(
                source=frame,
                conf=conf,
                iou=iou,
                imgsz=image_size,
                device=self.resolved_device,
                half=self.half,
                verbose=False,
                save=False,
            )
            result = results[0] if results else None
            detections = []

            if result is not None:
                for box in result.boxes:
                    x1, y1, x2, y2 = box.xyxy[0].tolist()
                    cls = int(box.cls[0])
                    class_name = result.names[cls]

                    detections.append(
                        build_detection(
                            model_type,
                            class_name,
                            float(box.conf[0]),
                            (x1, y1, x2, y2),
                            label,
                        )
                    )

            detections = self.tracker.assign(detections)

            annotated_frame = draw_detections(
                frame.copy(),
                detections,
                model_type,
                "Đang giám sát...",
            )

            candidate = {
                "detections": detections,
                "annotatedBase64": encode_jpeg_base64(annotated_frame) if annotated_frame is not None else None,
                "imageMimeType": "image/jpeg",
                "frameIndex": frame_index,
                "framesExamined": frames_examined,
                "elapsedMs": 0,
                "modelLoadMs": self.model_load_ms,
                "isMock": False,
                "gpuAvailable": self.gpu_available,
                "modelLoadedFromCache": True,
                "deviceRequested": self.device_mode,
                "deviceResolved": self.resolved_device,
                "errorMessage": None,
            }

            if fallback_payload is None:
                fallback_payload = candidate

            if best_payload is None:
                best_payload = candidate
                continue

            current_score = (
                len(candidate["detections"]),
                sum(item["confidence"] for item in candidate["detections"]),
            )
            best_score = (
                len(best_payload["detections"]),
                sum(item["confidence"] for item in best_payload["detections"]),
            )

            if current_score > best_score:
                best_payload = candidate

        result_payload = best_payload or fallback_payload
        if result_payload is None:
            return {
                "detections": [],
                "annotatedBase64": None,
                "imageMimeType": "image/jpeg",
                "frameIndex": 0,
                "framesExamined": 0,
                "elapsedMs": 0,
                "modelLoadMs": self.model_load_ms,
                "isMock": True,
                "gpuAvailable": self.gpu_available,
                "modelLoadedFromCache": True,
                "deviceRequested": self.device_mode,
                "deviceResolved": self.resolved_device,
                "errorMessage": "Không đọc được frame nào từ video hoặc ảnh.",
            }

        result_payload["framesExamined"] = frames_examined
        result_payload["elapsedMs"] = int((time.perf_counter() - started_at) * 1000)
        return result_payload


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", required=True)
    parser.add_argument("--model-type", default="YoloSmoking")
    parser.add_argument("--device", default="auto")
    parser.add_argument("--half", type=int, default=1)
    args = parser.parse_args()

    worker = YoloWorker(args.model, args.model_type, args.device, bool(args.half))
    print(
        json.dumps(
            {
                "status": "ready",
                "modelPath": args.model,
                "modelType": args.model_type,
                "deviceRequested": args.device,
                "deviceResolved": worker.resolved_device,
                "gpuAvailable": worker.gpu_available,
                "modelLoadMs": worker.model_load_ms,
            }
        ),
        flush=True,
    )

    for line in sys.stdin:
        raw = line.strip()
        if not raw:
            continue

        try:
            payload = json.loads(raw)
            response = worker.run(payload)
            print(json.dumps(response), flush=True)
            if response.get("status") == "bye":
                break
        except Exception as ex:
            error_payload = {
                "detections": [],
                "annotatedBase64": None,
                "imageMimeType": "image/jpeg",
                "frameIndex": 0,
                "framesExamined": 0,
                "elapsedMs": 0,
                "modelLoadMs": worker.model_load_ms,
                "isMock": True,
                "gpuAvailable": worker.gpu_available,
                "modelLoadedFromCache": True,
                "deviceRequested": worker.device_mode,
                "deviceResolved": worker.resolved_device,
                "errorMessage": str(ex),
            }
            print(json.dumps(error_payload), flush=True)


if __name__ == "__main__":
    main()
