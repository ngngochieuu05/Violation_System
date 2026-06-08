# ML assets

Thu muc nay dung de chua cac file lien quan den mo hinh YOLO chay cuc bo:

- `weights/`: file weight nhu `.pt`, `.onnx`
- `samples/`: anh hoac video mau de test suy luan
- `datasets/`: metadata hoac script tham chieu dataset public
- `scripts/`: script Python chay YOLO voi file `.pt`

Trong kien truc hien tai, MVC C# khong goi HTTP API de nhan dien.
Web app se goi local inference service, service nay co the:

- chay script Python su dung `ultralytics` + file `.pt`
- hoac sau nay doi sang `.onnx` neu muon suy luan thuần .NET

Khuyen nghi khong commit file weight lon vao git chinh.
