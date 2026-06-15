(() => {
    const modal = document.querySelector("[data-onboarding-modal]");
    if (!modal) {
        return;
    }

    const statusUrl = "/Account/OnboardingStatus";
    const changePasswordUrl = "/Account/ChangePassword";
    const passwordSection = document.querySelector("[data-onboarding-password-section]");
    const faceSection = document.querySelector("[data-onboarding-face-section]");
    const passwordBadge = document.querySelector("[data-onboarding-password-badge]");
    const faceBadge = document.querySelector("[data-onboarding-face-badge]");
    const passwordHint = document.querySelector("[data-onboarding-password-hint]");
    const passwordMsg = document.querySelector("[data-onboarding-pwd-msg]");
    const faceMsg = document.querySelector("[data-onboarding-face-msg]");
    const completeMsg = document.querySelector("[data-onboarding-complete-msg]");
    const savePasswordButton = document.querySelector("[data-onboarding-save-password]");
    const openFaceButton = document.querySelector("[data-onboarding-open-face]");
    const passwordInput = document.querySelector("[data-onboarding-pwd-new]");
    const confirmInput = document.querySelector("[data-onboarding-pwd-confirm]");
    const summary = document.querySelector("[data-onboarding-summary]");

    let pollHandle = null;

    const validatePasswordPolicy = (password) => {
        if (!password || password.length < 8) {
            return false;
        }

        return /[A-Z]/.test(password)
            && /[a-z]/.test(password)
            && /\d/.test(password)
            && /[^A-Za-z0-9]/.test(password);
    };

    const setBadge = (element, done) => {
        if (!element) {
            return;
        }

        element.textContent = done ? "Đã hoàn tất" : "Chưa hoàn tất";
        element.className = `inline-flex rounded-full px-3 py-1 text-[11px] font-bold ${done ? "bg-emerald-100 text-emerald-700" : "bg-amber-100 text-amber-700"}`;
    };

    let isSetupRequired = false;
    let isModalHiddenForProfile = false;

    const setModalState = (open) => {
        isSetupRequired = open;
        if (isModalHiddenForProfile && open) {
            return;
        }
        modal.classList.toggle("hidden", !open);
        modal.classList.toggle("flex", open);
        document.body.classList.toggle("overflow-hidden", open);
    };

    const startPolling = () => {
        if (pollHandle) {
            return;
        }

        pollHandle = window.setInterval(() => {
            refreshStatus();
        }, 2000);
    };

    const stopPolling = () => {
        if (!pollHandle) {
            return;
        }

        window.clearInterval(pollHandle);
        pollHandle = null;
    };

    const refreshStatus = async () => {
        try {
            const response = await fetch(statusUrl, { credentials: "same-origin" });
            const data = await response.json();
            if (!data.success) {
                return;
            }

            const mustChangePassword = !!data.mustChangePassword;
            const hasBiometric = !!data.hasBiometricRegistration;
            const requiresSetup = !!data.requiresInitialSecuritySetup || !hasBiometric;

            setModalState(requiresSetup);
            passwordSection?.classList.toggle("hidden", !mustChangePassword);
            faceSection?.classList.toggle("hidden", hasBiometric);
            setBadge(passwordBadge, !mustChangePassword);
            setBadge(faceBadge, hasBiometric);

            if (passwordHint) {
                passwordHint.textContent = data.passwordPolicyDescription || "Mật khẩu mới phải mạnh hơn để bảo vệ tài khoản.";
            }

            if (faceMsg) {
                faceMsg.textContent = hasBiometric
                    ? "Đã có dữ liệu khuôn mặt."
                    : "Hãy mở camera và chụp đủ 4 ảnh khuôn mặt.";
            }

            if (completeMsg) {
                completeMsg.textContent = requiresSetup
                    ? "Dashboard sẽ được mở khi bạn hoàn tất 2 bước trên."
                    : "Bạn đã hoàn tất xác thực tài khoản.";
            }

            if (summary) {
                summary.textContent = requiresSetup
                    ? "Tài khoản đăng nhập bằng email phải đổi mật khẩu và bổ sung khuôn mặt trước khi tiếp tục sử dụng dashboard."
                    : "Tài khoản đã hoàn tất xác thực bắt buộc.";
            }

            if (requiresSetup) {
                startPolling();
            } else {
                stopPolling();
                isModalHiddenForProfile = false;
            }
        } catch (error) {
            console.error("Failed to refresh onboarding status", error);
        }
    };

    savePasswordButton?.addEventListener("click", async () => {
        const newPassword = passwordInput?.value || "";
        const confirmPassword = confirmInput?.value || "";

        if (!passwordMsg) {
            return;
        }

        if (!newPassword || !confirmPassword) {
            passwordMsg.textContent = "Vui lòng nhập đầy đủ mật khẩu mới và xác nhận.";
            passwordMsg.className = "mt-3 text-xs font-semibold text-red-600";
            return;
        }

        if (newPassword !== confirmPassword) {
            passwordMsg.textContent = "Mật khẩu xác nhận không khớp.";
            passwordMsg.className = "mt-3 text-xs font-semibold text-red-600";
            return;
        }

        if (!validatePasswordPolicy(newPassword)) {
            passwordMsg.textContent = "Mật khẩu phải có ít nhất 8 ký tự, gồm chữ hoa, chữ thường, số và ký tự đặc biệt.";
            passwordMsg.className = "mt-3 text-xs font-semibold text-red-600";
            return;
        }

        passwordMsg.textContent = "Đang cập nhật mật khẩu...";
        passwordMsg.className = "mt-3 text-xs font-semibold text-amber-600";

        try {
            const response = await fetch(changePasswordUrl, {
                method: "POST",
                headers: { "Content-Type": "application/x-www-form-urlencoded" },
                body: new URLSearchParams({ oldPassword: "", newPassword })
            });
            const data = await response.json();

            passwordMsg.textContent = data.message || (data.success ? "Đổi mật khẩu thành công." : "Không thể đổi mật khẩu.");
            passwordMsg.className = `mt-3 text-xs font-semibold ${data.success ? "text-emerald-600" : "text-red-600"}`;

            if (data.success) {
                if (passwordInput) passwordInput.value = "";
                if (confirmInput) confirmInput.value = "";
                refreshStatus();
            }
        } catch (error) {
            passwordMsg.textContent = "Lỗi kết nối máy chủ.";
            passwordMsg.className = "mt-3 text-xs font-semibold text-red-600";
        }
    });

    openFaceButton?.addEventListener("click", () => {
        isModalHiddenForProfile = true;
        // Go to Profile Tab
        document.querySelector("[data-tab-trigger='profile']")?.click();
        
        // Hide modal
        modal.classList.add("hidden");
        modal.classList.remove("flex");
        document.body.classList.remove("overflow-hidden");
        
        // Highlight face update section
        setTimeout(() => {
            const faceBtn = document.querySelector("[data-profile-update-face]");
            if (faceBtn) {
                faceBtn.scrollIntoView({ behavior: 'smooth', block: 'center' });
                faceBtn.classList.add("ring-4", "ring-red-500", "ring-opacity-50");
                setTimeout(() => faceBtn.classList.remove("ring-4", "ring-red-500", "ring-opacity-50"), 3000);
            }
        }, 500);

        startPolling();
    });

    // Enforce tab constraint
    document.addEventListener("click", (e) => {
        if (!isSetupRequired) return;

        // If they click on any sidebar tab that is NOT profile, block it
        const tabTrigger = e.target.closest("[data-tab-trigger]");
        if (tabTrigger && tabTrigger.getAttribute("data-tab-trigger") !== "profile") {
            e.preventDefault();
            e.stopPropagation();
            alert("Vui lòng hoàn tất cập nhật hồ sơ và dữ liệu khuôn mặt trước khi sử dụng các tính năng khác.");
        }
    }, true); // use capture phase to intercept early

    refreshStatus();
})();
