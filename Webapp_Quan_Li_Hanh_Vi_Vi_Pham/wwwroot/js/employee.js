(() => {
    const app = document.querySelector("[data-employee-app]");
    if (!app) return;

    const biometricVerifyUrl = "/Account/VerifyCurrentUserFace";
    const initialTab = app.dataset.initialTab || "home";
    const currentUserId = app.dataset.userId || "";
    const currentUsername = app.dataset.username || "";
    const currentFullName = app.dataset.fullName || "";
    const userScope = currentUserId || currentUsername || "anonymous";
    const storageKeys = {
        attendance: `employee.attendance.${userScope}`,
        requests: `employee.requests.${userScope}`,
        chats: `employee.chats.${userScope}`,
        profile: `employee.profile.${userScope}`,
        settings: `employee.settings.${userScope}`,
        avatar: `employee.avatar.${userScope}`
    };

    const tabButtons = Array.from(document.querySelectorAll("[data-tab-trigger]"));
    const tabPanels = Array.from(document.querySelectorAll("[data-tab-panel]"));

    const defaultProfile = {
        name: currentFullName || document.querySelector("[data-profile-input='name']")?.value || "",
        department: document.querySelector("[data-profile-input='department']")?.value || "",
        email: document.querySelector("[data-profile-input='email']")?.value || "",
        phone: document.querySelector("[data-profile-input='phone']")?.value || "",
        employeeId: document.querySelector("[data-profile-id]")?.textContent?.trim() || "EMP-2026-014"
    };

    const defaultChats = {
        manager: [
            { author: "other", text: "Bạn cập nhật giúp tôi tiến độ ca sáng trước 11:00 nhé." },
            { author: "self", text: "Vâng, tôi sẽ gửi báo cáo ngay khi hoàn tất." }
        ],
        hr: [
            { author: "other", text: "Phòng nhân sự nhắc bạn cập nhật hồ sơ tháng này." }
        ]
    };

    const attendanceVideo = document.querySelector("[data-attendance-video]");
    const attendanceCanvas = document.querySelector("[data-attendance-canvas]");
    const attendanceScanline = document.querySelector("[data-attendance-scanline]");
    const attendanceCameraStatus = document.querySelector("[data-attendance-camera-status]");
    const attendancePreview = document.querySelector("[data-attendance-preview]");
    const attendancePreviewEmpty = document.querySelector("[data-attendance-preview-empty]");
    const attendanceMessage = document.querySelector("[data-attendance-message]");
    const attendanceDetailModal = document.querySelector("[data-attendance-detail-modal]");
    const attendanceCameraModal = document.querySelector("[data-attendance-camera-modal]");

    let attendanceStream = null;
    let currentAttendanceAction = null;

    const readStore = (key, fallback) => {
        try {
            const raw = localStorage.getItem(key);
            return raw ? JSON.parse(raw) : fallback;
        } catch {
            return fallback;
        }
    };

    const writeStore = (key, value) => {
        try {
            localStorage.setItem(key, JSON.stringify(value));
        } catch {
            console.warn("localStorage quota exceeded for key:", key);
        }
    };

    const readAvatar = () => {
        try {
            return localStorage.getItem(storageKeys.avatar) || null;
        } catch {
            return null;
        }
    };

    const writeAvatar = (dataUrl) => {
        try {
            if (dataUrl) {
                localStorage.setItem(storageKeys.avatar, dataUrl);
            } else {
                localStorage.removeItem(storageKeys.avatar);
            }
        } catch {
            const msg = document.querySelector("[data-avatar-message]");
            if (msg) {
                msg.textContent = "Ảnh quá lớn, không thể lưu vào trình duyệt. Hãy chọn ảnh nhỏ hơn.";
            }
        }
    };

    const compressImage = (file, callback) => {
        const reader = new FileReader();
        reader.onload = (event) => {
            if (file.type === "image/gif") {
                callback(event.target.result);
                return;
            }

            const img = new Image();
            img.onload = () => {
                const MAX = 256;
                let { width, height } = img;
                if (width > MAX || height > MAX) {
                    const ratio = Math.min(MAX / width, MAX / height);
                    width = Math.round(width * ratio);
                    height = Math.round(height * ratio);
                }

                const canvas = document.createElement("canvas");
                canvas.width = width;
                canvas.height = height;
                const ctx = canvas.getContext("2d");
                ctx.drawImage(img, 0, 0, width, height);
                callback(canvas.toDataURL("image/jpeg", 0.75));
            };
            img.src = event.target.result;
        };
        reader.readAsDataURL(file);
    };

    const normalizeAttendance = (raw) => {
        if (!raw || typeof raw !== "object") {
            return {
                currentSession: null,
                sessions: [],
                status: "Chưa check-in",
                lastCapture: null
            };
        }

        if (Array.isArray(raw.sessions) || raw.currentSession) {
            return {
                currentSession: raw.currentSession || null,
                sessions: Array.isArray(raw.sessions) ? raw.sessions : [],
                status: raw.status || "Chưa check-in",
                lastCapture: raw.lastCapture || raw.currentSession?.checkInImage || null
            };
        }

        const migrated = {
            currentSession: null,
            sessions: [],
            status: raw.status || "Chưa check-in",
            lastCapture: null
        };

        if (raw.checkIn) {
            if (raw.checkOut) {
                migrated.sessions.push({
                    id: `session-${Date.parse(raw.checkIn) || Date.now()}`,
                    checkInAt: raw.checkIn,
                    checkOutAt: raw.checkOut,
                    checkInImage: null,
                    checkOutImage: null,
                    status: "Đã check-out"
                });
            } else {
                migrated.currentSession = {
                    id: `session-${Date.parse(raw.checkIn) || Date.now()}`,
                    checkInAt: raw.checkIn,
                    checkInImage: null
                };
            }
        }

        return migrated;
    };

    let profile = { ...defaultProfile, ...readStore(storageKeys.profile, {}) };
    let avatarDataUrl = readAvatar();
    let attendance = normalizeAttendance(readStore(storageKeys.attendance, null));
    let requests = readStore(storageKeys.requests, []);
    let chats = readStore(storageKeys.chats, defaultChats);
    let settings = readStore(storageKeys.settings, {
        notifications: true,
        compact: false,
        language: "vi-VN",
        theme: "light",
        reducedMotion: false
    });
    let activeChat = "manager";

    const getLocale = () => settings.language || "vi-VN";

    const formatDate = (date) =>
        new Intl.DateTimeFormat(getLocale(), {
            weekday: "long",
            day: "2-digit",
            month: "2-digit",
            year: "numeric"
        }).format(date);

    const formatTime = (date) =>
        new Intl.DateTimeFormat(getLocale(), {
            hour: "2-digit",
            minute: "2-digit",
            second: "2-digit"
        }).format(date);

    const formatShortTime = (value) => {
        if (!value) return "--:--";
        return new Intl.DateTimeFormat(getLocale(), {
            hour: "2-digit",
            minute: "2-digit"
        }).format(new Date(value));
    };

    const formatDateTime = (value) => {
        if (!value) return "--";
        return new Intl.DateTimeFormat(getLocale(), {
            hour: "2-digit",
            minute: "2-digit",
            second: "2-digit",
            day: "2-digit",
            month: "2-digit",
            year: "numeric"
        }).format(new Date(value));
    };

    const toDateKey = (value) => {
        const date = value ? new Date(value) : new Date();
        const year = date.getFullYear();
        const month = String(date.getMonth() + 1).padStart(2, "0");
        const day = String(date.getDate()).padStart(2, "0");
        return `${year}-${month}-${day}`;
    };

    const calculateSessionDurationMs = (session) => {
        if (!session?.checkInAt) return 0;
        const start = new Date(session.checkInAt).getTime();
        const end = session.checkOutAt ? new Date(session.checkOutAt).getTime() : Date.now();
        return Math.max(end - start, 0);
    };

    const formatDuration = (durationMs) => {
        const totalMinutes = Math.floor(Math.max(durationMs, 0) / 60000);
        const hours = Math.floor(totalMinutes / 60);
        const minutes = totalMinutes % 60;
        return `${String(hours).padStart(2, "0")}h ${String(minutes).padStart(2, "0")}m`;
    };

    const getTodayTotalDurationMs = () => {
        const todayKey = toDateKey();
        const completed = attendance.sessions
            .filter((session) => toDateKey(session.checkInAt) === todayKey)
            .reduce((sum, session) => sum + calculateSessionDurationMs(session), 0);

        const current = attendance.currentSession && toDateKey(attendance.currentSession.checkInAt) === todayKey
            ? calculateSessionDurationMs(attendance.currentSession)
            : 0;

        return completed + current;
    };

    const saveAttendance = () => {
        writeStore(storageKeys.attendance, attendance);
    };

    const setActiveTab = (tab) => {
        const normalized = tabPanels.some((panel) => panel.dataset.tabPanel === tab) ? tab : "home";
        const homeHero = document.querySelector("[data-home-hero]");

        tabButtons.forEach((button) => {
            const isActive = button.dataset.tabTrigger === normalized;
            button.classList.toggle("bg-red-600", isActive);
            button.classList.toggle("text-white", isActive);
            button.classList.toggle("shadow-sm", isActive);
            button.classList.toggle("hover:bg-red-600", isActive);
            button.classList.toggle("hover:text-white", isActive);
            button.classList.toggle("text-slate-600", !isActive);
        });

        tabPanels.forEach((panel) => {
            panel.classList.toggle("hidden", panel.dataset.tabPanel !== normalized);
        });

        if (homeHero) {
            homeHero.classList.toggle("hidden", normalized !== "home");
        }

        const url = new URL(window.location.href);
        url.searchParams.set("tab", normalized);
        window.history.replaceState({}, "", url);
    };

    const renderDateTime = () => {
        const now = new Date();
        document.querySelectorAll("[data-live-date], [data-home-date]").forEach((el) => {
            el.textContent = formatDate(now);
        });
        document.querySelectorAll("[data-live-time], [data-home-time]").forEach((el) => {
            el.textContent = formatTime(now);
        });
    };

    const renderProfile = () => {
        const mappings = [
            ["[data-employee-name]", profile.name],
            ["[data-home-name]", profile.name],
            ["[data-profile-name]", profile.name],
            ["[data-profile-department]", profile.department],
            ["[data-profile-department-view]", profile.department],
            ["[data-profile-id]", profile.employeeId],
            ["[data-profile-email-view]", profile.email],
            ["[data-profile-phone-view]", profile.phone]
        ];

        mappings.forEach(([selector, value]) => {
            document.querySelectorAll(selector).forEach((el) => {
                el.textContent = value;
            });
        });

        document.querySelectorAll("[data-avatar-image]").forEach((img) => {
            if (avatarDataUrl) {
                img.src = avatarDataUrl;
                img.classList.remove("hidden");
            } else {
                img.removeAttribute("src");
                img.classList.add("hidden");
            }
        });

        document.querySelectorAll("[data-avatar-fallback]").forEach((icon) => {
            icon.classList.toggle("hidden", !!avatarDataUrl);
        });

        document.querySelectorAll("[data-profile-input]").forEach((input) => {
            const field = input.dataset.profileInput;
            if (field && profile[field] !== undefined) {
                input.value = profile[field];
            }
        });

        const avatarMessage = document.querySelector("[data-avatar-message]");
        if (avatarMessage) {
            avatarMessage.textContent = avatarDataUrl
                ? "Ảnh đại diện đã được cập nhật và lưu trong trình duyệt hiện tại."
                : "Ảnh đại diện hiện đang dùng biểu tượng mặc định.";
        }
    };

    const renderAttendancePreview = (imageDataUrl) => {
        if (!attendancePreview || !attendancePreviewEmpty) return;
        if (imageDataUrl) {
            attendancePreview.src = imageDataUrl;
            attendancePreview.classList.remove("hidden");
            attendancePreviewEmpty.classList.add("hidden");
        } else {
            attendancePreview.removeAttribute("src");
            attendancePreview.classList.add("hidden");
            attendancePreviewEmpty.classList.remove("hidden");
        }
    };

    const renderAttendance = () => {
        const latestCompleted = attendance.sessions[attendance.sessions.length - 1] || null;
        const referenceSession = attendance.currentSession || latestCompleted;

        const checkInEl = document.querySelector("[data-attendance-checkin]");
        const checkOutEl = document.querySelector("[data-attendance-checkout]");
        const totalEl = document.querySelector("[data-attendance-total]");
        const statusEl = document.querySelector("[data-attendance-status]");
        const historyEl = document.querySelector("[data-attendance-history]");

        if (checkInEl) checkInEl.textContent = referenceSession?.checkInAt ? formatShortTime(referenceSession.checkInAt) : "--:--";
        if (checkOutEl) {
            checkOutEl.textContent = attendance.currentSession
                ? "--:--"
                : (latestCompleted?.checkOutAt ? formatShortTime(latestCompleted.checkOutAt) : "--:--");
        }
        if (totalEl) totalEl.textContent = formatDuration(getTodayTotalDurationMs());
        if (statusEl) {
            statusEl.textContent = attendance.currentSession ? "Đang làm việc" : (attendance.sessions.length ? "Đã check-out" : "Chưa check-in");
        }

        renderAttendancePreview(attendance.lastCapture || attendance.currentSession?.checkInImage || latestCompleted?.checkOutImage || latestCompleted?.checkInImage || null);

        if (!historyEl) return;
        historyEl.innerHTML = "";

        const timeline = [];
        attendance.sessions.forEach((session) => timeline.push({ ...session, active: false }));
        if (attendance.currentSession) {
            timeline.push({ ...attendance.currentSession, active: true });
        }

        if (!timeline.length) {
            historyEl.innerHTML = '<div class="rounded-2xl bg-slate-50 p-4 text-sm text-slate-500">Chưa có lịch sử chấm công trong trình duyệt này.</div>';
            return;
        }

        [...timeline].reverse().forEach((item) => {
            const card = document.createElement("div");
            card.className = "rounded-2xl border border-slate-200 bg-slate-50 p-4";
            const duration = formatDuration(calculateSessionDurationMs(item));
            const statusText = item.active ? "Đang làm việc" : "Đã check-out";
            card.innerHTML = `
                <div class="flex items-start justify-between gap-3">
                    <div>
                        <p class="font-semibold text-slate-900">${formatDate(new Date(item.checkInAt))}</p>
                        <p class="mt-1 text-xs text-slate-500">Check-in: ${formatDateTime(item.checkInAt)}</p>
                        <p class="mt-1 text-xs text-slate-500">Check-out: ${item.checkOutAt ? formatDateTime(item.checkOutAt) : "Chưa có"}</p>
                    </div>
                    <span class="rounded-full px-2.5 py-1 text-xs font-semibold ${item.active ? "bg-amber-100 text-amber-700" : "bg-emerald-100 text-emerald-700"}">${statusText}</span>
                </div>
                <div class="mt-3 flex items-center justify-between gap-3">
                    <p class="text-sm font-medium text-slate-700">Tổng thời gian: <span class="font-bold text-slate-900">${duration}</span></p>
                    <button type="button" data-attendance-detail-button="${item.id}" class="rounded-xl border border-slate-200 bg-white px-3 py-2 text-xs font-semibold text-slate-700 transition hover:bg-red-50 hover:text-red-600">
                        Xem chi tiết
                    </button>
                </div>
            `;
            historyEl.appendChild(card);
        });
    };

    const openAttendanceDetail = (sessionId) => {
        const session = attendance.currentSession?.id === sessionId
            ? { ...attendance.currentSession, active: true }
            : attendance.sessions.find((item) => item.id === sessionId);
        if (!session || !attendanceDetailModal) return;

        const setImage = (imageSelector, emptySelector, imageDataUrl) => {
            const image = attendanceDetailModal.querySelector(imageSelector);
            const empty = attendanceDetailModal.querySelector(emptySelector);
            if (!image || !empty) return;

            if (imageDataUrl) {
                image.src = imageDataUrl;
                image.classList.remove("hidden");
                empty.classList.add("hidden");
            } else {
                image.removeAttribute("src");
                image.classList.add("hidden");
                empty.classList.remove("hidden");
            }
        };

        attendanceDetailModal.querySelector("[data-attendance-detail-checkin]").textContent = formatDateTime(session.checkInAt);
        attendanceDetailModal.querySelector("[data-attendance-detail-checkout]").textContent = session.checkOutAt ? formatDateTime(session.checkOutAt) : "Chưa check-out";
        attendanceDetailModal.querySelector("[data-attendance-detail-duration]").textContent = formatDuration(calculateSessionDurationMs(session));
        attendanceDetailModal.querySelector("[data-attendance-detail-date]").textContent = formatDate(new Date(session.checkInAt));
        attendanceDetailModal.querySelector("[data-attendance-detail-status]").textContent = session.active ? "Đang làm việc" : "Đã check-out";

        setImage("[data-attendance-detail-checkin-image]", "[data-attendance-detail-checkin-empty]", session.checkInImage);
        setImage("[data-attendance-detail-checkout-image]", "[data-attendance-detail-checkout-empty]", session.checkOutImage);

        attendanceDetailModal.classList.remove("hidden");
        attendanceDetailModal.classList.add("flex");
    };

    const closeAttendanceDetail = () => {
        if (!attendanceDetailModal) return;
        attendanceDetailModal.classList.add("hidden");
        attendanceDetailModal.classList.remove("flex");
    };

    const renderRequests = () => {
        const list = document.querySelector("[data-request-list]");
        if (!list) return;

        list.innerHTML = "";
        if (!requests.length) {
            list.innerHTML = '<div class="rounded-2xl bg-slate-50 p-4 text-sm text-slate-500">Chưa có đơn nào được tạo trong trình duyệt này.</div>';
            return;
        }

        [...requests].reverse().forEach((item) => {
            const card = document.createElement("div");
            const tone = item.status === "Đã gửi" ? "bg-green-50 text-green-700" : "bg-amber-50 text-amber-700";
            card.className = "rounded-2xl border border-slate-200 bg-white p-4";
            card.innerHTML = `
                <div class="flex items-center justify-between gap-3">
                    <p class="font-semibold text-slate-900">${item.type}</p>
                    <span class="rounded-full px-2.5 py-1 text-xs font-semibold ${tone}">${item.status}</span>
                </div>
                <p class="mt-2 text-sm text-slate-500">${item.date || "Chưa chọn ngày"}</p>
                <p class="mt-2 text-sm text-slate-600">${item.content}</p>
            `;
            list.appendChild(card);
        });
    };

    const renderChats = () => {
        const title = document.querySelector("[data-chat-title]");
        const thread = document.querySelector("[data-chat-thread]");
        if (!title || !thread) return;

        title.textContent = activeChat === "manager" ? "Quản lý trực tiếp" : "Phòng nhân sự";
        thread.innerHTML = "";

        (chats[activeChat] || []).forEach((message) => {
            const bubble = document.createElement("div");
            bubble.className = message.author === "self"
                ? "ml-auto max-w-md rounded-2xl bg-red-600 px-4 py-3 text-sm text-white"
                : "max-w-md rounded-2xl bg-slate-100 px-4 py-3 text-sm text-slate-700";
            bubble.textContent = message.text;
            thread.appendChild(bubble);
        });
    };

    const applySettings = () => {
        app.classList.toggle("space-y-6", settings.compact);
        document.body.classList.toggle("employee-dark", settings.theme === "dark");
        document.body.classList.toggle("motion-reduce", !!settings.reducedMotion);

        const compactToggle = document.querySelector('[data-setting-toggle="compact"]');
        const notificationsToggle = document.querySelector('[data-setting-toggle="notifications"]');
        const reducedMotionToggle = document.querySelector('[data-setting-toggle="reducedMotion"]');
        const languageSelect = document.querySelector('[data-setting-select="language"]');
        const themeSelect = document.querySelector('[data-setting-select="theme"]');

        if (compactToggle) compactToggle.checked = !!settings.compact;
        if (notificationsToggle) notificationsToggle.checked = !!settings.notifications;
        if (reducedMotionToggle) reducedMotionToggle.checked = !!settings.reducedMotion;
        if (languageSelect) languageSelect.value = settings.language || "vi-VN";
        if (themeSelect) themeSelect.value = settings.theme || "light";
    };

    const startAttendanceCamera = async () => {
        if (!attendanceVideo) return false;
        try {
            const statusText = document.querySelector("[data-attendance-camera-status-text]");
            if (statusText) statusText.textContent = "Đang khởi tạo camera...";
            attendanceCameraStatus?.classList.remove("hidden");
            
            attendanceStream = await navigator.mediaDevices.getUserMedia({
                video: { width: 640, height: 480 }
            });
            attendanceVideo.srcObject = attendanceStream;
            attendanceCameraStatus?.classList.add("hidden");
            return true;
        } catch {
            const statusText = document.querySelector("[data-attendance-camera-status-text]");
            if (statusText) statusText.textContent = "Lỗi camera. Vui lòng cấp quyền.";
            if (attendanceMessage) {
                attendanceMessage.textContent = "Không thể truy cập camera. Vui lòng cấp quyền rồi thử lại.";
            }
            return false;
        }
    };

    const stopAttendanceCamera = () => {
        if (attendanceStream) {
            attendanceStream.getTracks().forEach((track) => track.stop());
            attendanceStream = null;
        }
        if (attendanceVideo) {
            attendanceVideo.srcObject = null;
        }
        attendanceCameraStatus?.classList.remove("hidden");
        attendanceScanline?.classList.add("hidden");
    };

    const openAttendanceCameraModal = (action) => {
        currentAttendanceAction = action;
        
        if (action === "checkin" && attendance.currentSession) {
            alert("Bạn đã check-in rồi. Hãy check-out trước khi tạo phiên mới.");
            if (attendanceMessage) attendanceMessage.textContent = "Bạn đã check-in rồi. Hãy check-out trước khi tạo phiên mới.";
            return;
        }
        if (action === "checkout" && !attendance.currentSession) {
            alert("Bạn cần check-in trước khi check-out.");
            if (attendanceMessage) attendanceMessage.textContent = "Bạn cần check-in trước khi check-out.";
            return;
        }

        const title = document.querySelector("[data-attendance-camera-title]");
        if (title) title.textContent = action === "checkin" ? "Check-in Khuôn Mặt" : "Check-out Khuôn Mặt";
        
        const msg = document.querySelector("[data-attendance-camera-message]");
        if (msg) msg.textContent = "Vui lòng nhìn thẳng vào camera và nhấn nút bên dưới để xác thực.";
        
        if (attendanceCameraModal) {
            attendanceCameraModal.classList.remove("hidden");
            attendanceCameraModal.classList.add("flex");
        }
        startAttendanceCamera();
    };

    const closeAttendanceCameraModal = () => {
        if (attendanceCameraModal) {
            attendanceCameraModal.classList.add("hidden");
            attendanceCameraModal.classList.remove("flex");
        }
        stopAttendanceCamera();
        currentAttendanceAction = null;
    };

    const captureAttendanceFrame = () => {
        if (!attendanceVideo || !attendanceCanvas || !attendanceVideo.videoWidth) return null;
        const context = attendanceCanvas.getContext("2d");
        attendanceCanvas.width = attendanceVideo.videoWidth;
        attendanceCanvas.height = attendanceVideo.videoHeight;
        context.drawImage(attendanceVideo, 0, 0, attendanceCanvas.width, attendanceCanvas.height);
        return attendanceCanvas.toDataURL("image/jpeg", 0.85);
    };

    const verifyAttendanceFace = async (imageDataUrl) => {
        const response = await fetch(biometricVerifyUrl, {
            method: "POST",
            headers: {
                "Content-Type": "application/x-www-form-urlencoded; charset=UTF-8"
            },
            body: new URLSearchParams({
                faceImage: imageDataUrl
            })
        });

        if (!response.ok) {
            throw new Error("Không thể xác thực khuôn mặt.");
        }

        return response.json();
    };

    const handleAttendanceAction = async () => {
        if (!attendanceStream || !attendanceVideo?.videoWidth) {
            const msg = document.querySelector("[data-attendance-camera-message]");
            if (msg) msg.textContent = "Camera chưa sẵn sàng.";
            return;
        }

        const action = currentAttendanceAction;
        const imageDataUrl = captureAttendanceFrame();
        if (!imageDataUrl) {
            const msg = document.querySelector("[data-attendance-camera-message]");
            if (msg) msg.textContent = "Không thể chụp ảnh từ camera. Hãy thử lại.";
            return;
        }

        attendanceScanline?.classList.remove("hidden");
        const msg = document.querySelector("[data-attendance-camera-message]");
        if (msg) msg.textContent = `Đang quét khuôn mặt để ${action === "checkin" ? "check-in" : "check-out"}...`;

        try {
            const result = await verifyAttendanceFace(imageDataUrl);
            attendanceScanline?.classList.add("hidden");

            if (!result.success) {
                if (msg) msg.textContent = result.message || "Xác thực khuôn mặt thất bại.";
                return;
            }

            const nowIso = new Date().toISOString();
            attendance.lastCapture = imageDataUrl;

            if (action === "checkin") {
                attendance.currentSession = {
                    id: `session-${Date.now()}`,
                    checkInAt: nowIso,
                    checkInImage: imageDataUrl
                };
                attendance.status = "Đang làm việc";
                if (attendanceMessage) attendanceMessage.textContent = "Check-in thành công sau khi xác thực đúng khuôn mặt.";
            } else if (attendance.currentSession) {
                attendance.sessions.push({
                    id: attendance.currentSession.id,
                    checkInAt: attendance.currentSession.checkInAt,
                    checkOutAt: nowIso,
                    checkInImage: attendance.currentSession.checkInImage,
                    checkOutImage: imageDataUrl,
                    status: "Đã check-out"
                });
                attendance.currentSession = null;
                attendance.status = "Đã check-out";
                if (attendanceMessage) attendanceMessage.textContent = "Check-out thành công sau khi xác thực đúng khuôn mặt.";
            }

            saveAttendance();
            renderAttendance();
            closeAttendanceCameraModal();
        } catch (error) {
            attendanceScanline?.classList.add("hidden");
            if (msg) msg.textContent = error.message || "Có lỗi hệ thống khi xác thực khuôn mặt.";
        }
    };

    tabButtons.forEach((button) => {
        button.addEventListener("click", () => setActiveTab(button.dataset.tabTrigger));
    });

    document.querySelectorAll("[data-chat-target]").forEach((button) => {
        button.addEventListener("click", () => {
            activeChat = button.dataset.chatTarget;
            renderChats();
        });
    });

    document.querySelector("[data-chat-send]")?.addEventListener("click", () => {
        const input = document.querySelector("[data-chat-input]");
        const text = input?.value.trim();
        if (!text) return;
        chats[activeChat] = chats[activeChat] || [];
        chats[activeChat].push({ author: "self", text });
        writeStore(storageKeys.chats, chats);
        input.value = "";
        renderChats();
    });

    const buildDocHtml = (tieuDe, kinhGui, bodyLines, name, department, date, reason) => {
        const now = new Date();
        const d = now.getDate(), m = now.getMonth() + 1, y = now.getFullYear();
        const locationDate = `Ngày ${d} tháng ${m} năm ${y}`;

        const bodyHtml = bodyLines
            .map(line => {
                const filled = line
                    .replace(/\{name\}/g, `<strong>${name}</strong>`)
                    .replace(/\{department\}/g, `<strong>${department}</strong>`)
                    .replace(/\{date\}/g, `<strong>${date}</strong>`)
                    .replace(/\{reason\}/g, reason);
                return `<p style="text-align:justify;text-indent:2em;margin:4px 0;">${filled}</p>`;
            })
            .join("");

        return `
<div style="font-family:'Times New Roman',serif;font-size:13px;line-height:1.8;color:#111;padding:4px 8px;">
  <div style="text-align:center;margin-bottom:2px;">
    <strong style="font-size:13px;">CỘNG HOÀ XÃ HỘI CHỦ NGHĨA VIỆT NAM</strong><br>
    <span style="font-size:12px;">Độc lập – Tự do – Hạnh phúc</span><br>
    <span style="display:inline-block;width:140px;border-top:1.5px solid #111;margin-top:3px;"></span>
  </div>

  <div style="text-align:center;margin:14px 0 10px;">
    <strong style="font-size:14px;text-transform:uppercase;letter-spacing:0.04em;">${tieuDe}</strong>
  </div>

  <p style="margin:6px 0;"><em>Kính gửi:</em> ${kinhGui}</p>

  ${bodyHtml}

  <div style="margin-top:20px;display:flex;justify-content:flex-end;">
    <div style="text-align:center;min-width:180px;">
      <p style="margin:0;"><em>${locationDate}</em></p>
      <p style="margin:2px 0;">Người làm đơn</p>
      <p style="margin:0;font-style:italic;font-size:11px;color:#555;">(Ký và ghi rõ họ tên)</p>
      <p style="margin:40px 0 0;"><strong>${name}</strong></p>
    </div>
  </div>
</div>`;
    };

    const buildDoc = {
        "Nghỉ phép": (name, department, date, reason) => buildDocHtml(
            "Đơn xin nghỉ phép",
            "Ban Giám đốc và Quản lý bộ phận",
            [
                "Tôi tên là: {name} &nbsp;&nbsp; Bộ phận: {department}",
                "Tôi làm đơn này kính xin phép được nghỉ vào ngày {date}.",
                "Lý do: {reason}",
                "Kính mong Ban Giám đốc và Quản lý bộ phận xem xét, chấp thuận cho tôi được nghỉ theo thời gian trên.",
                "Tôi cam kết bàn giao công việc đầy đủ trước khi nghỉ và trở lại làm việc đúng lịch.",
                "Trân trọng cảm ơn!"
            ],
            name, department, date, reason
        ),
        "Đi muộn": (name, department, date, reason) => buildDocHtml(
            "Đơn xin đi muộn / về sớm",
            "Ban Giám đốc và Quản lý bộ phận",
            [
                "Tôi tên là: {name} &nbsp;&nbsp; Bộ phận: {department}",
                "Tôi làm đơn này kính xin phép được đi muộn / về sớm vào ngày {date}.",
                "Lý do: {reason}",
                "Kính mong Ban Giám đốc và Quản lý bộ phận xem xét và chấp thuận.",
                "Trân trọng cảm ơn!"
            ],
            name, department, date, reason
        ),
        "Tăng ca": (name, department, date, reason) => buildDocHtml(
            "Đơn xin làm thêm giờ (tăng ca)",
            "Ban Giám đốc và Quản lý bộ phận",
            [
                "Tôi tên là: {name} &nbsp;&nbsp; Bộ phận: {department}",
                "Tôi làm đơn này kính xin phép được làm thêm giờ vào ngày {date}.",
                "Nội dung công việc / Lý do: {reason}",
                "Kính mong Ban Giám đốc và Quản lý bộ phận xem xét, phê duyệt để tôi có thể hoàn thành nhiệm vụ được giao.",
                "Trân trọng cảm ơn!"
            ],
            name, department, date, reason
        ),
        "Điều chỉnh ca": (name, department, date, reason) => buildDocHtml(
            "Đơn xin điều chỉnh ca làm việc",
            "Ban Giám đốc và Quản lý bộ phận",
            [
                "Tôi tên là: {name} &nbsp;&nbsp; Bộ phận: {department}",
                "Tôi làm đơn này kính xin phép được điều chỉnh ca làm việc vào ngày {date}.",
                "Lý do: {reason}",
                "Kính mong Ban Giám đốc và Quản lý bộ phận xem xét, chấp thuận và sắp xếp ca phù hợp.",
                "Trân trọng cảm ơn!"
            ],
            name, department, date, reason
        )
    };

    const updateRequestPreview = () => {
        const typeEl = document.querySelector("[data-request-type]");
        const dateEl = document.querySelector("[data-request-date]");
        const reasonEl = document.querySelector("[data-request-reason]");
        const previewEl = document.querySelector("[data-request-preview]");

        if (!typeEl || !previewEl) return;

        const type = typeEl.value;
        const dateRaw = dateEl?.value;
        const dateStr = dateRaw ? new Date(dateRaw).toLocaleDateString("vi-VN") : "[Ngày/Tháng/Năm]";
        const reason = reasonEl?.value.trim() || "[Nhập lý do chi tiết...]";
        const name = profile.name || "[Tên nhân viên]";
        const department = profile.department || "[Bộ phận]";

        const builder = buildDoc[type] || buildDoc["Nghỉ phép"];
        previewEl.innerHTML = builder(name, department, dateStr, reason);
    };

    document.querySelector("[data-request-type]")?.addEventListener("change", updateRequestPreview);
    document.querySelector("[data-request-date]")?.addEventListener("change", updateRequestPreview);
    document.querySelector("[data-request-reason]")?.addEventListener("input", updateRequestPreview);

    document.querySelector("[data-request-submit]")?.addEventListener("click", () => {
        const type = document.querySelector("[data-request-type]")?.value || "";
        const date = document.querySelector("[data-request-date]")?.value || "";
        const reason = document.querySelector("[data-request-reason]")?.value.trim() || "";
        const previewEl = document.querySelector("[data-request-preview]");
        const content = previewEl ? previewEl.textContent : "";
        const message = document.querySelector("[data-request-message]");

        if (!reason) {
            if (message) message.textContent = "Vui lòng nhập lý do trước khi gửi đơn.";
            return;
        }

        requests.push({ type, date, content, status: "Đã gửi" });
        writeStore(storageKeys.requests, requests);
        
        const reasonInput = document.querySelector("[data-request-reason]");
        if (reasonInput) reasonInput.value = "";
        updateRequestPreview();
        
        if (message) message.textContent = "Đơn đã được gửi thành công.";
        renderRequests();
    });

    document.querySelector("[data-request-draft]")?.addEventListener("click", () => {
        const type = document.querySelector("[data-request-type]")?.value || "";
        const date = document.querySelector("[data-request-date]")?.value || "";
        const reason = document.querySelector("[data-request-reason]")?.value.trim() || "";
        const previewEl = document.querySelector("[data-request-preview]");
        const content = previewEl ? previewEl.textContent : "";
        const message = document.querySelector("[data-request-message]");

        if (!reason) {
            if (message) message.textContent = "Vui lòng nhập lý do trước khi lưu nháp.";
            return;
        }

        requests.push({ type, date, content, status: "Lưu nháp" });
        writeStore(storageKeys.requests, requests);
        if (message) message.textContent = "Đã lưu nháp đơn hiện tại.";
        renderRequests();
    });

    document.querySelector("[data-profile-save]")?.addEventListener("click", () => {
        profile = {
            ...profile,
            name: document.querySelector("[data-profile-input='name']")?.value.trim() || profile.name,
            department: document.querySelector("[data-profile-input='department']")?.value.trim() || profile.department,
            email: document.querySelector("[data-profile-input='email']")?.value.trim() || profile.email,
            phone: document.querySelector("[data-profile-input='phone']")?.value.trim() || profile.phone
        };
        writeStore(storageKeys.profile, profile);
        renderProfile();
        const msg = document.querySelector("[data-profile-message]");
        if (msg) {
            msg.textContent = "Đã cập nhật thông tin hiển thị trong khu vực nhân viên.";
        }
    });

    document.querySelectorAll("[data-avatar-input]").forEach((input) => {
        input.addEventListener("change", (event) => {
            const file = event.target.files?.[0];
            if (!file) return;

            if (!file.type.startsWith("image/")) {
                const msg = document.querySelector("[data-avatar-message]");
                if (msg) msg.textContent = "Vui lòng chọn đúng tệp hình ảnh.";
                event.target.value = "";
                return;
            }

            compressImage(file, (dataUrl) => {
                avatarDataUrl = dataUrl;
                writeAvatar(avatarDataUrl);
                event.target.value = "";
                renderProfile();
            });
        });
    });

    document.querySelector("[data-avatar-remove]")?.addEventListener("click", () => {
        avatarDataUrl = null;
        writeAvatar(null);
        renderProfile();
    });

    document.querySelectorAll("[data-setting-toggle]").forEach((toggle) => {
        toggle.addEventListener("change", () => {
            settings = {
                notifications: !!document.querySelector('[data-setting-toggle="notifications"]')?.checked,
                compact: !!document.querySelector('[data-setting-toggle="compact"]')?.checked,
                reducedMotion: !!document.querySelector('[data-setting-toggle="reducedMotion"]')?.checked,
                language: document.querySelector('[data-setting-select="language"]')?.value || "vi-VN",
                theme: document.querySelector('[data-setting-select="theme"]')?.value || "light"
            };
            writeStore(storageKeys.settings, settings);
            applySettings();
            renderDateTime();
            renderAttendance();
            renderRequests();
            const msg = document.querySelector("[data-setting-message]");
            if (msg) msg.textContent = "Đã cập nhật setting trong trình duyệt hiện tại.";
        });
    });

    document.querySelectorAll("[data-setting-select]").forEach((select) => {
        select.addEventListener("change", () => {
            settings = {
                notifications: !!document.querySelector('[data-setting-toggle="notifications"]')?.checked,
                compact: !!document.querySelector('[data-setting-toggle="compact"]')?.checked,
                reducedMotion: !!document.querySelector('[data-setting-toggle="reducedMotion"]')?.checked,
                language: document.querySelector('[data-setting-select="language"]')?.value || "vi-VN",
                theme: document.querySelector('[data-setting-select="theme"]')?.value || "light"
            };
            writeStore(storageKeys.settings, settings);
            applySettings();
            renderDateTime();
            renderAttendance();
            renderRequests();
            const msg = document.querySelector("[data-setting-message]");
            if (msg) msg.textContent = "Đã áp dụng thay đổi giao diện và ngôn ngữ.";
        });
    });

    document.querySelector("[data-setting-reset]")?.addEventListener("click", () => {
        settings = {
            notifications: true,
            compact: false,
            language: "vi-VN",
            theme: "light",
            reducedMotion: false
        };
        writeStore(storageKeys.settings, settings);
        applySettings();
        renderDateTime();
        renderAttendance();
        renderRequests();
        const msg = document.querySelector("[data-setting-message]");
        if (msg) msg.textContent = "Đã khôi phục thiết lập mặc định.";
    });

    document.querySelectorAll("[data-attendance-action-trigger]").forEach((button) => {
        button.addEventListener("click", () => openAttendanceCameraModal(button.dataset.attendanceActionTrigger));
    });

    document.querySelector("[data-attendance-camera-close]")?.addEventListener("click", closeAttendanceCameraModal);
    document.querySelector("[data-attendance-camera-capture]")?.addEventListener("click", handleAttendanceAction);

    attendanceCameraModal?.addEventListener("click", (event) => {
        if (event.target === attendanceCameraModal) {
            closeAttendanceCameraModal();
        }
    });

    document.querySelector("[data-attendance-history]")?.addEventListener("click", (event) => {
        const button = event.target.closest("[data-attendance-detail-button]");
        if (!button) return;
        openAttendanceDetail(button.dataset.attendanceDetailButton);
    });

    document.querySelector("[data-attendance-detail-close]")?.addEventListener("click", closeAttendanceDetail);
    attendanceDetailModal?.addEventListener("click", (event) => {
        if (event.target === attendanceDetailModal) {
            closeAttendanceDetail();
        }
    });

    renderDateTime();
    renderProfile();
    renderAttendance();
    renderRequests();
    updateRequestPreview();
    renderChats();
    applySettings();
    setActiveTab(initialTab);

    setInterval(() => {
        renderDateTime();
        if (attendance.currentSession) {
            renderAttendance();
        }
    }, 1000);

    window.addEventListener("beforeunload", () => {
        stopAttendanceCamera();
    });
})();
