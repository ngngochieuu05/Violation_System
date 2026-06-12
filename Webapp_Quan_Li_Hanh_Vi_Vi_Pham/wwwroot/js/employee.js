(() => {
    const app = document.querySelector("[data-employee-app]");
    if (!app) return;

    const biometricVerifyUrl = "/Account/VerifyCurrentUserFace";
    const biometricRegistrationStatusUrl = "/Account/BiometricRegistrationStatus";
    const onboardingStatusUrl = "/Account/OnboardingStatus";
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
        avatar: `employee.avatar.${userScope}`,
        tasks: `employee.tasks.${userScope}`
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
    // Teleport modals to body to escape z-index stacking contexts
    [attendanceDetailModal, attendanceCameraModal, document.querySelector("[data-face-modal]"), document.querySelector("[data-onboarding-modal]")].forEach(modal => {
        if (modal) {
            document.body.appendChild(modal);
        }
    });

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

    const validatePasswordPolicy = (password) => {
        if (!password || password.length < 8) {
            return false;
        }

        return /[A-Z]/.test(password)
            && /[a-z]/.test(password)
            && /\d/.test(password)
            && /[^A-Za-z0-9]/.test(password);
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
    let hasPayrollPin = false;
    let attendance = normalizeAttendance(readStore(storageKeys.attendance, null));
    let requests = readStore(storageKeys.requests, []);
    let chats = readStore(storageKeys.chats, defaultChats);
    let tasks = readStore(storageKeys.tasks, [
        { id: "task-1", title: "Cập nhật báo cáo KPI tháng", status: "pending", time: "10:00 AM", date: toDateKey() },
        { id: "task-2", title: "Họp phòng chuyên môn", status: "pending", time: "14:30 PM", date: toDateKey() },
        { id: "task-3", title: "Chuẩn bị tài liệu dự án ABC", status: "done", time: "08:30 AM", date: toDateKey() }
    ]);
    let settings = readStore(storageKeys.settings, {
        notifications: true,
        compact: false,
        language: "vi-VN",
        theme: "light",
        reducedMotion: false
    });
    let activeChat = "manager";
    let editingMessageId = null;

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

    function toDateKey(value) {
        const date = value ? new Date(value) : new Date();
        const year = date.getFullYear();
        const month = String(date.getMonth() + 1).padStart(2, "0");
        const day = String(date.getDate()).padStart(2, "0");
        return `${year}-${month}-${day}`;
    }

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

        if (normalized === "home") {
            loadMyViolations();
            loadMyTasks();
        } else if (normalized === "schedule") {
            loadMyTasks();
        } else if (normalized === "payroll") {
            loadMyPayrolls();
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

    const loadMyViolations = async () => {
        const countEl = document.getElementById("homeViolationCount");
        const listEl = document.getElementById("homeViolationList");
        const fullCountEl = document.getElementById("fullViolationCount");
        const fullListEl = document.getElementById("fullViolationList");
        
        try {
            const res = await fetch("/Employee/GetMyViolations");
            const result = await res.json();
            if (result.success && Array.isArray(result.data)) {
                const list = result.data;
                if (countEl) countEl.textContent = `${list.length} vi phạm`;
                if (fullCountEl) fullCountEl.textContent = list.length;

                // Home Tab Widget
                if (listEl) {
                    if (list.length === 0) {
                        listEl.innerHTML = `
                            <div class="text-center py-6 text-slate-400">
                                <i class="fa-solid fa-circle-check text-2xl text-green-500 mb-2"></i>
                                <p class="text-xs">Không có vi phạm ghi nhận</p>
                            </div>
                        `;
                    } else {
                        listEl.innerHTML = list.map(item => {
                            const date = new Date(item.detectedAtUtc).toLocaleDateString('vi-VN', {
                                day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit'
                            });
                            let severityClass = "bg-amber-100 text-amber-700";
                            if (item.severity?.toLowerCase() === "high" || item.severity?.toLowerCase() === "danger") {
                                severityClass = "bg-red-100 text-red-700";
                            } else if (item.severity?.toLowerCase() === "low") {
                                severityClass = "bg-slate-100 text-slate-700";
                            }

                            return `
                                <div class="flex items-center justify-between p-2 rounded-xl border border-slate-100 bg-white shadow-sm hover:shadow-md transition-all">
                                    <div class="min-w-0 flex-1">
                                        <div class="flex items-center gap-1.5">
                                            <span class="text-xs font-semibold text-slate-800 truncate">${item.violationType}</span>
                                            <span class="px-1.5 py-0.5 text-[9px] font-bold rounded-full ${severityClass}">${item.severity}</span>
                                        </div>
                                        <p class="text-[10px] text-slate-400 mt-0.5"><i class="fa-solid fa-camera mr-1"></i>${item.cameraLocation} • ${date}</p>
                                    </div>
                                    <span class="text-[10px] font-semibold ${item.status === "Approved" || item.status === "Đã duyệt" ? "text-green-600" : "text-amber-500"}">${item.status}</span>
                                </div>
                            `;
                        }).join("");
                    }
                }

                // Violations Tab Grid
                if (fullListEl) {
                    if (list.length === 0) {
                        fullListEl.innerHTML = `
                            <div class="col-span-full text-center py-12 text-slate-400">
                                <div class="inline-flex h-16 w-16 items-center justify-center rounded-full bg-green-50 mb-4 shadow-inner">
                                    <i class="fa-solid fa-circle-check text-2xl text-green-500"></i>
                                </div>
                                <p class="text-sm">Hồ sơ trong sạch. Chưa ghi nhận vi phạm nào.</p>
                            </div>
                        `;
                    } else {
                        fullListEl.innerHTML = list.map(item => {
                            const date = new Date(item.detectedAtUtc).toLocaleDateString('vi-VN', {
                                day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit'
                            });
                            let severityClass = "bg-amber-100 text-amber-700";
                            let iconClass = "fa-triangle-exclamation text-amber-500";
                            if (item.severity?.toLowerCase() === "high" || item.severity?.toLowerCase() === "danger") {
                                severityClass = "bg-red-100 text-red-700";
                                iconClass = "fa-ban text-red-500";
                            } else if (item.severity?.toLowerCase() === "low") {
                                severityClass = "bg-slate-100 text-slate-700";
                                iconClass = "fa-circle-info text-slate-500";
                            }

                            return `
                                <div class="employee-surface rounded-[1.5rem] border border-slate-100 bg-white p-5 shadow-sm hover:shadow-lg transition-all duration-300 hover:-translate-y-1 relative">
                                    <div class="flex justify-between items-start mb-3">
                                        <div class="h-10 w-10 rounded-xl bg-slate-50 flex items-center justify-center shadow-inner">
                                            <i class="fa-solid ${iconClass} text-lg"></i>
                                        </div>
                                        <span class="px-2 py-1 text-[10px] font-bold rounded-md ${severityClass} uppercase tracking-widest">${item.severity}</span>
                                    </div>
                                    <h4 class="font-outfit text-base font-bold text-slate-900 mb-1 line-clamp-2">${item.violationType}</h4>
                                    <div class="space-y-1 mt-3">
                                        <p class="text-[11px] text-slate-500"><i class="fa-solid fa-camera text-slate-400 mr-2 w-3 text-center"></i>${item.cameraLocation}</p>
                                        <p class="text-[11px] text-slate-500"><i class="fa-regular fa-clock text-slate-400 mr-2 w-3 text-center"></i>${date}</p>
                                        <p class="text-[11px] font-semibold mt-2 ${item.status === "Approved" || item.status === "Đã duyệt" ? "text-green-600" : "text-amber-500"}"><i class="fa-solid fa-circle-notch text-slate-400 mr-2 w-3 text-center"></i>${item.status}</p>
                                    </div>
                                </div>
                            `;
                        }).join("");
                    }
                }
            }
        } catch (err) {
            console.error("Failed to load violations", err);
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

        // Cập nhật trạng thái trang chủ
        const homeDot = document.getElementById("attendanceStatusDot");
        const homeText = document.getElementById("attendanceStatusText");
        const homeSubText = document.getElementById("attendanceStatusSubText");
        if (homeDot && homeText && homeSubText) {
            if (attendance.currentSession) {
                homeDot.className = "h-2 w-2 rounded-full bg-green-500 animate-pulse";
                homeText.textContent = "Đang làm việc";
                homeSubText.textContent = `Check-in lúc ${formatShortTime(attendance.currentSession.checkInAt)}`;
            } else {
                const todayKey = toDateKey();
                const todaySessions = attendance.sessions.filter(s => toDateKey(s.checkInAt) === todayKey);
                if (todaySessions.length > 0) {
                    homeDot.className = "h-2 w-2 rounded-full bg-slate-500";
                    homeText.textContent = "Đã check-out";
                    homeSubText.textContent = "Hoàn thành ca làm việc";
                } else {
                    homeDot.className = "h-2 w-2 rounded-full bg-amber-500";
                    homeText.textContent = "Chưa Check-in";
                    homeSubText.textContent = "Yêu cầu xác thực khuôn mặt";
                }
            }
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

    const renderRequests = async () => {
    const list = document.querySelector('[data-request-list]');
    if (!list) return;

    try {
        const res = await fetch('/Employee/GetMyRequests');
        const data = await res.json();
        if (data.success) {
            requests = data.data.map(r => ({
                type: r.requestType,
                date: new Date(r.submittedAt).toLocaleDateString('vi-VN'),
                content: r.content,
                status: r.status
            }));
            
            list.innerHTML = '';
            if (!requests.length) {
                list.innerHTML = '<div class="rounded-2xl bg-slate-50 p-4 text-sm text-slate-500">Chưa có đơn nào được tạo trong hệ thống.</div>';
                return;
            }

            requests.forEach((item) => {
                const card = document.createElement('div');
                let tone = 'bg-slate-50 text-slate-700';
                if (item.status === 'Đã duyệt' || item.status === 'Approved') tone = 'bg-green-50 text-green-700';
                else if (item.status === 'Từ chối' || item.status === 'Rejected') tone = 'bg-red-50 text-red-700';
                else tone = 'bg-amber-50 text-amber-700';
                
                card.className = 'rounded-2xl border border-slate-200 bg-white p-4';
                card.innerHTML = `<div class="flex items-center justify-between gap-3"><p class="font-semibold text-slate-900">${item.type}</p><span class="rounded-full px-2.5 py-1 text-xs font-semibold ${tone}">${item.status}</span></div><p class="mt-2 text-sm text-slate-500">${item.date}</p><p class="mt-2 text-sm text-slate-600">${item.content.replace(/\r?\n/g, '<br>')}</p>
                <div class="mt-3 flex justify-end">
                    <button type="button" class="text-xs font-semibold text-red-600 hover:text-red-700 hover:underline px-2 py-1 rounded-lg hover:bg-red-50 transition-colors">Xem chi tiết</button>
                </div>`;
                
                // Add detail button click handler
                const detailBtn = card.querySelector('button');
                detailBtn.onclick = () => {
                    const modal = document.getElementById("requestDetailModal");
                    const content = document.getElementById("requestDetailPreviewContent");
                    if (modal && content) {
                        content.innerHTML = item.content;
                        modal.classList.remove("hidden");
                        modal.classList.add("flex");
                    } else {
                        alert(`Chi tiết đơn:\n\nNội dung:\n${item.content}`);
                    }
                };
                
                list.appendChild(card);
            });
        }
    } catch (e) { console.error(e); }
};

const loadChatContacts = async () => {
    try {
        const res = await fetch("/Employee/GetChatContacts");
        const result = await res.json();
        if (result.success && Array.isArray(result.data)) {
            const listEl = document.getElementById("messageChannelList");
            if (!listEl) return;
            listEl.innerHTML = "";
            
            result.data.forEach(contact => {
                if (!chats[contact.username]) chats[contact.username] = [];
                
                const btn = document.createElement("button");
                btn.type = "button";
                btn.className = "w-full flex items-center gap-3 p-3 rounded-xl hover:bg-slate-50 transition text-left";
                if (activeChat === contact.username) {
                    btn.classList.add("bg-red-50");
                    btn.classList.remove("hover:bg-slate-50");
                }
                btn.dataset.chatTarget = contact.username;
                btn.dataset.chatName = contact.fullName;
                
                const avatarDiv = document.createElement("div");
                avatarDiv.className = "w-10 h-10 rounded-full bg-slate-100 flex items-center justify-center font-bold text-slate-600 overflow-hidden shrink-0";
                if (contact.avatarUrl) {
                    avatarDiv.innerHTML = `<img src="${contact.avatarUrl}" class="w-full h-full object-cover" />`;
                } else {
                    avatarDiv.textContent = contact.fullName.charAt(0).toUpperCase();
                }
                
                const infoDiv = document.createElement("div");
                infoDiv.className = "flex-1 min-w-0";
                
                const nameP = document.createElement("p");
                nameP.className = "text-sm font-bold text-slate-900 truncate";
                nameP.textContent = contact.fullName;
                
                const preP = document.createElement("p");
                preP.className = "text-xs text-slate-500 truncate";
                preP.dataset.chatPreview = contact.username;
                const msgs = chats[contact.username];
                preP.textContent = msgs && msgs.length > 0 ? (msgs[msgs.length-1].revoked ? "Đã thu hồi" : msgs[msgs.length-1].text) : "Chưa có tin nhắn";
                
                infoDiv.appendChild(nameP);
                infoDiv.appendChild(preP);
                btn.appendChild(avatarDiv);
                btn.appendChild(infoDiv);
                
                btn.addEventListener("click", () => {
                    listEl.querySelectorAll("[data-chat-target]").forEach(b => {
                        b.classList.remove("bg-red-50");
                        b.classList.add("hover:bg-slate-50");
                    });
                    btn.classList.add("bg-red-50");
                    btn.classList.remove("hover:bg-slate-50");
                    
                    activeChat = contact.username;
                    document.querySelector("[data-chat-title]").textContent = contact.fullName;
                    renderChats();
                });
                
                listEl.appendChild(btn);
            });
            
            if (result.data.length > 0 && (!activeChat || activeChat === 'manager')) {
                activeChat = result.data[0].username;
                document.querySelector("[data-chat-title]").textContent = result.data[0].fullName;
                listEl.querySelector(`[data-chat-target="${activeChat}"]`)?.classList.add("bg-red-50");
                listEl.querySelector(`[data-chat-target="${activeChat}"]`)?.classList.remove("hover:bg-slate-50");
                renderChats();
            }
        }
    } catch (e) { console.error(e); }
};

const loadMessages = async () => {
    try {
        const res = await fetch("/Employee/GetMyMessages");
        const result = await res.json();
        if (result.success && Array.isArray(result.data)) {
            chats = {}; // Reset toàn bộ
            result.data.forEach(m => {
                const ch = m.channel || "manager";
                if (!chats[ch]) chats[ch] = [];
                chats[ch].push({
                    id: m.id,
                    author: m.senderRole === "Employee" ? "self" : "other",
                    text: m.content,
                    revoked: m.isRevoked
                });
            });
            await loadChatContacts(); // Load danh bạ thực tế thay vì hardcode
            renderChats();
        }
    } catch (e) { console.error(e); }
};

    const clearMessageEditing = () => {
        editingMessageId = null;
        const chatEditBar = document.querySelector("[data-chat-edit-bar]");
        const chatEditLabel = document.querySelector("[data-chat-edit-label]");
        const chatInput = document.querySelector("[data-chat-input]");
        if (chatEditBar) {
            chatEditBar.classList.add("hidden");
            chatEditBar.classList.remove("flex");
        }
        if (chatEditLabel) chatEditLabel.textContent = "Đang chỉnh sửa tin nhắn";
        if (chatInput) chatInput.value = "";
    };

    const startMessageEditing = (message) => {
        editingMessageId = message.id;
        const chatEditBar = document.querySelector("[data-chat-edit-bar]");
        const chatEditLabel = document.querySelector("[data-chat-edit-label]");
        const chatInput = document.querySelector("[data-chat-input]");
        if (chatInput) {
            chatInput.value = message.text || "";
            chatInput.focus();
        }
        if (chatEditLabel) {
            chatEditLabel.textContent = `Đang chỉnh sửa: ${message.text || ""}`;
        }
        if (chatEditBar) {
            chatEditBar.classList.remove("hidden");
            chatEditBar.classList.add("flex");
        }
    };

    const renderChats = () => {
        const title = document.querySelector("[data-chat-title]");
        const thread = document.querySelector("[data-chat-thread]");
        if (!title || !thread) return;

        thread.innerHTML = "";

        (chats[activeChat] || []).forEach((message, index) => {
            const bubbleWrapper = document.createElement("div");
            bubbleWrapper.className = message.author === "self" ? "flex flex-col items-end group" : "flex flex-col items-start";
            
            const bubble = document.createElement("div");
            
            if (message.revoked) {
                bubble.className = message.author === "self"
                    ? "max-w-md rounded-2xl bg-slate-100 px-4 py-3 text-sm text-slate-400 italic border border-slate-200"
                    : "max-w-md rounded-2xl bg-slate-50 px-4 py-3 text-sm text-slate-400 italic border border-slate-100";
                bubble.textContent = "Tin nhắn đã bị thu hồi";
                bubbleWrapper.appendChild(bubble);
            } else {
                bubble.className = message.author === "self"
                    ? "max-w-md rounded-2xl bg-red-600 px-4 py-3 text-sm text-white"
                    : "max-w-md rounded-2xl bg-slate-100 px-4 py-3 text-sm text-slate-700";
                bubble.textContent = message.text;
                
                if (message.author === "self") {
                    const row = document.createElement("div");
                    row.className = "flex items-center gap-2";
                    
                    const revokeBtn = document.createElement("button");
                    revokeBtn.className = "text-[11px] text-slate-400 hover:text-red-600 font-medium transition-colors px-2 py-1";
                    revokeBtn.textContent = "Thu hồi";
                    revokeBtn.onclick = async () => {
                        if (confirm("Bạn có chắc chắn muốn thu hồi tin nhắn này?")) {
                            message.revoked = true;
                            renderChats(); // Cập nhật UI ngay lập tức
                            
                            if (!message.id || typeof message.id === 'string' && message.id.length > 10) {
                                // Nếu không có id hoặc là id ảo từ localStorage cũ, không cần gọi API
                                writeStore(storageKeys.chats, chats);
                                return;
                            }
                            
                            try {
                                const res = await fetch('/Employee/RevokeMessage', {
                                    method: 'POST',
                                    headers: { 'Content-Type': 'application/json' },
                                    body: JSON.stringify({ id: message.id })
                                });
                                const data = await res.json();
                                if (data.success) {
                                    loadMessages();
                                } else {
                                    alert(data.message || "Không thể thu hồi tin nhắn.");
                                    loadMessages(); // Phục hồi nếu lỗi
                                }
                            } catch (e) { console.error(e); loadMessages(); }
                        }
                    };

                    const editBtn = document.createElement("button");
                    editBtn.className = "text-[11px] text-slate-400 hover:text-blue-600 font-medium transition-colors px-2 py-1";
                    editBtn.textContent = "Chỉnh sửa";
                    editBtn.onclick = () => {
                        if (!message.id) message.id = Date.now().toString();
                        startMessageEditing(message);
                    };
                    
                    row.appendChild(editBtn);
                    row.appendChild(revokeBtn);
                    row.appendChild(bubble);
                    bubbleWrapper.appendChild(row);
                } else {
                    bubbleWrapper.appendChild(bubble);
                }
            }
            
            thread.appendChild(bubbleWrapper);
        });
    };

    const loadMyTasks = async () => {
        try {
            const res = await fetch("/Employee/GetMyTasks");
            const result = await res.json();
            if (result.success && Array.isArray(result.data)) {
                const fetchedTasks = result.data.map(t => ({
                    id: t.id,
                    title: t.title,
                    status: t.status.toLowerCase(),
                    time: new Date(t.dueDate).toLocaleTimeString('vi-VN', {hour: '2-digit', minute:'2-digit'}),
                    date: toDateKey(t.dueDate)
                }));
                // Combine or just use fetched
                tasks = fetchedTasks;
                renderTasks();
            }
        } catch (e) { console.error(e); }
    };

    const renderTasks = () => {
        const todayKey = toDateKey();
        const todayTasks = tasks.filter(t => t.date === todayKey || t.date < todayKey && t.status !== 'done');
        const doneTasks = todayTasks.filter(t => t.status === "done");
        
        // Schedule Tab
        const taskList = document.querySelector("[data-task-list]");
        const doneList = document.querySelector("[data-task-done-list]");
        const statsDone = document.querySelector("[data-task-stats-done]");
        const statsTotal = document.querySelector("[data-task-stats-total]");
        const progressBar = document.querySelector("[data-task-progress-bar]");
        const progressText = document.querySelector("[data-task-progress-text]");

        if (taskList) {
            taskList.innerHTML = "";
            const pendingTasks = todayTasks.filter(t => t.status !== "done");
            if (pendingTasks.length === 0) {
                taskList.innerHTML = '<div class="rounded-2xl bg-slate-50 p-4 text-sm text-slate-500">Tuyệt vời! Bạn đã hoàn thành hết công việc.</div>';
            } else {
                pendingTasks.forEach(task => {
                    const el = document.createElement("div");
                    el.className = "flex items-center justify-between rounded-2xl border border-slate-200 bg-white p-4 shadow-sm hover:border-red-300 transition-colors cursor-pointer";
                    el.innerHTML = `
                        <div class="flex items-center gap-4">
                            <input type="checkbox" onchange="window.markTaskDone('${task.id}')" data-task-checkbox="${task.id}" class="h-6 w-6 rounded-full border-slate-300 text-red-600 focus:ring-red-600 cursor-pointer" />
                            <div>
                                <p class="font-semibold text-slate-900">${task.title}</p>
                                <p class="text-xs text-slate-500"><i class="fa-regular fa-clock mr-1"></i>Hạn chót: ${task.time} ${task.date}</p>
                            </div>
                        </div>
                    `;
                    taskList.appendChild(el);
                });
            }
        }

        if (doneList) {
            doneList.innerHTML = "";
            if (doneTasks.length === 0) {
                doneList.innerHTML = '<div class="text-sm text-slate-500">Chưa hoàn thành công việc nào.</div>';
            } else {
                doneTasks.forEach(task => {
                    const el = document.createElement("div");
                    el.className = "flex items-center justify-between rounded-xl bg-slate-50 p-3 opacity-75";
                    el.innerHTML = `
                        <div class="flex items-center gap-3">
                            <i class="fa-solid fa-circle-check text-green-500"></i>
                            <p class="text-sm font-medium text-slate-600 line-through">${task.title}</p>
                        </div>
                    `;
                    doneList.appendChild(el);
                });
            }
        }

        if (statsTotal) {
            statsTotal.textContent = todayTasks.length;
            statsDone.textContent = doneTasks.length;
            const pct = todayTasks.length === 0 ? 0 : Math.round((doneTasks.length / todayTasks.length) * 100);
            progressBar.style.width = `${pct}%`;
            progressText.textContent = `${pct}% hoàn thành`;
        }

        // Home Tab
        const homeTotal = document.querySelector("[data-home-task-total]");
        const homePending = document.querySelector("[data-home-task-pending]");
        const homeDone = document.querySelector("[data-home-task-done]");
        const homeList = document.querySelector("[data-home-task-list]");

        if (homeTotal) homeTotal.textContent = todayTasks.length;
        if (homePending) homePending.textContent = todayTasks.length - doneTasks.length;
        if (homeDone) homeDone.textContent = doneTasks.length;

        if (homeList) {
            homeList.innerHTML = "";
            const recentTasks = todayTasks.slice(0, 3);
            if (recentTasks.length === 0) {
                homeList.innerHTML = '<div class="rounded-xl border border-slate-200 bg-slate-50 p-3 text-sm text-slate-500">Chưa có nhiệm vụ nào.</div>';
            } else {
                recentTasks.forEach(task => {
                    const el = document.createElement("div");
                    el.className = "flex items-center justify-between rounded-xl border border-slate-200 bg-white p-3";
                    el.innerHTML = `
                        <div class="flex items-center gap-3">
                            <i class="fa-solid ${task.status === "done" ? "fa-circle-check text-green-500" : "fa-circle text-slate-300"}"></i>
                            <p class="text-sm font-medium ${task.status === "done" ? "text-slate-500 line-through" : "text-slate-800"}">${task.title}</p>
                        </div>
                        <span class="text-xs font-semibold text-slate-400">${task.time}</span>
                    `;
                    homeList.appendChild(el);
                });
            }
        }
        
        renderScheduleCalendar();
    };

    const renderScheduleCalendar = () => {
        const cal = document.getElementById("scheduleCalendar");
        if (!cal) return;
        cal.innerHTML = "";
        const now = new Date();
        const year = now.getFullYear();
        const month = now.getMonth();
        const daysInMonth = new Date(year, month + 1, 0).getDate();
        const firstDay = new Date(year, month, 1).getDay();
        const startOffset = firstDay === 0 ? 6 : firstDay - 1;

        const dayNames = ["T2", "T3", "T4", "T5", "T6", "T7", "CN"];
        dayNames.forEach(d => {
            const el = document.createElement("div");
            el.className = "bg-slate-100 p-2 text-center text-xs font-semibold text-slate-500 uppercase tracking-wider";
            el.textContent = d;
            cal.appendChild(el);
        });

        for (let i = 0; i < startOffset; i++) {
            const el = document.createElement("div");
            el.className = "bg-white p-2 min-h-[60px]";
            cal.appendChild(el);
        }

        const todayKey = toDateKey(now);
        for (let d = 1; d <= daysInMonth; d++) {
            const dateObj = new Date(year, month, d);
            const dateStr = toDateKey(dateObj);
            const dayTasks = tasks.filter(t => t.date === dateStr);
            const isToday = dateStr === todayKey;

            const el = document.createElement("div");
            el.className = `bg-white p-2 min-h-[80px] border-t border-slate-100 flex flex-col gap-1 transition-colors hover:bg-slate-50 cursor-pointer ${isToday ? "ring-2 ring-inset ring-red-500 rounded-lg relative z-10 bg-red-50" : ""}`;
            
            const dateSpan = document.createElement("span");
            dateSpan.className = `text-sm font-semibold ${isToday ? "text-red-600 font-bold" : "text-slate-700"}`;
            dateSpan.textContent = d;
            
            const headerDiv = document.createElement("div");
            headerDiv.className = "flex items-center justify-between";
            headerDiv.appendChild(dateSpan);
            el.appendChild(headerDiv);

            dayTasks.slice(0, 2).forEach(t => {
                const isDone = t.status === "done";
                const taskDiv = document.createElement("div");
                taskDiv.className = `text-[10px] leading-tight px-1.5 py-1 rounded truncate ${isDone ? "bg-slate-100 text-slate-400 line-through" : "bg-red-50 text-red-700 font-medium"}`;
                taskDiv.textContent = t.title;
                el.appendChild(taskDiv);
            });
            
            if (dayTasks.length > 2) {
                const moreDiv = document.createElement("div");
                moreDiv.className = "text-[10px] text-slate-400 font-medium px-1";
                moreDiv.textContent = `+${dayTasks.length - 2} việc nữa`;
                el.appendChild(moreDiv);
            }

            el.addEventListener("click", () => {
                if (dayTasks.length > 0) {
                    alert(`Ngày ${d}/${month + 1}/${year}:\n` + dayTasks.map(t => `- ${t.title}`).join('\n'));
                } else {
                    if (confirm("Chưa có công việc trong ngày này. Bạn có muốn thêm mới?")) {
                        const modal = document.querySelector("[data-task-add-modal]");
                        if (modal) {
                            modal.classList.remove("hidden");
                            modal.classList.add("flex");
                            document.getElementById("newTaskDate").value = `${year}-${String(month + 1).padStart(2, '0')}-${String(d).padStart(2, '0')}T08:00`;
                        }
                    }
                }
            });

            cal.appendChild(el);
        }
    };

    window.markTaskDone = async (id) => {
        try {
            await fetch(`/Employee/MarkTaskDone?id=${id}`, { method: 'POST' });
            loadMyTasks();
        } catch (e) { console.error(e); }
    };

    const loadMyPayrolls = async () => {
        try {
            const res = await fetch(`/Employee/GetMyPayrolls?year=2026`);
            const result = await res.json();
            if (result.success && result.data.length > 0) {
                const latest = result.data[0];
                document.getElementById('empBaseSalaryTop').textContent = latest.baseSalary.toLocaleString('vi-VN') + ' ₫';
                document.getElementById('empKpiBonusTop').textContent = '+' + latest.kpiBonus.toLocaleString('vi-VN') + ' ₫';
                document.getElementById('empDeductionTop').textContent = '-' + latest.violationDeduction.toLocaleString('vi-VN') + ' ₫';
                document.getElementById('empNetSalaryTop').textContent = latest.netSalary.toLocaleString('vi-VN') + ' ₫';
                
                document.getElementById('empBaseSalary').textContent = latest.baseSalary.toLocaleString('vi-VN') + ' ₫';
                document.getElementById('empKpiBonus').textContent = '+' + latest.kpiBonus.toLocaleString('vi-VN') + ' ₫';
                document.getElementById('empDeduction').textContent = '-' + latest.violationDeduction.toLocaleString('vi-VN') + ' ₫';

                const listEl = document.getElementById('payrollHistoryList');
                if (listEl) {
                    listEl.innerHTML = result.data.map(p => `
                        <div class="rounded-xl border border-slate-100 bg-slate-50 p-4 flex items-center justify-between group hover:border-emerald-200 transition cursor-pointer">
                            <div class="flex items-center gap-3">
                                <div class="w-10 h-10 rounded-full bg-white border border-slate-200 flex items-center justify-center ${p.status === 'Đã thanh toán' ? 'text-emerald-500' : 'text-slate-400'} font-bold font-outfit shadow-sm">
                                    ${String(p.month).padStart(2, '0')}
                                </div>
                                <div>
                                    <p class="text-sm font-bold text-slate-900">Tháng ${String(p.month).padStart(2, '0')}/${p.year}</p>
                                    <p class="text-[10px] ${p.status === 'Đã thanh toán' ? 'text-emerald-600' : 'text-slate-500'} mt-0.5">${p.status}</p>
                                </div>
                            </div>
                            <div class="text-right">
                                <p class="text-sm font-bold text-slate-900">${p.netSalary.toLocaleString('vi-VN')} ₫</p>
                            </div>
                        </div>
                    `).join('');
                }
            } else {
                const listEl = document.getElementById('payrollHistoryList');
                if (listEl) listEl.innerHTML = '<div class="p-4 text-center text-sm text-slate-400">Chưa có dữ liệu lương.</div>';
            }
        } catch (e) { console.error(e); }
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

    const ensureBiometricRegistration = async () => {
        const response = await fetch(biometricRegistrationStatusUrl, {
            method: "GET",
            headers: {
                "X-Requested-With": "XMLHttpRequest"
            }
        });

        if (!response.ok) {
            throw new Error("Không thể kiểm tra trạng thái sinh trắc học.");
        }

        return response.json();
    };

    const processAttendanceAction = async (action) => {
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

        try {
            const biometricStatus = await ensureBiometricRegistration();
            if (!biometricStatus.success || !biometricStatus.hasBiometricRegistration) {
                const message = biometricStatus.message || "Bạn chưa đăng ký sinh trắc học. Vui lòng cập nhật ở Cài đặt hồ sơ.";
                alert(message);
                if (attendanceMessage) attendanceMessage.textContent = message;
                return;
            }
        } catch (error) {
            const message = error.message || "Không thể kiểm tra trạng thái sinh trắc học.";
            alert(message);
            if (attendanceMessage) attendanceMessage.textContent = message;
            return;
        }

        const nowIso = new Date().toISOString();
        attendance.lastCapture = null;

        if (action === "checkin") {
            attendance.currentSession = {
                id: `session-${Date.now()}`,
                checkInAt: nowIso,
                checkInImage: null
            };
            attendance.status = "Đang làm việc";
            if (attendanceMessage) attendanceMessage.textContent = "Check-in thành công. Tài khoản đã có dữ liệu sinh trắc học.";
            try {
                await fetch('/Employee/CheckIn', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ imageDataUrl: null })
                });
            } catch (e) { console.error(e); }
        } else {
            attendance.sessions.push({
                id: attendance.currentSession.id,
                checkInAt: attendance.currentSession.checkInAt,
                checkOutAt: nowIso,
                checkInImage: attendance.currentSession.checkInImage || null,
                checkOutImage: null,
                status: "Đã check-out"
            });
            attendance.currentSession = null;
            attendance.status = "Đã check-out";
            if (attendanceMessage) attendanceMessage.textContent = "Check-out thành công. Tài khoản đã có dữ liệu sinh trắc học.";
            try {
                await fetch('/Employee/CheckOut', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ imageDataUrl: null })
                });
            } catch (e) { console.error(e); }
        }

        saveAttendance();
        renderAttendance();
    };

    const openAttendanceCameraModal = async (action) => {
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

        try {
            const biometricStatus = await ensureBiometricRegistration();
            if (!biometricStatus.success || !biometricStatus.hasBiometricRegistration) {
                const message = biometricStatus.message || "Bạn chưa đăng ký sinh trắc học. Vui lòng cập nhật ở Cài đặt hồ sơ.";
                alert(message);
                if (attendanceMessage) attendanceMessage.textContent = message;
                return;
            }
        } catch (error) {
            const message = error.message || "Không thể kiểm tra trạng thái sinh trắc học.";
            alert(message);
            if (attendanceMessage) attendanceMessage.textContent = message;
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
                try {
                    await fetch('/Employee/CheckIn', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ imageDataUrl: imageDataUrl })
                    });
                } catch (e) { console.error(e); }
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
                try {
                    await fetch('/Employee/CheckOut', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ imageDataUrl: imageDataUrl })
                    });
                } catch (e) { console.error(e); }
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


    document.querySelector("[data-chat-input]")?.addEventListener("keypress", (e) => {
        if (e.key === "Enter") {
            e.preventDefault();
            document.querySelector("[data-chat-send]")?.click();
        }
    });

    document.querySelector("[data-chat-send]")?.addEventListener("click", async () => {
        const input = document.querySelector("[data-chat-input]");
        const text = input?.value.trim();
        if (!text) return;
        chats[activeChat] = chats[activeChat] || [];
        
        if (editingMessageId) {
            const msg = chats[activeChat].find(m => m.id === editingMessageId);
            if (msg) msg.text = text;
            clearMessageEditing();
        } else {
            chats[activeChat].push({ author: "self", text, id: Date.now().toString() });
        }
        writeStore(storageKeys.chats, chats);
        if (!editingMessageId) input.value = "";
        renderChats();
        
        try {
            await fetch(editingMessageId ? '/Employee/EditMessage' : '/Employee/SendMessage', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(editingMessageId ? { id: editingMessageId, content: text } : { channel: activeChat, title: "Gửi " + activeChat, content: text })
            });
            loadMessages(); // Reload from DB after sending
        } catch (e) { console.error(e); }
    });

    document.querySelector("[data-chat-edit-cancel]")?.addEventListener("click", () => {
        clearMessageEditing();
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
        const selectedOption = typeEl.options[typeEl.selectedIndex];
        const code = selectedOption ? selectedOption.getAttribute('data-code') : '';
        const dateRaw = dateEl?.value;
        const dateStr = dateRaw ? new Date(dateRaw).toLocaleDateString("vi-VN") : "[Ngày/Tháng/Năm]";
        const reason = reasonEl?.value.trim() || "[Nhập lý do chi tiết...]";
        const name = profile.name || "[Tên nhân viên]";
        const department = profile.department || "[Bộ phận]";

        const builder = buildDoc[code] || buildDoc["Nghỉ phép"];
        previewEl.innerHTML = builder(name, department, dateStr, reason);
    };

    document.querySelector("[data-request-type]")?.addEventListener("change", updateRequestPreview);
    document.querySelector("[data-request-date]")?.addEventListener("change", updateRequestPreview);
    document.querySelector("[data-request-reason]")?.addEventListener("input", updateRequestPreview);

    document.querySelector('[data-request-submit]')?.addEventListener('click', async () => {
    const type = document.querySelector('[data-request-type]')?.value || '';
    const date = document.querySelector('[data-request-date]')?.value || '';
    const reason = document.querySelector('[data-request-reason]')?.value.trim() || '';
    const message = document.querySelector('[data-request-message]');

    if (!type || type === "") {
        if (message) message.textContent = 'Vui lòng chọn mẫu đơn.';
        return;
    }

    if (!reason) {
        if (message) message.textContent = 'Vui lòng nhập lý do trước khi gửi đơn.';
        return;
    }

    try {
        const res = await fetch('/Employee/SendRequest', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ TemplateId: parseInt(type, 10), RequestedDate: date, Reason: reason })
        });
        const data = await res.json();
        if (data.success) {
            const reasonInput = document.querySelector('[data-request-reason]');
            if (reasonInput) reasonInput.value = '';
            if (message) message.textContent = 'Đơn đã được gửi thành công.';
            renderRequests();
        }
    } catch (e) {
        if (message) message.textContent = 'Lỗi kết nối khi gửi đơn.';
    }
});

    document.querySelector("[data-request-draft]")?.addEventListener("click", () => {
        const typeEl = document.querySelector("[data-request-type]");
        const typeValue = typeEl?.value || "";
        const selectedOption = typeEl?.options[typeEl.selectedIndex];
        const typeText = selectedOption ? selectedOption.text : "";
        const date = document.querySelector("[data-request-date]")?.value || "";
        const reason = document.querySelector("[data-request-reason]")?.value.trim() || "";
        const previewEl = document.querySelector("[data-request-preview]");
        const content = previewEl ? previewEl.textContent : "";
        const message = document.querySelector("[data-request-message]");

        if (!typeValue) {
            if (message) message.textContent = "Vui lòng chọn mẫu đơn.";
            return;
        }

        if (!reason) {
            if (message) message.textContent = "Vui lòng nhập lý do trước khi lưu nháp.";
            return;
        }

        requests.push({ type: typeText, date, content, status: "Lưu nháp" });
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

    document.querySelector("[data-profile-change-pwd]")?.addEventListener("click", () => {
        const oldPwd = document.querySelector("[data-profile-pwd-old]")?.value;
        const newPwd = document.querySelector("[data-profile-pwd-new]")?.value;
        const confirmPwd = document.querySelector("[data-profile-pwd-confirm]")?.value;
        const msg = document.querySelector("[data-profile-pwd-msg]");
        
        if (!msg) return;

        if (!oldPwd || !newPwd || !confirmPwd) {
            msg.className = "text-xs font-semibold text-red-600";
            msg.innerHTML = '<i class="fa-solid fa-triangle-exclamation mr-1"></i>Vui lòng nhập đầy đủ thông tin.';
            msg.classList.remove("hidden");
            return;
        }

        if (newPwd !== confirmPwd) {
            msg.className = "text-xs font-semibold text-red-600";
            msg.innerHTML = '<i class="fa-solid fa-triangle-exclamation mr-1"></i>Mật khẩu xác nhận không khớp.';
            msg.classList.remove("hidden");
            return;
        }

        if (newPwd.length < 8) {
            msg.className = "text-xs font-semibold text-red-600";
            msg.innerHTML = '<i class="fa-solid fa-triangle-exclamation mr-1"></i>Mật khẩu mới phải từ 8 ký tự.';
            msg.classList.remove("hidden");
            return;
        }

        // Giả lập call API thành công
        msg.className = "text-xs font-semibold text-emerald-600";
        msg.innerHTML = '<i class="fa-solid fa-circle-check mr-1"></i>Đổi mật khẩu thành công!';
        msg.classList.remove("hidden");

        document.querySelector("[data-profile-pwd-old]").value = "";
        document.querySelector("[data-profile-pwd-new]").value = "";
        document.querySelector("[data-profile-pwd-confirm]").value = "";
        
        setTimeout(() => {
            msg.classList.add("hidden");
        }, 3000);
    });

    // Mở khóa chỉnh sửa thông tin nhạy cảm
    document.querySelectorAll('[data-profile-input="cccd"], [data-profile-input="bank"]').forEach(input => {
        const btn = input.nextElementSibling;
        if (btn && btn.tagName === 'BUTTON') {
            btn.addEventListener('click', () => {
                const isLocked = input.disabled;
                if (isLocked) {
                    const pin = prompt("Vui lòng nhập mã PIN bảo mật hoặc OTP (demo: 1234) để chỉnh sửa:");
                    if (pin === "1234") {
                        input.disabled = false;
                        input.classList.remove("bg-slate-100", "text-slate-600");
                        input.classList.add("bg-white", "text-slate-900", "focus:border-red-300", "focus:ring-2", "focus:ring-red-100");
                        if (input.dataset.profileInput === "cccd") input.value = "079012345123";
                        if (input.dataset.profileInput === "bank") input.value = "1903123456456";
                        btn.innerHTML = '<i class="fa-solid fa-lock-open text-emerald-500"></i>';
                        btn.title = "Đang mở khóa";
                    } else if (pin) {
                        alert("Mã PIN không đúng!");
                    }
                } else {
                    input.disabled = true;
                    input.classList.add("bg-slate-100", "text-slate-600");
                    input.classList.remove("bg-white", "text-slate-900", "focus:border-red-300", "focus:ring-2", "focus:ring-red-100");
                    if (input.dataset.profileInput === "cccd") input.value = "079*****123";
                    if (input.dataset.profileInput === "bank") input.value = "190*****456 (Techcombank)";
                    btn.innerHTML = '<i class="fa-solid fa-pen-to-square"></i>';
                    btn.title = "Mở khóa chỉnh sửa";
                }
            });
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

            compressImage(file, async (dataUrl) => {
                avatarDataUrl = dataUrl;
                try {
                    const formData = new FormData();
                    formData.append("avatarBase64", dataUrl);
                    formData.append("fileName", file.name);

                    const res = await fetch("/Employee/UploadAvatar", {
                        method: "POST",
                        body: formData
                    });
                    const result = await res.json();
                    if (result.success && result.avatarUrl) {
                        document.querySelectorAll("[data-avatar-image]").forEach(img => {
                            img.src = result.avatarUrl;
                            img.classList.remove("hidden");
                        });
                        document.querySelectorAll("[data-avatar-fallback]").forEach(icon => {
                            icon.classList.add("hidden");
                        });
                    } else {
                        const msg = document.querySelector("[data-avatar-message]");
                        if (msg) msg.textContent = result.message || "Lỗi lưu ảnh.";
                    }
                } catch(e) {
                    console.error(e);
                }
                event.target.value = "";
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

    // --- Add Task Modal Logic ---
    const addTaskTrigger = document.querySelector("[data-task-add-trigger]");
    const addTaskModal = document.querySelector("[data-task-add-modal]");
    const addTaskCloseBtn = document.querySelector("[data-task-add-close]");
    const submitNewTaskBtn = document.getElementById("submitNewTaskBtn");

    if (addTaskModal) {
        document.body.appendChild(addTaskModal);
    }

    addTaskTrigger?.addEventListener("click", () => {
        if (!addTaskModal) return;
        const titleEl = document.getElementById("newTaskTitle");
        const descEl = document.getElementById("newTaskDesc");
        const dateEl = document.getElementById("newTaskDate");
        if (titleEl) titleEl.value = "";
        if (descEl) descEl.value = "";
        if (dateEl) dateEl.value = toDateKey();
        
        addTaskModal.classList.remove("hidden");
        addTaskModal.style.display = "flex";
        setTimeout(() => {
            const content = addTaskModal.querySelector(".saas-card");
            if (content) {
                content.style.transform = "scale(1)";
                content.style.opacity = "1";
            }
            if (titleEl) titleEl.focus();
        }, 10);
    });

    const closeAddTaskModal = () => {
        if (!addTaskModal) return;
        const content = addTaskModal.querySelector(".saas-card");
        if (content) {
            content.style.transform = "scale(0.95)";
            content.style.opacity = "0";
        }
        setTimeout(() => {
            addTaskModal.style.display = "none";
            addTaskModal.classList.add("hidden");
        }, 300);
    };

    addTaskCloseBtn?.addEventListener("click", closeAddTaskModal);

    submitNewTaskBtn?.addEventListener("click", async () => {
        const title = document.getElementById("newTaskTitle")?.value;
        const desc = document.getElementById("newTaskDesc")?.value;
        const date = document.getElementById("newTaskDate")?.value;

        if (!title) {
            alert("Vui lòng nhập tiêu đề công việc.");
            return;
        }

        const payload = {
            Title: title,
            Description: desc,
            DueDate: date ? new Date(date).toISOString() : new Date().toISOString()
        };

        try {
            const res = await fetch("/Employee/AddTask", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(payload)
            });
            const result = await res.json();
            if (result.success) {
                closeAddTaskModal();
                if (typeof loadMyTasks === "function") loadMyTasks();
            } else {
                alert(result.message || "Không thể thêm công việc");
            }
        } catch (e) {
            console.error("Error adding task:", e);
        }
    });

    // --- Task Event Listeners ---
    document.addEventListener("change", (e) => {
        if (e.target.matches("[data-task-checkbox]")) {
            const id = e.target.dataset.taskCheckbox;
            const task = tasks.find(t => t.id === id);
            if (task) {
                task.status = e.target.checked ? "done" : "pending";
                writeStore(storageKeys.tasks, tasks);
                renderTasks();
            }
        }
    });

    document.addEventListener("click", (e) => {
        const undoBtn = e.target.closest("[data-task-undo]");
        if (undoBtn) {
            const id = undoBtn.dataset.taskUndo;
            const task = tasks.find(t => t.id === id);
            if (task) {
                task.status = "pending";
                writeStore(storageKeys.tasks, tasks);
                renderTasks();
            }
        }
        
        const addMockBtn = e.target.closest("[data-task-add-mock]");
        if (addMockBtn) {
            const newTask = {
                id: `task-${Date.now()}`,
                title: `Nhiệm vụ mới ${Math.floor(Math.random() * 1000)}`,
                status: "pending",
                time: new Date().toLocaleTimeString('vi-VN', {hour: '2-digit', minute:'2-digit'}),
                date: toDateKey()
            };
            tasks.push(newTask);
            writeStore(storageKeys.tasks, tasks);
            renderTasks();
        }
    });

    // --- Security / Profile Updates ---
    document.querySelector("[data-profile-change-pwd]")?.addEventListener("click", async () => {
        const oldPwd = document.querySelector("[data-profile-pwd-old]")?.value;
        const newPwd = document.querySelector("[data-profile-pwd-new]")?.value;
        const confirmPwd = document.querySelector("[data-profile-pwd-confirm]")?.value;
        const msg = document.querySelector("[data-profile-pwd-msg]");

        if (!oldPwd || !newPwd || !confirmPwd) {
            msg.textContent = "Vui lòng điền đủ thông tin mật khẩu.";
            msg.className = "text-xs font-semibold mt-2 text-red-600 block";
            return;
        }

        if (newPwd !== confirmPwd) {
            msg.textContent = "Mật khẩu xác nhận không khớp.";
            msg.className = "text-xs font-semibold mt-2 text-red-600 block";
            return;
        }

        msg.textContent = "Đang cập nhật...";
        msg.className = "text-xs font-semibold mt-2 text-amber-600 block";

        try {
            const res = await fetch("/Account/ChangePassword", {
                method: "POST",
                headers: { "Content-Type": "application/x-www-form-urlencoded" },
                body: new URLSearchParams({ oldPassword: oldPwd, newPassword: newPwd })
            });
            const data = await res.json();
            
            if (data.success) {
                msg.textContent = "Đổi mật khẩu thành công!";
                msg.className = "text-xs font-semibold mt-2 text-green-600 block";
                document.querySelector("[data-profile-pwd-old]").value = "";
                document.querySelector("[data-profile-pwd-new]").value = "";
                document.querySelector("[data-profile-pwd-confirm]").value = "";
            } else {
                msg.textContent = data.message || "Đổi mật khẩu thất bại.";
                msg.className = "text-xs font-semibold mt-2 text-red-600 block";
            }
        } catch (e) {
            msg.textContent = "Lỗi kết nối máy chủ.";
            msg.className = "text-xs font-semibold mt-2 text-red-600 block";
        }
    });

    document.getElementById("closeRequestDetailModalBtn")?.addEventListener("click", () => {
        const modal = document.getElementById("requestDetailModal");
        if(modal) {
            modal.classList.add("hidden");
            modal.classList.remove("flex");
        }
    });

    document.getElementById("closeRequestDetailModalBtn2")?.addEventListener("click", () => {
        const modal = document.getElementById("requestDetailModal");
        if(modal) {
            modal.classList.add("hidden");
            modal.classList.remove("flex");
        }
    });

    document.getElementById("submitNewTaskBtn")?.addEventListener("click", async () => {
        const title = document.getElementById("newTaskTitle")?.value.trim();
        const desc = document.getElementById("newTaskDesc")?.value.trim();
        const date = document.getElementById("newTaskDate")?.value;
        if (!title || !date) {
            alert("Vui lòng nhập tiêu đề và hạn chót.");
            return;
        }
        
        try {
            const res = await fetch('/Employee/AddTask', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ title, description: desc, dueDate: new Date(date).toISOString() })
            });
            const result = await res.json();
            if (result.success) {
                document.querySelector("[data-task-add-modal]")?.classList.add("hidden");
                document.querySelector("[data-task-add-modal]")?.classList.remove("flex");
                
                document.getElementById("newTaskTitle").value = "";
                document.getElementById("newTaskDesc").value = "";
                document.getElementById("newTaskDate").value = "";
                
                loadMyTasks();
            } else {
                alert(result.message || "Lỗi thêm công việc");
            }
        } catch (e) { console.error(e); }
    });

    // --- Face Update ---
    const faceModal = document.querySelector("[data-face-modal]");
    const faceVideo = document.querySelector("[data-face-video]");
    const faceCanvas = document.querySelector("[data-face-canvas]");
    const faceStatus = document.querySelector("[data-face-status]");
    const faceInstruction = document.querySelector("[data-face-instruction]");
    const faceCaptureBtn = document.querySelector("[data-face-capture]");
    
    let faceStream = null;
    let faceImages = [];
    const maxFaces = 4;
    const instructions = [
        "Nhìn thẳng trực diện vào camera và nhấn chụp.",
        "Hơi quay mặt sang TRÁI (khoảng 30 độ) và nhấn chụp.",
        "Hơi quay mặt sang PHẢI (khoảng 30 độ) và nhấn chụp.",
        "Ngước mặt LÊN một chút và nhấn chụp."
    ];

    const openFaceModal = async () => {
        faceImages = [];
        updateFaceStepUI(0);
        faceModal?.classList.remove("hidden");
        faceModal?.classList.add("flex");
        
        if (faceVideo) {
            try {
                faceStatus?.classList.remove("hidden");
                faceStream = await navigator.mediaDevices.getUserMedia({ video: { width: 640, height: 480 } });
                faceVideo.srcObject = faceStream;
                faceStatus?.classList.add("hidden");
            } catch {
                if (faceInstruction) faceInstruction.textContent = "Lỗi camera. Vui lòng cấp quyền truy cập.";
            }
        }
    };

    const closeFaceModal = () => {
        if (faceStream) {
            faceStream.getTracks().forEach(t => t.stop());
            faceStream = null;
        }
        if (faceVideo) faceVideo.srcObject = null;
        faceModal?.classList.add("hidden");
        faceModal?.classList.remove("flex");
    };

    const updateFaceStepUI = (step) => {
        for (let i = 0; i < maxFaces; i++) {
            const el = document.getElementById(`step-${i}`);
            if (!el) continue;
            if (i === step) {
                el.className = "flex-1 rounded-lg bg-red-600/20 py-2 text-red-400 border border-red-600/30 transition-colors duration-300";
            } else if (i < step) {
                el.className = "flex-1 rounded-lg bg-green-500/20 py-2 text-green-400 border border-green-500/30 transition-colors duration-300";
                el.innerHTML = '<i class="fa-solid fa-check"></i>';
            } else {
                el.className = "flex-1 rounded-lg bg-slate-800 py-2 text-slate-400 transition-colors duration-300";
            }
        }
        if (faceInstruction && step < maxFaces) {
            faceInstruction.textContent = instructions[step];
        }
    };

    document.querySelector("[data-profile-update-face]")?.addEventListener("click", openFaceModal);
    document.querySelector("[data-face-close]")?.addEventListener("click", closeFaceModal);

    faceCaptureBtn?.addEventListener("click", async () => {
        if (faceImages.length >= maxFaces || !faceVideo || !faceCanvas) return;
        
        const ctx = faceCanvas.getContext("2d");
        faceCanvas.width = faceVideo.videoWidth;
        faceCanvas.height = faceVideo.videoHeight;
        ctx.drawImage(faceVideo, 0, 0, faceCanvas.width, faceCanvas.height);
        
        faceImages.push(faceCanvas.toDataURL("image/jpeg", 0.85));
        
        if (faceImages.length < maxFaces) {
            updateFaceStepUI(faceImages.length);
        } else {
            // Done capturing 4 images, send to backend
            updateFaceStepUI(4);
            if (faceInstruction) faceInstruction.textContent = "Đang xử lý và cập nhật dữ liệu sinh trắc học...";
            faceStatus?.classList.remove("hidden");
            faceCaptureBtn.disabled = true;
            
            const payload = faceImages.join(";base64split;");
            
            try {
                const res = await fetch("/Account/UpdateFace", {
                    method: "POST",
                    headers: { "Content-Type": "application/x-www-form-urlencoded" },
                    body: new URLSearchParams({ faceImagesBase64: payload })
                });
                const data = await res.json();
                
                if (data.success) {
                    alert("Cập nhật dữ liệu khuôn mặt thành công!");
                    closeFaceModal();
                } else {
                    alert(data.message || "Cập nhật thất bại.");
                    faceImages = [];
                    updateFaceStepUI(0);
                }
            } catch (e) {
                alert("Lỗi kết nối máy chủ.");
                faceImages = [];
                updateFaceStepUI(0);
            } finally {
                faceStatus?.classList.add("hidden");
                faceCaptureBtn.disabled = false;
            }
        }
    });

    // --- Payroll PIN Logic ---
    const unlockPayrollBtn = document.getElementById("unlockPayrollBtn");
    const payrollPinModal = document.getElementById("payrollPinModal");
    const closePayrollPinModal = document.getElementById("closePayrollPinModal");
    const verifyPayrollPinBtn = document.getElementById("verifyPayrollPinBtn");
    const payrollPinInput = document.getElementById("payrollPinInput");
    const payrollPinError = document.getElementById("payrollPinError");
    const payrollPinModalContent = document.getElementById("payrollPinModalContent");
    
    if (payrollPinModal) {
        document.body.appendChild(payrollPinModal);
    }

    const openPayrollModal = () => {
        if (!hasPayrollPin) {
            alert("Bạn chưa đăng ký mã PIN. Vui lòng vào mục Cài đặt -> Phiên làm việc & Bảo mật để đăng ký mã PIN trước khi xem lương.");
            const settingsTabTrigger = document.querySelector('[data-tab-trigger="settings"]');
            if (settingsTabTrigger) settingsTabTrigger.click();
            return;
        }
        if (!payrollPinModal) return;
        payrollPinInput.value = "";
        payrollPinError.classList.add("hidden");
        payrollPinModal.classList.remove("hidden");
        payrollPinModal.style.display = "flex";
        setTimeout(() => {
            if (payrollPinModalContent) {
                payrollPinModalContent.style.transform = "scale(1)";
                payrollPinModalContent.style.opacity = "1";
            }
            payrollPinInput?.focus();
        }, 10);
    };

    const closePayrollModal = () => {
        if (!payrollPinModal) return;
        if (payrollPinModalContent) {
            payrollPinModalContent.style.transform = "scale(0.95)";
            payrollPinModalContent.style.opacity = "0";
        }
        setTimeout(() => {
            payrollPinModal.style.display = "none";
            payrollPinModal.classList.add("hidden");
        }, 300);
    };

    unlockPayrollBtn?.addEventListener("click", openPayrollModal);
    closePayrollPinModal?.addEventListener("click", closePayrollModal);
    
    payrollPinInput?.addEventListener("keypress", (e) => {
        if (e.key === "Enter") verifyPayrollPinBtn?.click();
    });

    verifyPayrollPinBtn?.addEventListener("click", async () => {
        const pin = payrollPinInput?.value;
        if (!pin) {
            if (payrollPinError) {
                payrollPinError.textContent = "Vui lòng nhập mã PIN";
                payrollPinError.classList.remove("hidden");
            }
            payrollPinInput.focus();
            return;
        }
        
        try {
            const res = await fetch("/Employee/VerifyPayrollPin", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ pin: pin })
            });
            const result = await res.json();
            
            if (result.success) {
                closePayrollModal();
                document.querySelectorAll(".payroll-secure-data").forEach(el => {
                    el.classList.remove("blur-md", "select-none");
                });
                if (unlockPayrollBtn) {
                    unlockPayrollBtn.innerHTML = '<i class="fa-solid fa-unlock mr-2 text-green-400"></i>Đã mở khóa';
                    unlockPayrollBtn.classList.remove("bg-slate-800", "hover:bg-slate-700");
                    unlockPayrollBtn.classList.add("bg-green-600", "hover:bg-green-700");
                    setTimeout(() => {
                        unlockPayrollBtn.classList.add("hidden");
                    }, 2000);
                }
            } else {
                if (payrollPinError) {
                    payrollPinError.textContent = result.message || "Mã PIN không đúng!";
                    payrollPinError.classList.remove("hidden");
                }
                payrollPinInput.value = "";
                payrollPinInput.focus();
            }
        } catch (e) { console.error(e); }
    });

    // --- Setup Payroll PIN Logic ---
    const setupPayrollPinModal = document.getElementById("setupPayrollPinModal");
    const setupPayrollPinModalContent = document.getElementById("setupPayrollPinModalContent");
    const closeSetupPayrollPinModal = document.getElementById("closeSetupPayrollPinModal");
    const submitSetupPayrollPinBtn = document.getElementById("submitSetupPayrollPinBtn");
    const newPayrollPinInput = document.getElementById("newPayrollPinInput");
    const confirmPayrollPinInput = document.getElementById("confirmPayrollPinInput");
    const setupPayrollPinError = document.getElementById("setupPayrollPinError");
    
    document.getElementById("btnOpenPayrollPinSetup")?.addEventListener("click", () => {
        if (!setupPayrollPinModal) return;
        newPayrollPinInput.value = "";
        confirmPayrollPinInput.value = "";
        setupPayrollPinError.classList.add("hidden");
        setupPayrollPinModal.classList.remove("hidden");
        setupPayrollPinModal.style.display = "flex";
        setTimeout(() => {
            if (setupPayrollPinModalContent) {
                setupPayrollPinModalContent.style.transform = "scale(1)";
                setupPayrollPinModalContent.style.opacity = "1";
            }
            newPayrollPinInput.focus();
        }, 10);
    });

    const closeSetupPinModal = () => {
        if (!setupPayrollPinModal) return;
        if (setupPayrollPinModalContent) {
            setupPayrollPinModalContent.style.transform = "scale(0.95)";
            setupPayrollPinModalContent.style.opacity = "0";
        }
        setTimeout(() => {
            setupPayrollPinModal.style.display = "none";
            setupPayrollPinModal.classList.add("hidden");
        }, 300);
    };

    closeSetupPayrollPinModal?.addEventListener("click", closeSetupPinModal);

    submitSetupPayrollPinBtn?.addEventListener("click", async () => {
        const pin1 = newPayrollPinInput.value;
        const pin2 = confirmPayrollPinInput.value;
        
        if (!pin1 || pin1.length < 4) {
            setupPayrollPinError.textContent = "Mã PIN phải có ít nhất 4 ký tự.";
            setupPayrollPinError.classList.remove("hidden");
            newPayrollPinInput.focus();
            return;
        }
        if (pin1 !== pin2) {
            setupPayrollPinError.textContent = "Mã PIN xác nhận không khớp.";
            setupPayrollPinError.classList.remove("hidden");
            confirmPayrollPinInput.focus();
            return;
        }
        
        try {
            const res = await fetch("/Employee/UpdatePayrollPin", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ pin: pin1 })
            });
            const result = await res.json();
            if (result.success) {
                hasPayrollPin = true;
                alert("Đăng ký mã PIN thành công!");
                closeSetupPinModal();
            } else {
                setupPayrollPinError.textContent = result.message || "Lỗi cập nhật mã PIN.";
                setupPayrollPinError.classList.remove("hidden");
            }
        } catch (e) { console.error(e); }
    });

    renderDateTime();
    renderProfile();
    renderAttendance();
    renderRequests();
    updateRequestPreview();
    loadMessages();
    loadMyTasks();
    renderScheduleCalendar();
    applySettings();
    
    // Load Avatar from DB initially
    (async () => {
        try {
            const res = await fetch("/Employee/GetProfile");
            const result = await res.json();
            if (result.success && result.data) {
                hasPayrollPin = result.data.hasPayrollPin;
                if (result.data.avatarUrl) {
                    document.querySelectorAll("[data-avatar-image]").forEach(img => {
                        img.src = result.data.avatarUrl;
                        img.classList.remove("hidden");
                    });
                    document.querySelectorAll("[data-avatar-fallback]").forEach(icon => {
                        icon.classList.add("hidden");
                    });
                }
            }
        } catch (e) { console.error("Failed to load initial avatar", e); }
    })();

    loadMyViolations();
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
