let stream = null;
const video = document.getElementById('webcam');
const canvas = document.getElementById('photoCanvas');
const cameraStatus = document.getElementById('cameraStatus');
const scanLine = document.getElementById('scanLine');
const btnCapture = document.getElementById('btnCapture');
const btnToggleCamera = document.getElementById('btnToggleCamera');
const biometricMsg = document.getElementById('biometricMsg');
const faceImageInput = document.getElementById('faceImage');
const thumbsGrid = document.getElementById('thumbsGrid');
const photoCountBadge = document.getElementById('photoCountBadge');

let capturedImages = [];

function toggleManagerKeySection() {
    const role = document.getElementById('role').value;
    const keySection = document.getElementById('managerKeySection');
    if (role === 'Manager') {
        keySection.classList.remove('hidden');
    } else {
        keySection.classList.add('hidden');
    }
}

async function startCamera() {
    try {
        biometricMsg.innerText = '';
        biometricMsg.className = 'text-[11px] font-medium text-zinc-500 mt-1';
        btnCapture.disabled = true;
        
        stream = await navigator.mediaDevices.getUserMedia({ video: { width: 640, height: 480 } });
        video.srcObject = stream;
        cameraStatus.classList.add('hidden');
        btnCapture.disabled = false;
        btnToggleCamera.innerHTML = 'Tắt Camera';
        btnToggleCamera.onclick = stopCamera;
    } catch (err) {
        console.error("Error accessing camera: ", err);
        biometricMsg.innerText = 'Không thể truy cập camera. Vui lòng cấp quyền.';
        biometricMsg.className = 'text-[11px] font-medium text-red-600 mt-1';
    }
}

function stopCamera() {
    if (stream) {
        stream.getTracks().forEach(track => track.stop());
        video.srcObject = null;
        stream = null;
    }
    cameraStatus.classList.remove('hidden');
    btnCapture.disabled = true;
    btnToggleCamera.innerHTML = 'Bật Camera';
    btnToggleCamera.onclick = startCamera;
    scanLine.classList.add('hidden');
}

function capturePhoto() {
    if (!stream) return;

    const context = canvas.getContext('2d');
    canvas.width = video.videoWidth;
    canvas.height = video.videoHeight;
    context.drawImage(video, 0, 0, canvas.width, canvas.height);

    const dataUrl = canvas.toDataURL('image/jpeg', 0.85);
    
    // Add to list
    capturedImages.push(dataUrl);
    
    // Update input value (join with special separator)
    faceImageInput.value = capturedImages.join(";base64split;");
    
    // Update badge count
    photoCountBadge.innerText = `Đã chụp: ${capturedImages.length}/4`;
    if (capturedImages.length >= 4) {
        photoCountBadge.className = "inline-flex items-center px-2 py-0.5 rounded-full text-[10px] font-bold bg-green-100 text-green-700";
        biometricMsg.innerText = 'Đã chụp đủ 4 ảnh! Bạn có thể chụp thêm hoặc nhấn Tạo tài khoản.';
        biometricMsg.className = 'text-[11px] font-medium text-green-600 mt-1';
    } else {
        photoCountBadge.className = "inline-flex items-center px-2 py-0.5 rounded-full text-[10px] font-bold bg-red-100 text-red-700";
        biometricMsg.innerText = `Chụp tiếp góc độ khác (Còn thiếu ${4 - capturedImages.length} ảnh)...`;
        biometricMsg.className = 'text-[11px] font-medium text-red-500 mt-1';
    }

    // Render thumbnail
    renderThumbnails();
}

function renderThumbnails() {
    thumbsGrid.innerHTML = '';
    capturedImages.forEach((imgSrc, index) => {
        const thumbWrapper = document.createElement('div');
        thumbWrapper.className = 'relative rounded-xl overflow-hidden aspect-square border border-zinc-200 shadow-sm';
        
        const img = document.createElement('img');
        img.src = imgSrc;
        img.className = 'w-full h-full object-cover transform -scale-x-100';
        
        const deleteBtn = document.createElement('button');
        deleteBtn.type = 'button';
        deleteBtn.className = 'absolute top-0.5 right-0.5 bg-red-600 text-white w-4 h-4 rounded-full flex items-center justify-center text-[10px] hover:bg-red-700 transition shadow';
        deleteBtn.innerHTML = '<i class="fa-solid fa-times"></i>';
        deleteBtn.onclick = () => deleteThumbnail(index);
        
        thumbWrapper.appendChild(img);
        thumbWrapper.appendChild(deleteBtn);
        thumbsGrid.appendChild(thumbWrapper);
    });
}

function deleteThumbnail(index) {
    capturedImages.splice(index, 1);
    faceImageInput.value = capturedImages.join(";base64split;");
    
    photoCountBadge.innerText = `Đã chụp: ${capturedImages.length}/4`;
    if (capturedImages.length >= 4) {
        photoCountBadge.className = "inline-flex items-center px-2 py-0.5 rounded-full text-[10px] font-bold bg-green-100 text-green-700";
        biometricMsg.innerText = 'Đã chụp đủ 4 ảnh! Bạn có thể đăng ký.';
        biometricMsg.className = 'text-[11px] font-medium text-green-600 mt-1';
    } else {
        photoCountBadge.className = "inline-flex items-center px-2 py-0.5 rounded-full text-[10px] font-bold bg-red-100 text-red-700";
        biometricMsg.innerText = `Vui lòng chụp thêm ${4 - capturedImages.length} ảnh khác góc độ...`;
        biometricMsg.className = 'text-[11px] font-medium text-red-500 mt-1';
    }
    
    renderThumbnails();
}

function validateForm() {
    if (capturedImages.length < 4) {
        biometricMsg.innerText = 'Vui lòng chụp đủ ít nhất 4 góc độ khuôn mặt trước khi đăng ký.';
        biometricMsg.className = 'text-[11px] font-medium text-red-600 mt-1';
        return false;
    }
    return true;
}
