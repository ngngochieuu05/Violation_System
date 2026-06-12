import cv2
import json
import threading
from flask import Flask, Response, jsonify
from ultralytics import YOLO
import argparse

app = Flask(__name__)
model = None
camera = None
latest_detections = []
lock = threading.Lock()

def init_model(model_path):
    global model
    model = YOLO(model_path)

def generate_frames(source):
    global camera, latest_detections
    camera = cv2.VideoCapture(source)
    
    while True:
        success, frame = camera.read()
        if not success:
            # Loop video
            camera.set(cv2.CAP_PROP_POS_FRAMES, 0)
            continue
            
        results = model(frame, verbose=False)
        
        detections = []
        for r in results:
            boxes = r.boxes
            for box in boxes:
                x1, y1, x2, y2 = box.xyxy[0].tolist()
                conf = float(box.conf[0])
                cls = int(box.cls[0])
                custom_label = app.config.get('LABEL')
                label = custom_label if custom_label else model.names[cls]
                
                # Draw box
                cv2.rectangle(frame, (int(x1), int(y1)), (int(x2), int(y2)), (0, 0, 255), 2)
                cv2.putText(frame, f"{label} {conf:.2f}", (int(x1), int(y1) - 10), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 0, 255), 2)
                
                detections.append({
                    "label": label,
                    "confidence": conf,
                    "boundingBox": f"x:{int(x1)},y:{int(y1)},w:{int(x2-x1)},h:{int(y2-y1)}"
                })
        
        with lock:
            latest_detections = detections

        ret, buffer = cv2.imencode('.jpg', frame)
        frame_bytes = buffer.tobytes()
        yield (b'--frame\r\n'
               b'Content-Type: image/jpeg\r\n\r\n' + frame_bytes + b'\r\n')

@app.route('/video_feed')
def video_feed():
    # Allow CORS if needed
    response = Response(generate_frames(app.config['SOURCE']), mimetype='multipart/x-mixed-replace; boundary=frame')
    response.headers['Access-Control-Allow-Origin'] = '*'
    return response

@app.route('/detections')
def get_detections():
    with lock:
        response = jsonify(latest_detections)
        response.headers['Access-Control-Allow-Origin'] = '*'
        return response

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", required=True)
    parser.add_argument("--source", required=True)
    parser.add_argument("--port", type=int, default=5000)
    parser.add_argument("--label", type=str, default="")
    args = parser.parse_args()
    
    init_model(args.model)
    app.config['SOURCE'] = int(args.source) if args.source.isdigit() else args.source
    app.config['LABEL'] = args.label
    
    app.run(host='0.0.0.0', port=args.port, threaded=True)
