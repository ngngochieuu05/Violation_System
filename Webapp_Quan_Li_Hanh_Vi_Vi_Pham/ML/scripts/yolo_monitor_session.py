import argparse
import base64
import json
import ssl
import sys
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')
if hasattr(sys.stderr, 'reconfigure'):
    sys.stderr.reconfigure(encoding='utf-8')

import time
import urllib.error
import urllib.request
from datetime import datetime, timezone
from pathlib import Path

import cv2
import numpy as np


def utc_now_iso():
    return datetime.now(timezone.utc).isoformat()


def utc_now_unix_ms():
    return int(time.time() * 1000)


def resize_frame(frame, max_side=1280):
    height, width = frame.shape[:2]
    longest_side = max(height, width)
    if longest_side <= max_side:
        return frame
    scale = max_side / float(longest_side)
    return cv2.resize(
        frame,
        (max(1, int(width * scale)), max(1, int(height * scale))),
        interpolation=cv2.INTER_AREA,
    )


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


def bbox_union(box_a, box_b):
    return [
        min(box_a[0], box_b[0]),
        min(box_a[1], box_b[1]),
        max(box_a[2], box_b[2]),
        max(box_a[3], box_b[3]),
    ]


def bbox_center(box):
    return ((box[0] + box[2]) / 2.0, (box[1] + box[3]) / 2.0)


def center_inside(box, point):
    return box[0] <= point[0] <= box[2] and box[1] <= point[1] <= box[3]


def expand_box(box, frame_shape, padding=16):
    height, width = frame_shape[:2]
    x1 = max(0, int(box[0] - padding))
    y1 = max(0, int(box[1] - padding))
    x2 = min(width, int(box[2] + padding))
    y2 = min(height, int(box[3] + padding))
    return [x1, y1, x2, y2]


def crop_snapshot(frame, box):
    x1, y1, x2, y2 = expand_box(box, frame.shape)
    crop = frame[y1:y2, x1:x2]
    if crop.size == 0:
        crop = frame
    ok, encoded = cv2.imencode(".jpg", crop, [int(cv2.IMWRITE_JPEG_QUALITY), 88])
    if not ok:
        return "", "image/jpeg"
    return base64.b64encode(encoded.tobytes()).decode("ascii"), "image/jpeg"


def normalize_mojibake(value):
    if not isinstance(value, str) or not value:
        return value

    try:
        repaired = value.encode("latin1", errors="ignore").decode("utf-8", errors="ignore")
        return repaired or value
    except Exception:
        return value


def draw_detection(frame, detection, color):
    x1, y1, x2, y2 = detection["xyxy"]
    caption = f'{normalize_mojibake(detection["displayLabel"])} {detection["confidence"]:.2f}'
    if detection.get("trackId"):
        caption = f'{caption} #{detection["trackId"]}'
    cv2.rectangle(frame, (x1, y1), (x2, y2), color, 2)
    (text_width, text_height), baseline = cv2.getTextSize(caption, cv2.FONT_HERSHEY_SIMPLEX, 0.48, 1)
    text_y = max(18, y1 - 8)
    cv2.rectangle(
        frame,
        (x1, text_y - text_height - baseline - 6),
        (x1 + text_width + 10, text_y + 4),
        color,
        -1,
    )
    cv2.putText(frame, caption, (x1 + 4, text_y - 4), cv2.FONT_HERSHEY_SIMPLEX, 0.48, (255, 255, 255), 1, cv2.LINE_AA)


class StreamTracker:
    def __init__(self, iou_threshold=0.4):
        self.iou_threshold = iou_threshold
        self.trackers = {}
        self.next_ids = {}

    def assign(self, model_type, detections):
        entries = self.trackers.setdefault(model_type, [])
        next_id = self.next_ids.setdefault(model_type, 1)
        updated_entries = []

        for detection in detections:
            best_index = -1
            best_iou = 0.0
            for idx, entry in enumerate(entries):
                if entry["label"].lower() != detection["label"].lower():
                    continue
                current_iou = bbox_iou(entry["xyxy"], detection["xyxy"])
                if current_iou > self.iou_threshold and current_iou > best_iou:
                    best_iou = current_iou
                    best_index = idx

            if best_index >= 0:
                matched = entries.pop(best_index)
                detection["trackId"] = matched["trackId"]
            else:
                detection["trackId"] = f"{model_type}-{next_id:04d}"
                next_id += 1

            updated_entries.append(
                {
                    "trackId": detection["trackId"],
                    "label": detection["label"],
                    "xyxy": detection["xyxy"],
                }
            )

        self.trackers[model_type] = updated_entries
        self.next_ids[model_type] = next_id
        return detections


class InstantAlertPublisher:
    def __init__(self, callback_url, callback_key):
        self.callback_url = (callback_url or "").strip()
        self.callback_key = callback_key or ""
        self.ssl_context = ssl._create_unverified_context()

    def send(self, payload):
        if not self.callback_url:
            return False, "callback_url_missing"

        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        request = urllib.request.Request(
            self.callback_url,
            data=body,
            headers={
                "Content-Type": "application/json; charset=utf-8",
                "X-Monitoring-Key": self.callback_key,
            },
            method="POST",
        )

        try:
            with urllib.request.urlopen(request, timeout=10, context=self.ssl_context) as response:
                response_body = response.read().decode("utf-8", errors="ignore")
                return 200 <= response.status < 300, response_body[:240]
        except urllib.error.HTTPError as exc:
            message = exc.read().decode("utf-8", errors="ignore")
            return False, f"http_{exc.code}:{message[:240]}"
        except Exception as exc:
            return False, str(exc)


class RuleStateTracker:
    def __init__(self):
        self.states = {}

    def begin_or_update(self, rule_key, root_track_id, object_box, frame_index, now_ts):
        state = self.states.get(rule_key)
        if state is None:
            state = {
                "rootTrackId": root_track_id,
                "firstSeenTs": now_ts,
                "lastSeenTs": now_ts,
                "frameStart": frame_index,
                "frameLast": frame_index,
                "objectBox": object_box,
                "alertSent": False,
            }
            self.states[rule_key] = state
        else:
            state["lastSeenTs"] = now_ts
            state["frameLast"] = frame_index
            state["objectBox"] = object_box
        return state

    def prune_missing(self, active_rule_keys, now_ts, stale_seconds):
        active_rule_keys = set(active_rule_keys)
        for key in list(self.states.keys()):
            if key in active_rule_keys:
                continue

            state = self.states.get(key)
            if state is None:
                continue

            if now_ts - state["lastSeenTs"] > stale_seconds:
                self.states.pop(key, None)


class MonitoringSessionWorker:
    def __init__(
        self,
        session_dir,
        models,
        source,
        source_type,
        device,
        half,
        imgsz,
        fps,
        callback_url,
        callback_key,
        camera_location,
        person_label,
        smoke_label,
        empty_desk_label,
        smoke_seconds,
        empty_desk_seconds,
    ):
        from ultralytics import YOLO
        import torch

        self.session_dir = Path(session_dir)
        self.session_dir.mkdir(parents=True, exist_ok=True)
        self.models = []
        self.source = source
        self.source_type = source_type
        self.device_requested = device
        self.device_resolved = self._resolve_device(device)
        self.gpu_available = bool(torch.cuda.is_available())
        self.half = bool(half) and self.device_resolved != "cpu"
        self.imgsz = imgsz
        self.frame_delay = max(0.04, 1.0 / max(1, fps))
        self.tracker = StreamTracker()
        self.rule_tracker = RuleStateTracker()
        self.publisher = InstantAlertPublisher(callback_url, callback_key)
        self.camera_location = camera_location
        self.person_label = (person_label or "person").strip().lower()
        self.smoke_label = (smoke_label or "Cigarette").strip().lower()
        self.empty_desk_label = (empty_desk_label or "un-occupied_desk").strip().lower()
        self.smoke_seconds = max(0.1, float(smoke_seconds))
        self.empty_desk_seconds = max(0.1, float(empty_desk_seconds))
        self.capture = None
        self.current_frame_index = -1
        self.static_image = None
        self.last_alert_ts = {}

        for model in models:
            raw_conf = float(model.get("ConfThreshold", 0.25))
            raw_iou = float(model.get("IouThreshold", 0.45))
            if raw_conf > 1.0:
                raw_conf = raw_conf / 100.0
            if raw_iou > 1.0:
                raw_iou = raw_iou / 100.0
            safe_conf = max(0.01, min(0.99, raw_conf))
            safe_iou = max(0.01, min(0.99, raw_iou))

            loaded = YOLO(model["ModelPath"])
            warmup = np.zeros((320, 320, 3), dtype=np.uint8)
            loaded.predict(
                source=warmup,
                conf=safe_conf,
                iou=safe_iou,
                imgsz=self.imgsz,
                device=self.device_resolved,
                half=self.half,
                verbose=False,
                save=False,
            )
            self.models.append(
                {
                    "model": loaded,
                    "modelType": model["Type"],
                    "modelName": model["Name"],
                    "modelPath": model["ModelPath"],
                    "conf": safe_conf,
                    "iou": safe_iou,
                }
            )

    def _resolve_device(self, device):
        try:
            import torch

            gpu_available = bool(torch.cuda.is_available())
            normalized = (device or "auto").strip().lower()
            if normalized in {"gpu", "cuda", "0", "auto"}:
                return "0" if gpu_available else "cpu"
            return "cpu"
        except Exception:
            return "cpu"

    def _open_source(self):
        if self.source_type == "image":
            self.static_image = cv2.imread(self.source)
            return

        if self.source.startswith("camera:"):
            camera_index = int(self.source.split(":", 1)[1])
            self.capture = cv2.VideoCapture(camera_index, cv2.CAP_DSHOW)
            if not self.capture.isOpened():
                self.capture = cv2.VideoCapture(camera_index)
            return

        self.capture = cv2.VideoCapture(self.source)

    def _read_frame(self):
        if self.source_type == "image":
            self.current_frame_index += 1
            return resize_frame(self.static_image.copy()) if self.static_image is not None else None

        if self.capture is None:
            return None

        ok, frame = self.capture.read()
        if ok and frame is not None:
            self.current_frame_index += 1
            return resize_frame(frame)

        if self.source_type == "video":
            self.capture.set(cv2.CAP_PROP_POS_FRAMES, 0)
            ok, frame = self.capture.read()
            if ok and frame is not None:
                self.current_frame_index = 0
                return resize_frame(frame)

        return None

    def _write_status(self, payload):
        tmp_path = self.session_dir / "status.tmp"
        status_path = self.session_dir / "status.json"
        tmp_path.write_text(json.dumps(payload, ensure_ascii=False), encoding="utf-8")
        tmp_path.replace(status_path)

    def _associate_with_person(self, target_detection, person_detections):
        target_box = target_detection["xyxy"]
        target_center = bbox_center(target_box)
        best_person = None
        best_score = 0.0

        for person in person_detections:
            person_box = person["xyxy"]
            score = bbox_iou(target_box, person_box)
            if center_inside(person_box, target_center):
                score = max(score, 0.6)
            if score > best_score:
                best_person = person
                best_score = score

        return best_person

    def _handle_instant_rules(self, frame, all_detections, annotated_frames):
        now_ts = time.time()
        active_rule_keys = []
        person_detections = [
            detection for detection in all_detections if detection["label"].strip().lower() == self.person_label
        ]

        for detection in all_detections:
            normalized_label = detection["label"].strip().lower()
            if normalized_label not in {self.smoke_label, self.empty_desk_label}:
                continue

            associated_person = self._associate_with_person(detection, person_detections)
            root_track_id = (
                associated_person.get("trackId")
                if associated_person is not None and associated_person.get("trackId")
                else detection.get("trackId")
            ) or f'{detection["modelType"]}-root-{self.current_frame_index}'

            root_box = associated_person["xyxy"] if associated_person is not None else detection["xyxy"]
            snapshot_box = bbox_union(root_box, detection["xyxy"])
            rule_type = "smoke" if normalized_label == self.smoke_label else "empty_desk"
            threshold_seconds = self.smoke_seconds if rule_type == "smoke" else self.empty_desk_seconds
            rule_key = f"{rule_type}:{root_track_id}"
            active_rule_keys.append(rule_key)

            state = self.rule_tracker.begin_or_update(
                rule_key=rule_key,
                root_track_id=root_track_id,
                object_box=snapshot_box,
                frame_index=self.current_frame_index,
                now_ts=now_ts,
            )

            duration_seconds = max(0.0, now_ts - state["firstSeenTs"])
            if state["alertSent"] or duration_seconds < threshold_seconds:
                continue

            # Áp dụng thời gian chờ (cooldown) 5 giây giữa các lần gửi cho cùng một loại vi phạm
            if now_ts - self.last_alert_ts.get(rule_type, 0.0) < 5.0:
                continue

            annotated_frame = annotated_frames.get(detection["modelType"]) or frame
            snapshot_base64, snapshot_mime = crop_snapshot(annotated_frame, state["objectBox"])
            payload = {
                "ruleType": rule_type,
                "modelType": detection["modelType"],
                "label": detection["label"],
                "trackId": root_track_id,
                "cameraLocation": self.camera_location,
                "sourceType": self.source_type,
                "sourceLabel": self.source,
                "boundingBox": f'x:{snapshot_box[0]},y:{snapshot_box[1]},w:{snapshot_box[2] - snapshot_box[0]},h:{snapshot_box[3] - snapshot_box[1]}',
                "durationSeconds": round(duration_seconds, 2),
                "detectedAtUtc": utc_now_iso(),
                "snapshotBase64": snapshot_base64,
                "snapshotMimeType": snapshot_mime,
            }
            success, summary = self.publisher.send(payload)
            if success:
                state["alertSent"] = True
                self.last_alert_ts[rule_type] = now_ts
                print(
                    json.dumps(
                        {
                            "type": "instant-alert",
                            "ruleType": rule_type,
                            "trackId": root_track_id,
                            "durationSeconds": round(duration_seconds, 2),
                            "result": summary,
                        },
                        ensure_ascii=False,
                    ),
                    flush=True,
                )
            else:
                print(
                    json.dumps(
                        {
                            "type": "instant-alert-error",
                            "ruleType": rule_type,
                            "trackId": root_track_id,
                            "durationSeconds": round(duration_seconds, 2),
                            "error": summary,
                        },
                        ensure_ascii=False,
                    ),
                    flush=True,
                )

        # Empty-desk detection often flickers for a few frames. Keep the timer alive
        # briefly so the "rời vị trí" rule is not reset by a single missed frame.
        stale_seconds = max(1.5, self.empty_desk_seconds * 0.5, self.smoke_seconds * 0.5)
        self.rule_tracker.prune_missing(active_rule_keys, now_ts, stale_seconds)

    def _resolve_detection_color(self, detection):
        normalized_label = detection["label"].strip().lower()
        if normalized_label == self.person_label:
            return (255, 191, 0)
        if normalized_label == self.smoke_label:
            return (0, 102, 255)
        if normalized_label == self.empty_desk_label:
            return (0, 200, 120)

        model_type = detection.get("modelType", "")
        return (0, 140, 255) if model_type == "YoloSmoking" else (180, 105, 255)

    def run(self):
        self._open_source()
        self._write_status(
            {
                "state": "starting",
                "message": "Đang mở nguồn vào và tải model...",
                "sourceLabel": self.source,
                "gpuAvailable": self.gpu_available,
                "deviceResolved": self.device_resolved,
                "frameIndex": 0,
                "updatedAtUnixMs": utc_now_unix_ms(),
                "primaryModelType": None,
                "models": [],
            }
        )

        while True:
            started = time.perf_counter()
            frame = self._read_frame()
            if frame is None:
                self._write_status(
                    {
                        "state": "error",
                        "message": "Không đọc được frame từ nguồn vào.",
                        "sourceLabel": self.source,
                        "gpuAvailable": self.gpu_available,
                        "deviceResolved": self.device_resolved,
                        "frameIndex": self.current_frame_index,
                        "updatedAtUnixMs": utc_now_unix_ms(),
                        "primaryModelType": None,
                        "models": [],
                    }
                )
                time.sleep(0.5)
                continue

            model_outputs = []
            all_detections = []
            annotated_frames = {}
            primary_model_type = None
            primary_score = -1

            for model_entry in self.models:
                inference_started = time.perf_counter()
                results = model_entry["model"].predict(
                    source=frame,
                    conf=model_entry["conf"],
                    iou=model_entry["iou"],
                    imgsz=self.imgsz,
                    device=self.device_resolved,
                    half=self.half,
                    verbose=False,
                    save=False,
                )
                result = results[0] if results else None
                detections = []

                if result is not None:
                    names = result.names if result.names is not None else {}
                    for box in result.boxes:
                        x1, y1, x2, y2 = [int(value) for value in box.xyxy[0].tolist()]
                        cls = int(box.cls[0])
                        label = names[cls] if isinstance(names, list) else names.get(cls, str(cls))
                        normalized_label = normalize_mojibake(str(label))
                        normalized_model_name = normalize_mojibake(str(model_entry["modelName"]))
                        detections.append(
                            {
                                "modelType": model_entry["modelType"],
                                "label": normalized_label,
                                "displayLabel": f'{normalized_model_name}: {normalized_label}',
                                "confidence": round(float(box.conf[0]), 4),
                                "xyxy": [x1, y1, x2, y2],
                                "boundingBox": f"x:{x1},y:{y1},w:{x2 - x1},h:{y2 - y1}",
                                "processedAtUtc": utc_now_iso(),
                            }
                        )

                detections = self.tracker.assign(model_entry["modelType"], detections)
                all_detections.extend(detections)
                annotated = frame.copy()
                for detection in detections:
                    draw_detection(annotated, detection, self._resolve_detection_color(detection))
                annotated_frames[model_entry["modelType"]] = annotated.copy()

                output_name = f'{model_entry["modelType"].lower()}.jpg'
                output_path = self.session_dir / output_name
                cv2.imwrite(str(output_path), annotated, [int(cv2.IMWRITE_JPEG_QUALITY), 82])
                elapsed_ms = int((time.perf_counter() - inference_started) * 1000)

                model_outputs.append(
                    {
                        "modelType": model_entry["modelType"],
                        "modelName": model_entry["modelName"],
                        "modelPath": model_entry["modelPath"],
                        "confThreshold": model_entry["conf"],
                        "iouThreshold": model_entry["iou"],
                        "isMockResult": False,
                        "elapsedMilliseconds": elapsed_ms,
                        "imageFileName": output_name,
                        "detectionCount": len(detections),
                        "detections": [
                            {
                                "label": detection["label"],
                                "displayLabel": detection["displayLabel"],
                                "confidence": detection["confidence"],
                                "boundingBox": detection["boundingBox"],
                                "trackId": detection.get("trackId"),
                                "processedAtUtc": detection["processedAtUtc"],
                            }
                            for detection in detections
                        ],
                    }
                )

                score = (len(detections) * 1000) + elapsed_ms
                if primary_model_type is None or score > primary_score:
                    primary_model_type = model_entry["modelType"]
                    primary_score = score

            self._handle_instant_rules(frame, all_detections, annotated_frames)

            self._write_status(
                {
                    "state": "running",
                    "message": "Phiên giám sát đang chạy liên tục như luồng kiểm thử Python.",
                    "sourceLabel": self.source,
                    "gpuAvailable": self.gpu_available,
                    "deviceResolved": self.device_resolved,
                    "frameIndex": self.current_frame_index,
                    "updatedAtUnixMs": utc_now_unix_ms(),
                    "primaryModelType": primary_model_type,
                    "models": model_outputs,
                }
            )

            elapsed = time.perf_counter() - started
            if elapsed < self.frame_delay:
                time.sleep(self.frame_delay - elapsed)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--session-dir", required=True)
    parser.add_argument("--models-config", required=True)
    parser.add_argument("--source", required=True)
    parser.add_argument("--source-type", required=True)
    parser.add_argument("--device", default="auto")
    parser.add_argument("--half", default="1")
    parser.add_argument("--imgsz", type=int, default=640)
    parser.add_argument("--fps", type=int, default=6)
    parser.add_argument("--callback-url", default="")
    parser.add_argument("--callback-key", default="")
    parser.add_argument("--camera-location", default="Camera giám sát mặc định")
    parser.add_argument("--person-label", default="person")
    parser.add_argument("--smoke-label", default="Cigarette")
    parser.add_argument("--empty-desk-label", default="un-occupied_desk")
    parser.add_argument("--smoke-seconds", type=float, default=1.5)
    parser.add_argument("--empty-desk-seconds", type=float, default=3.0)
    args = parser.parse_args()

    models = json.loads(Path(args.models_config).read_text(encoding="utf-8-sig"))
    worker = MonitoringSessionWorker(
        session_dir=args.session_dir,
        models=models,
        source=args.source,
        source_type=args.source_type,
        device=args.device,
        half=args.half == "1",
        imgsz=max(320, args.imgsz),
        fps=max(1, args.fps),
        callback_url=args.callback_url,
        callback_key=args.callback_key,
        camera_location=args.camera_location,
        person_label=args.person_label,
        smoke_label=args.smoke_label,
        empty_desk_label=args.empty_desk_label,
        smoke_seconds=args.smoke_seconds,
        empty_desk_seconds=args.empty_desk_seconds,
    )
    worker.run()


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        sys.exit(0)
