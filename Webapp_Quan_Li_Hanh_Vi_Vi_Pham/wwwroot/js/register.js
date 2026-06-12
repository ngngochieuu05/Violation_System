let stream = null;
const video = document.getElementById("webcam");
const canvas = document.getElementById("photoCanvas");
const cameraStatus = document.getElementById("cameraStatus");
const scanLine = document.getElementById("scanLine");
const btnCapture = document.getElementById("btnCapture");
const btnToggleCamera = document.getElementById("btnToggleCamera");
const biometricMsg = document.getElementById("biometricMsg");
const faceImageInput = document.getElementById("faceImage");
const thumbsGrid = document.getElementById("thumbsGrid");
const photoCountBadge = document.getElementById("photoCountBadge");
const passwordInput = document.getElementById("password");

const passwordRuleText = "Tối thiểu 8 ký tự, có chữ hoa, chữ thường, số và ký tự đặc biệt.";
let capturedImages = [];

function toggleManagerKeySection() {
    const role = document.getElementById("role").value;
    const keySection = document.getElementById("managerKeySection");
    if (role === "Manager") {
        keySection.classList.remove("hidden");
    } else {
        keySection.classList.add("hidden");
    }
}

function ensurePasswordHint() {
    if (!passwordInput || document.getElementById("passwordRuleText")) {
        return;
    }

    const hint = document.createElement("p");
    hint.id = "passwordRuleText";
    hint.className = "mt-1.5 text-[11px] text-zinc-500";
    hint.textContent = passwordRuleText;
    passwordInput.parentElement?.insertAdjacentElement("afterend", hint);
}

function validatePassword(password) {
    if (!password || password.length < 8) {
        return false;
    }

    const hasUpper = /[A-Z]/.test(password);
    const hasLower = /[a-z]/.test(password);
    const hasDigit = /\d/.test(password);
    const hasSpecial = /[^A-Za-z0-9]/.test(password);
    return hasUpper && hasLower && hasDigit && hasSpecial;
}

async function startCamera() {
    try {
        biometricMsg.innerText = "";
        biometricMsg.className = "text-[11px] font-medium text-zinc-500 mt-1";
        btnCapture.disabled = true;

        stream = await navigator.mediaDevices.getUserMedia({ video: { width: 640, height: 480 } });
        video.srcObject = stream;
        cameraStatus.classList.add("hidden");
        btnCapture.disabled = false;
        btnToggleCamera.innerHTML = "Tắt Camera";
        btnToggleCamera.onclick = stopCamera;
    } catch (err) {
        console.error("Error accessing camera:", err);
        biometricMsg.innerText = "Không thể truy cập camera. Vui lòng cấp quyền.";
        biometricMsg.className = "text-[11px] font-medium text-red-600 mt-1";
    }
}

function stopCamera() {
    if (stream) {
        stream.getTracks().forEach(track => track.stop());
        video.srcObject = null;
        stream = null;
    }

    cameraStatus.classList.remove("hidden");
    btnCapture.disabled = true;
    btnToggleCamera.innerHTML = "Bật Camera";
    btnToggleCamera.onclick = startCamera;
    scanLine.classList.add("hidden");
}

function capturePhoto() {
    if (!stream) {
        return;
    }

    const context = canvas.getContext("2d");
    canvas.width = video.videoWidth;
    canvas.height = video.videoHeight;
    context.drawImage(video, 0, 0, canvas.width, canvas.height);

    const dataUrl = canvas.toDataURL("image/jpeg", 0.85);
    capturedImages.push(dataUrl);
    faceImageInput.value = capturedImages.join(";base64split;");

    photoCountBadge.innerText = `Đã chụp: ${capturedImages.length}/4`;
    if (capturedImages.length >= 4) {
        photoCountBadge.className = "inline-flex items-center px-2 py-0.5 rounded-full text-[10px] font-bold bg-green-100 text-green-700";
        biometricMsg.innerText = "Đã chụp đủ 4 ảnh. Bạn có thể tạo tài khoản.";
        biometricMsg.className = "text-[11px] font-medium text-green-600 mt-1";
    } else {
        photoCountBadge.className = "inline-flex items-center px-2 py-0.5 rounded-full text-[10px] font-bold bg-red-100 text-red-700";
        biometricMsg.innerText = `Chụp tiếp góc khác. Còn thiếu ${4 - capturedImages.length} ảnh.`;
        biometricMsg.className = "text-[11px] font-medium text-red-500 mt-1";
    }

    renderThumbnails();
}

function renderThumbnails() {
    thumbsGrid.innerHTML = "";
    capturedImages.forEach((imgSrc, index) => {
        const thumbWrapper = document.createElement("div");
        thumbWrapper.className = "relative rounded-xl overflow-hidden aspect-square border border-zinc-200 shadow-sm";

        const img = document.createElement("img");
        img.src = imgSrc;
        img.className = "w-full h-full object-cover transform -scale-x-100";

        const deleteBtn = document.createElement("button");
        deleteBtn.type = "button";
        deleteBtn.className = "absolute top-0.5 right-0.5 bg-red-600 text-white w-4 h-4 rounded-full flex items-center justify-center text-[10px] hover:bg-red-700 transition shadow";
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
        biometricMsg.innerText = "Đã chụp đủ 4 ảnh. Bạn có thể đăng ký.";
        biometricMsg.className = "text-[11px] font-medium text-green-600 mt-1";
    } else {
        photoCountBadge.className = "inline-flex items-center px-2 py-0.5 rounded-full text-[10px] font-bold bg-red-100 text-red-700";
        biometricMsg.innerText = `Vui lòng chụp thêm ${4 - capturedImages.length} ảnh khác góc độ.`;
        biometricMsg.className = "text-[11px] font-medium text-red-500 mt-1";
    }

    renderThumbnails();
}

function validateForm() {
    ensurePasswordHint();

    const password = passwordInput?.value ?? "";
    if (!validatePassword(password)) {
        biometricMsg.innerText = passwordRuleText;
        biometricMsg.className = "text-[11px] font-medium text-red-600 mt-1";
        passwordInput?.focus();
        return false;
    }

    if (capturedImages.length < 4) {
        biometricMsg.innerText = "Vui lòng chụp đủ ít nhất 4 góc độ khuôn mặt trước khi đăng ký.";
        biometricMsg.className = "text-[11px] font-medium text-red-600 mt-1";
        return false;
    }

    return true;
}

document.addEventListener("DOMContentLoaded", () => {
    ensurePasswordHint();
    toggleManagerKeySection();
});
