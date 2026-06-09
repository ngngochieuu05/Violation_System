(() => {
    const app = document.querySelector("[data-employee-app]");
    if (!app) return;

    const storageKeys = {
        attendance: "employee.attendance",
        requests: "employee.requests",
        chats: "employee.chats",
        profile: "employee.profile",
        settings: "employee.settings",
        avatar: "employee.avatar"  // lưu riêng để không ảnh hưởng các key khác
    };

    const initialTab = app.dataset.initialTab || "home";
    const tabButtons = Array.from(document.querySelectorAll("[data-tab-trigger]"));
    const tabPanels = Array.from(document.querySelectorAll("[data-tab-panel]"));

    const defaultProfile = {
        name: document.querySelector("[data-profile-input='name']")?.value || "",
        department: document.querySelector("[data-profile-input='department']")?.value || "",
        email: document.querySelector("[data-profile-input='email']")?.value || "",
        phone: document.querySelector("[data-profile-input='phone']")?.value || "",
        employeeId: document.querySelector("[data-profile-id]")?.textContent?.trim() || "EMP-2026-014"
        // avatar được lưu riêng, không nằm trong profile object
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
        } catch (e) {
            // QuotaExceededError - bỏ qua lỗi tràn bộ nhớ
            console.warn("localStorage quota exceeded for key:", key);
        }
    };

    // Đọc avatar riêng (raw string, không JSON)
    const readAvatar = () => {
        try { return localStorage.getItem(storageKeys.avatar) || null; } catch { return null; }
    };

    // Ghi avatar riêng (raw string)
    const writeAvatar = (dataUrl) => {
        try {
            if (dataUrl) {
                localStorage.setItem(storageKeys.avatar, dataUrl);
            } else {
                localStorage.removeItem(storageKeys.avatar);
            }
        } catch (e) {
            console.warn("Không thể lưu avatar vào localStorage (dung lượng quá lớn).", e);
            document.querySelector("[data-avatar-message]").textContent =
                "Ảnh quá lớn, không thể lưu vào trình duyệt. Hãy chọn ảnh nhỏ hơn.";
        }
    };

    // Resize + compress ảnh về tối đa 256x256 / quality 0.75 trước khi lưu
    const compressImage = (file, callback) => {
        const reader = new FileReader();
        reader.onload = (e) => {
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
            img.src = e.target.result;
        };
        reader.readAsDataURL(file);
    };

    let profile = readStore(storageKeys.profile, defaultProfile);
    let avatarDataUrl = readAvatar(); // avatar được load độc lập
    let attendance = readStore(storageKeys.attendance, {
        checkIn: null,
        checkOut: null,
        history: [],
        status: "Chưa check-in"
    });
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
        const date = new Date(value);
        return new Intl.DateTimeFormat(getLocale(), {
            hour: "2-digit",
            minute: "2-digit"
        }).format(date);
    };

    const calculateDuration = (checkIn, checkOut) => {
        if (!checkIn) return "00h 00m";
        const start = new Date(checkIn).getTime();
        const end = checkOut ? new Date(checkOut).getTime() : Date.now();
        const diff = Math.max(end - start, 0);
        const hours = Math.floor(diff / 3600000);
        const minutes = Math.floor((diff % 3600000) / 60000);
        return `${String(hours).padStart(2, "0")}h ${String(minutes).padStart(2, "0")}m`;
    };

    const setActiveTab = (tab) => {
        const normalized = tabPanels.some((panel) => panel.dataset.tabPanel === tab) ? tab : "home";
        tabButtons.forEach((button) => {
            const isActive = button.dataset.tabTrigger === normalized;
            button.classList.toggle("bg-red-600", isActive);
            button.classList.toggle("text-white", isActive);
            button.classList.toggle("shadow-sm", isActive);
            button.classList.toggle("text-slate-600", !isActive);
        });

        tabPanels.forEach((panel) => {
            panel.classList.toggle("hidden", panel.dataset.tabPanel !== normalized);
        });

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
            ["[data-profile-id]", profile.employeeId]
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

    const renderAttendance = () => {
        document.querySelector("[data-attendance-checkin]").textContent = formatShortTime(attendance.checkIn);
        document.querySelector("[data-attendance-checkout]").textContent = formatShortTime(attendance.checkOut);
        document.querySelector("[data-attendance-total]").textContent = calculateDuration(attendance.checkIn, attendance.checkOut);
        document.querySelector("[data-attendance-status]").textContent = attendance.status;

        const historyEl = document.querySelector("[data-attendance-history]");
        historyEl.innerHTML = "";
        if (!attendance.history.length) {
            historyEl.innerHTML = '<div class="rounded-2xl bg-slate-50 p-4 text-sm text-slate-500">Chưa có lịch sử chấm công trong trình duyệt này.</div>';
            return;
        }

        [...attendance.history].reverse().forEach((item) => {
            const row = document.createElement("div");
            row.className = "rounded-2xl bg-slate-50 p-4";
            row.innerHTML = `
                <div class="flex items-center justify-between text-sm">
                    <span class="font-semibold text-slate-900">${item.action}</span>
                    <span class="text-slate-500">${item.time}</span>
                </div>
                <p class="mt-1 text-xs text-slate-400">${item.date}</p>
            `;
            historyEl.appendChild(row);
        });
    };

    const addAttendanceHistory = (action, date) => {
        attendance.history.push({
            action,
            time: formatShortTime(date),
            date: formatDate(date)
        });
        attendance.history = attendance.history.slice(-8);
    };

    const renderRequests = () => {
        const list = document.querySelector("[data-request-list]");
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
        const text = input.value.trim();
        if (!text) return;
        chats[activeChat] = chats[activeChat] || [];
        chats[activeChat].push({ author: "self", text });
        writeStore(storageKeys.chats, chats);
        input.value = "";
        renderChats();
    });

    document.querySelector("[data-request-submit]")?.addEventListener("click", () => {
        const type = document.querySelector("[data-request-type]").value;
        const date = document.querySelector("[data-request-date]").value;
        const content = document.querySelector("[data-request-content]").value.trim();
        const message = document.querySelector("[data-request-message]");

        if (!content) {
            message.textContent = "Vui lòng nhập nội dung đơn trước khi gửi.";
            return;
        }

        requests.push({ type, date, content, status: "Đã gửi" });
        writeStore(storageKeys.requests, requests);
        document.querySelector("[data-request-content]").value = "";
        message.textContent = "Đơn đã được tạo thành công trong chế độ mô phỏng.";
        renderRequests();
    });

    document.querySelector("[data-request-draft]")?.addEventListener("click", () => {
        const type = document.querySelector("[data-request-type]").value;
        const date = document.querySelector("[data-request-date]").value;
        const content = document.querySelector("[data-request-content]").value.trim();
        const message = document.querySelector("[data-request-message]");

        if (!content) {
            message.textContent = "Nhập nội dung trước khi lưu nháp.";
            return;
        }

        requests.push({ type, date, content, status: "Lưu nháp" });
        writeStore(storageKeys.requests, requests);
        message.textContent = "Đã lưu nháp cục bộ cho đơn hiện tại.";
        renderRequests();
    });

    document.querySelector("[data-profile-save]")?.addEventListener("click", () => {
        profile = {
            ...profile,
            name: document.querySelector("[data-profile-input='name']").value.trim() || profile.name,
            department: document.querySelector("[data-profile-input='department']").value.trim() || profile.department,
            email: document.querySelector("[data-profile-input='email']").value.trim() || profile.email,
            phone: document.querySelector("[data-profile-input='phone']").value.trim() || profile.phone
        };
        writeStore(storageKeys.profile, profile);
        renderProfile();
        document.querySelector("[data-profile-message]").textContent = "Đã cập nhật thông tin hiển thị trong khu vực nhân viên.";
    });

    document.querySelector("[data-avatar-input]")?.addEventListener("change", (event) => {
        const file = event.target.files?.[0];
        if (!file) return;

        if (!file.type.startsWith("image/")) {
            document.querySelector("[data-avatar-message]").textContent = "Vui lòng chọn đúng tệp hình ảnh.";
            // Reset input để có thể chọn lại
            event.target.value = "";
            return;
        }

        // Compress và lưu avatar vào key riêng, KHÔNG lưu trong profile object
        compressImage(file, (dataUrl) => {
            avatarDataUrl = dataUrl;
            writeAvatar(avatarDataUrl);
            // Reset input để người dùng có thể chọn lại cùng file
            event.target.value = "";
            renderProfile();
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
                notifications: !!document.querySelector('[data-setting-toggle="notifications"]').checked,
                compact: !!document.querySelector('[data-setting-toggle="compact"]').checked,
                reducedMotion: !!document.querySelector('[data-setting-toggle="reducedMotion"]').checked,
                language: document.querySelector('[data-setting-select="language"]')?.value || "vi-VN",
                theme: document.querySelector('[data-setting-select="theme"]')?.value || "light"
            };
            writeStore(storageKeys.settings, settings);
            applySettings();
            renderDateTime();
            renderAttendance();
            renderRequests();
            document.querySelector("[data-setting-message]").textContent = "Đã cập nhật setting trong trình duyệt hiện tại.";
        });
    });

    document.querySelectorAll("[data-setting-select]").forEach((select) => {
        select.addEventListener("change", () => {
            settings = {
                notifications: !!document.querySelector('[data-setting-toggle="notifications"]').checked,
                compact: !!document.querySelector('[data-setting-toggle="compact"]').checked,
                reducedMotion: !!document.querySelector('[data-setting-toggle="reducedMotion"]').checked,
                language: document.querySelector('[data-setting-select="language"]')?.value || "vi-VN",
                theme: document.querySelector('[data-setting-select="theme"]')?.value || "light"
            };
            writeStore(storageKeys.settings, settings);
            applySettings();
            renderDateTime();
            renderAttendance();
            renderRequests();
            document.querySelector("[data-setting-message]").textContent = "Đã áp dụng thay đổi giao diện và ngôn ngữ.";
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
        document.querySelector("[data-setting-message]").textContent = "Đã khôi phục thiết lập mặc định.";
    });

    document.querySelectorAll("[data-attendance-action]").forEach((button) => {
        button.addEventListener("click", () => {
            const action = button.dataset.attendanceAction;
            const now = new Date();
            const message = document.querySelector("[data-attendance-message]");

            if (action === "checkin") {
                attendance.checkIn = now.toISOString();
                attendance.checkOut = null;
                attendance.status = "Đang làm việc";
                addAttendanceHistory("Check-in", now);
                message.textContent = "Đã check-in thành công cho ca làm hiện tại.";
            } else if (!attendance.checkIn) {
                message.textContent = "Bạn cần check-in trước khi check-out.";
                renderAttendance();
                return;
            } else {
                attendance.checkOut = now.toISOString();
                attendance.status = "Đã check-out";
                addAttendanceHistory("Check-out", now);
                message.textContent = "Đã check-out thành công.";
            }

            writeStore(storageKeys.attendance, attendance);
            renderAttendance();
        });
    });

    renderDateTime();
    renderProfile();
    renderAttendance();
    renderRequests();
    renderChats();
    applySettings();
    setActiveTab(initialTab);
    setInterval(() => {
        renderDateTime();
        if (attendance.checkIn && !attendance.checkOut) {
            renderAttendance();
        }
    }, 1000);
})();
