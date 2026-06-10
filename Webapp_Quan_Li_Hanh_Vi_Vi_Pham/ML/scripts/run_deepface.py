import argparse
import json
import os
import sys

DEFAULT_MODEL_NAME = "ArcFace"
DEFAULT_DETECTOR_BACKEND = "opencv"
DEFAULT_ALIGN = True
DEFAULT_ENFORCE_DETECTION = True
FALLBACK_DETECTOR_BACKENDS = ["retinaface", "mtcnn"]


def parse_bool(value):
    if isinstance(value, bool):
        return value
    return str(value).strip().lower() in {"1", "true", "yes", "on"}


def fail(message, exit_code=1):
    print(json.dumps({"success": False, "error": message}))
    sys.exit(exit_code)


def get_detector_candidates(primary_backend):
    candidates = [primary_backend]
    for backend in FALLBACK_DETECTOR_BACKENDS:
        if backend not in candidates:
            candidates.append(backend)
    return candidates


def extract_single_embedding(deepface_cls, image_path, model_name, detector_backend, enforce_detection, align):
    last_error = None
    for backend in get_detector_candidates(detector_backend):
        try:
            result = deepface_cls.represent(
                img_path=image_path,
                model_name=model_name,
                detector_backend=backend,
                enforce_detection=enforce_detection,
                align=align
            )
            if len(result) != 1:
                raise ValueError(f"Anh phai co dung 1 khuon mat (Phat hien {len(result)})")
            return result[0]["embedding"]
        except Exception as exc:
            last_error = exc
            continue

    raise last_error or ValueError("Face could not be detected")


def detect_single_face(deepface_cls, image_path, model_name, detector_backend, enforce_detection, align):
    last_error = None
    for backend in get_detector_candidates(detector_backend):
        try:
            result = deepface_cls.represent(
                img_path=image_path,
                model_name=model_name,
                detector_backend=backend,
                enforce_detection=enforce_detection,
                align=align
            )
            if len(result) != 1:
                raise ValueError(f"Anh phai co dung 1 khuon mat (Phat hien {len(result)})")
            return True
        except Exception as exc:
            last_error = exc
            continue

    raise last_error or ValueError("Face could not be detected")


def verify_faces(deepface_cls, img1_path, img2_path, model_name, detector_backend, enforce_detection, align):
    last_error = None
    for backend in get_detector_candidates(detector_backend):
        try:
            return deepface_cls.verify(
                img1_path=img1_path,
                img2_path=img2_path,
                model_name=model_name,
                detector_backend=backend,
                distance_metric="cosine",
                enforce_detection=enforce_detection,
                align=align
            )
        except Exception as exc:
            last_error = exc
            continue

    raise last_error or ValueError("Face verification failed")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--action", required=True, choices=["detect", "verify", "represent", "represent_batch"])
    parser.add_argument("--image", help="Path to image for face detection or embedding extraction")
    parser.add_argument("--img1", help="Path to first image for face verification")
    parser.add_argument("--img2", help="Path to second image for face verification")
    parser.add_argument("--images-file", help="Path to a text file containing image paths, one per line")
    parser.add_argument("--threshold", type=float, default=0.75, help="Distance threshold (confidence)")
    parser.add_argument("--model-name", default=DEFAULT_MODEL_NAME)
    parser.add_argument("--detector-backend", default=DEFAULT_DETECTOR_BACKEND)
    parser.add_argument("--align", default=str(DEFAULT_ALIGN).lower())
    parser.add_argument("--enforce-detection", default=str(DEFAULT_ENFORCE_DETECTION).lower())
    args = parser.parse_args()

    model_name = args.model_name or DEFAULT_MODEL_NAME
    detector_backend = args.detector_backend or DEFAULT_DETECTOR_BACKEND
    align = parse_bool(args.align)
    enforce_detection = parse_bool(args.enforce_detection)

    try:
        from deepface import DeepFace
    except ImportError:
        fail("DeepFace is not installed on the host. Biometric verification is disabled.")

    if args.action == "detect":
        if not args.image:
            fail("Missing --image parameter")
        if not os.path.exists(args.image):
            fail(f"Image path does not exist: {args.image}")

        try:
            detect_single_face(
                DeepFace,
                args.image,
                model_name,
                detector_backend,
                enforce_detection,
                align
            )
            print(json.dumps({
                "success": True,
                "faces_detected": 1,
                "message": "Exactly 1 face detected successfully using DeepFace"
            }))
        except Exception as exc:
            fail(str(exc))

    elif args.action == "represent":
        if not args.image:
            fail("Missing --image parameter")
        if not os.path.exists(args.image):
            fail(f"Image path does not exist: {args.image}")

        try:
            embedding = extract_single_embedding(
                DeepFace,
                args.image,
                model_name,
                detector_backend,
                enforce_detection,
                align
            )
            print(json.dumps({
                "success": True,
                "embedding": embedding,
                "message": "Embedding extracted successfully using DeepFace represent"
            }))
        except Exception as exc:
            fail(str(exc))

    elif args.action == "represent_batch":
        if not args.images_file:
            fail("Missing --images-file parameter")
        if not os.path.exists(args.images_file):
            fail(f"Images file does not exist: {args.images_file}")

        with open(args.images_file, "r", encoding="utf-8") as handle:
            image_paths = [line.strip() for line in handle if line.strip()]

        if not image_paths:
            fail("Images file is empty")

        results = []
        try:
            for image_path in image_paths:
                if not os.path.exists(image_path):
                    raise FileNotFoundError(f"Image path does not exist: {image_path}")
                embedding = extract_single_embedding(
                    DeepFace,
                    image_path,
                    model_name,
                    detector_backend,
                    enforce_detection,
                    align
                )
                results.append({"success": True, "embedding": embedding})

            print(json.dumps({
                "success": True,
                "results": results,
                "message": "Batch embeddings extracted successfully using DeepFace"
            }))
        except Exception as exc:
            fail(str(exc))

    elif args.action == "verify":
        if not args.img1 or not args.img2:
            fail("Missing --img1 or --img2 parameters")
        if not os.path.exists(args.img1) or not os.path.exists(args.img2):
            fail("One or both images do not exist")

        try:
            result = verify_faces(
                DeepFace,
                args.img1,
                args.img2,
                model_name,
                detector_backend,
                enforce_detection,
                align
            )
            print(json.dumps({
                "success": True,
                "verified": result.get("verified", False),
                "distance": result.get("distance", 1.0),
                "threshold": result.get("threshold", args.threshold),
                "message": "Verification completed with DeepFace"
            }))
        except Exception as exc:
            fail(str(exc))


if __name__ == "__main__":
    main()
