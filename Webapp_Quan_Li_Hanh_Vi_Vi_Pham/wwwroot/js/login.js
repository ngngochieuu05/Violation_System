let stream = null;
const video = document.getElementById('webcam');
const canvas = document.getElementById('photoCanvas');
const cameraStatus = document.getElementById('cameraStatus');
const scanLine = document.getElementById('scanLine');
const btnCapture = document.getElementById('btnCapture');
const btnToggleCamera = document.getElementById('btnToggleCamera');
const biometricMsg = document.getElementById('biometricMsg');

function switchTab(mode) {
    const btnPass = document.getElementById('btnTabPassword');
    const btnFace = document.getElementById('btnTabFace');
    const formPass = document.getElementById('formPassword');
    const formFace = document.getElementById('formFace');

    if (mode === 'password') {
        btnPass.className = "flex-1 py-2.5 text-xs font-semibold rounded-xl transition-all duration-300 bg-red-600 text-white shadow";
        btnFace.className = "flex-1 py-2.5 text-xs font-semibold rounded-xl transition-all duration-300 text-zinc-500 hover:text-zinc-900";
        formPass.classList.remove('hidden');
        formFace.classList.add('hidden');
        stopCamera();
    } else {
        btnFace.className = "flex-1 py-2.5 text-xs font-semibold rounded-xl transition-all duration-300 bg-red-600 text-white shadow";
        btnPass.className = "flex-1 py-2.5 text-xs font-semibold rounded-xl transition-all duration-300 text-zinc-500 hover:text-zinc-900";
        formFace.classList.remove('hidden');
        formPass.classList.add('hidden');
        const pUser = document.getElementById('username').value;
        if(pUser) {
            document.getElementById('faceUsername').value = pUser;
        }
    }
}

async function startCamera() {
    try {
        biometricMsg.innerText = '';
        btnCapture.disabled = true;
        stream = await navigator.mediaDevices.getUserMedia({ video: { width: 640, height: 480 } });
        video.srcObject = stream;
        cameraStatus.classList.add('hidden');
        btnCapture.disabled = false;
        btnToggleCamera.innerHTML = '<i class="fa-solid fa-video-slash mr-1.5"></i>Tắt Camera';
        btnToggleCamera.onclick = stopCamera;
    } catch (err) {
        console.error("Error accessing camera: ", err);
        biometricMsg.innerText = 'Không thể truy cập camera. Vui lòng cấp quyền.';
        biometricMsg.className = 'text-xs text-center font-medium mt-1 text-red-600';
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
    btnToggleCamera.innerHTML = '<i class="fa-solid fa-camera mr-1.5"></i>Bật Camera';
    btnToggleCamera.onclick = startCamera;
    scanLine.classList.add('hidden');
}

function captureAndLogin() {
    const username = document.getElementById('faceUsername').value.trim();
    if (!username) {
        biometricMsg.innerText = 'Vui lòng nhập tên đăng nhập trước khi quét.';
        biometricMsg.className = 'text-xs text-center font-medium mt-1 text-red-500';
        return;
    }

    biometricMsg.innerText = 'Đang quét khuôn mặt và phân tích AI...';
    biometricMsg.className = 'text-xs text-center font-medium mt-1 text-red-600';
    scanLine.classList.remove('hidden');
    scanLine.className = 'absolute w-full h-[2px] bg-gradient-to-r from-transparent via-red-500 to-transparent shadow-[0_0_8px_#dc2626] top-0 left-0 animate-[bounce_2s_infinite]';

    const context = canvas.getContext('2d');
    canvas.width = video.videoWidth;
    canvas.height = video.videoHeight;
    context.drawImage(video, 0, 0, canvas.width, canvas.height);

    const dataUrl = canvas.toDataURL('image/jpeg', 0.85);
    const loginUrl = window.BiometricLoginConfig?.loginUrl || '/Account/BiometricLogin';

    $.ajax({
        url: loginUrl,
        type: 'POST',
        data: {
            username: username,
            faceImage: dataUrl
        },
        success: function (response) {
            scanLine.classList.add('hidden');
            if (response.success) {
                biometricMsg.innerText = 'Xác thực thành công! Đang chuyển hướng...';
                biometricMsg.className = 'text-xs text-center font-medium mt-1 text-green-600';
                stopCamera();
                setTimeout(() => {
                    window.location.href = response.redirectUrl;
                }, 1000);
            } else {
                biometricMsg.innerText = 'Thất bại: ' + response.message;
                biometricMsg.className = 'text-xs text-center font-medium mt-1 text-red-600';
            }
        },
        error: function (xhr, status, error) {
            scanLine.classList.add('hidden');
            biometricMsg.innerText = 'Đã có lỗi hệ thống xảy ra.';
            biometricMsg.className = 'text-xs text-center font-medium mt-1 text-red-600';
            console.error("AJAX Error: ", error);
        }
    });
}
