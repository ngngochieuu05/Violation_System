(() => {
    const app = document.querySelector("[data-employee-app]");
    const modal = document.querySelector("[data-onboarding-modal]");
    if (!app || !modal) {
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

        element.textContent = done ? "Da hoan tat" : "Chua hoan tat";
        element.className = `inline-flex rounded-full px-3 py-1 text-[11px] font-bold ${done ? "bg-emerald-100 text-emerald-700" : "bg-amber-100 text-amber-700"}`;
    };

    const setModalState = (open) => {
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

            const requiresSetup = !!data.requiresInitialSecuritySetup;
            const mustChangePassword = !!data.mustChangePassword;
            const hasBiometric = !!data.hasBiometricRegistration;

            setModalState(requiresSetup);
            passwordSection?.classList.toggle("hidden", !mustChangePassword);
            faceSection?.classList.toggle("hidden", hasBiometric);
            setBadge(passwordBadge, !mustChangePassword);
            setBadge(faceBadge, hasBiometric);

            if (passwordHint) {
                passwordHint.textContent = data.passwordPolicyDescription || "Mat khau moi phai manh hon de bao ve tai khoan.";
            }

            if (faceMsg) {
                faceMsg.textContent = hasBiometric
                    ? "Da co du lieu khuon mat."
                    : "Hay mo camera va chup du 4 anh khuon mat.";
            }

            if (completeMsg) {
                completeMsg.textContent = requiresSetup
                    ? "Dashboard se duoc mo khi ban hoan tat 2 buoc tren."
                    : "Ban da hoan tat xac thuc tai khoan.";
            }

            if (summary) {
                summary.textContent = requiresSetup
                    ? "Tai khoan dang nhap bang email phai doi mat khau va bo sung khuon mat truoc khi tiep tuc su dung dashboard."
                    : "Tai khoan da hoan tat xac thuc bat buoc.";
            }

            if (requiresSetup) {
                startPolling();
            } else {
                stopPolling();
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
            passwordMsg.textContent = "Vui long nhap day du mat khau moi va xac nhan.";
            passwordMsg.className = "mt-3 text-xs font-semibold text-red-600";
            return;
        }

        if (newPassword !== confirmPassword) {
            passwordMsg.textContent = "Mat khau xac nhan khong khop.";
            passwordMsg.className = "mt-3 text-xs font-semibold text-red-600";
            return;
        }

        if (!validatePasswordPolicy(newPassword)) {
            passwordMsg.textContent = "Mat khau phai co it nhat 8 ky tu, gom chu hoa, chu thuong, so va ky tu dac biet.";
            passwordMsg.className = "mt-3 text-xs font-semibold text-red-600";
            return;
        }

        passwordMsg.textContent = "Dang cap nhat mat khau...";
        passwordMsg.className = "mt-3 text-xs font-semibold text-amber-600";

        try {
            const response = await fetch(changePasswordUrl, {
                method: "POST",
                headers: { "Content-Type": "application/x-www-form-urlencoded" },
                body: new URLSearchParams({ oldPassword: "", newPassword })
            });
            const data = await response.json();

            passwordMsg.textContent = data.message || (data.success ? "Doi mat khau thanh cong." : "Khong the doi mat khau.");
            passwordMsg.className = `mt-3 text-xs font-semibold ${data.success ? "text-emerald-600" : "text-red-600"}`;

            if (data.success) {
                if (passwordInput) passwordInput.value = "";
                if (confirmInput) confirmInput.value = "";
                refreshStatus();
            }
        } catch (error) {
            passwordMsg.textContent = "Loi ket noi may chu.";
            passwordMsg.className = "mt-3 text-xs font-semibold text-red-600";
        }
    });

    openFaceButton?.addEventListener("click", () => {
        document.querySelector("[data-profile-update-face]")?.dispatchEvent(new MouseEvent("click", { bubbles: true }));
        startPolling();
    });

    refreshStatus();
})();
