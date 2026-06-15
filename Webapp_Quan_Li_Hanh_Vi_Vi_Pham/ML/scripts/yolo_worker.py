import argparse
import base64
import io
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
from PIL import Image, ImageDraw, ImageFont


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


def build_detection(model_type, class_name, confidence, bbox, model_name):
    x1, y1, x2, y2 = bbox
    return {
        "modelType": model_type,
        "label": class_name,
        "displayLabel": f"{model_name}: {class_name}" if model_name else class_name,
        "confidence": round(float(confidence), 4),
        "boundingBox": f"x:{int(x1)},y:{int(y1)},w:{int(x2 - x1)},h:{int(y2 - y1)}",
        "processedAtUtc": datetime.now(timezone.utc).isoformat(),
    }


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


def draw_detections(frame_bgr, detections, model_name, no_detection_text):
    rgb_frame = cv2.cvtColor(frame_bgr, cv2.COLOR_BGR2RGB)
    image = Image.fromarray(rgb_frame)
    draw = ImageDraw.Draw(image)
    font = ImageFont.load_default()

    accent = (249, 115, 22) if any(item["modelType"] == "YoloSmoking" for item in detections) else (16, 185, 129)

    if not detections:
        draw.rectangle((16, 16, 500, 46), fill=(15, 23, 42))
        draw.text((24, 22), no_detection_text, fill=(255, 255, 255), font=font)
        return cv2.cvtColor(np.array(image), cv2.COLOR_RGB2BGR)

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

        caption = f"{detection['displayLabel']} {float(detection['confidence']):.2f}"
        text_bbox = draw.textbbox((0, 0), caption, font=font)
        text_width = text_bbox[2] - text_bbox[0]
        text_height = text_bbox[3] - text_bbox[1]
        text_y = max(4, y1 - text_height - 10)

        draw.rectangle((x1, y1, x2, y2), outline=accent, width=3)
        draw.rectangle((x1, text_y, x1 + text_width + 12, text_y + text_height + 8), fill=accent)
        draw.text((x1 + 6, text_y + 4), caption, fill=(255, 255, 255), font=font)

    draw.text((18, 18), model_name or "YOLO Monitoring", fill=(255, 255, 255), font=font)
    return cv2.cvtColor(np.array(image), cv2.COLOR_RGB2BGR)


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
