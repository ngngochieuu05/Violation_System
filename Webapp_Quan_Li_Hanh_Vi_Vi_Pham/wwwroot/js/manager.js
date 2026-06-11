(() => {
    const app = document.querySelector("[data-manager-app]");
    if (!app) return;

    const initialTab = app.dataset.initialTab || "home";
    const tabButtons = Array.from(document.querySelectorAll("[data-tab-trigger]"));
    const tabPanels = Array.from(document.querySelectorAll("[data-tab-panel]"));

    const setActiveTab = (tab) => {
        const normalized = tabPanels.some((panel) => panel.dataset.tabPanel === tab) ? tab : "home";

        tabButtons.forEach((button) => {
            const isActive = button.dataset.tabTrigger === normalized;
            button.classList.toggle("bg-red-600", isActive);
            button.classList.toggle("text-white", isActive);
            button.classList.toggle("shadow-sm", isActive);
            button.classList.toggle("hover:bg-red-600", isActive);
            button.classList.toggle("hover:text-white", isActive);
            button.classList.toggle("text-slate-600", !isActive);
            if (!isActive) {
                button.classList.add("hover:bg-red-50", "hover:text-red-600");
            } else {
                button.classList.remove("hover:bg-red-50", "hover:text-red-600");
            }
        });

        tabPanels.forEach((panel) => {
            panel.classList.toggle("hidden", panel.dataset.tabPanel !== normalized);
        });

        const url = new URL(window.location.href);
        url.searchParams.set("tab", normalized);
        window.history.replaceState({}, "", url);
        
        // Trigger specific data load based on tab
        loadTabData(normalized);
    };

    const loadTabData = (tab) => {
        switch(tab) {
            case "employees":
                loadEmployees();
                break;
            case "attendance":
                loadWorkSessions();
                break;
            case "violations":
                loadViolations();
                break;
            case "requests":
                loadRequests();
                break;
            case "messages":
                loadMessages();
                break;
            case "forms":
                loadForms();
                break;
            case "home":
                loadHomeStats();
                break;
            case "schedule":
                loadTasks();
                break;
            case "payroll":
                loadPayrolls();
                break;
        }
    };

    const loadEmployees = async () => {
        const tbody = document.getElementById("employeeListTbody");
        if (!tbody) return;
        try {
            const res = await fetch('/Manager/GetAllEmployees');
            const data = await res.json();
            if (data.success) {
                tbody.innerHTML = data.data.map(e => `
                    <tr class="hover:bg-slate-50 border-b border-slate-100 transition-colors">
                        <td class="p-4 py-3"><span class="font-medium text-slate-900">${e.employeeCode || 'N/A'}</span></td>
                        <td class="p-4 py-3 text-slate-700">${e.fullName}</td>
                        <td class="p-4 py-3 text-slate-500">${e.department || 'N/A'}</td>
                        <td class="p-4 py-3 text-slate-500">${e.username}</td>
                        <td class="p-4 py-3 text-center">
                            <button onclick="window.openCameraModal('${e.employeeCode}')" class="text-slate-400 hover:text-red-500 transition">
                                <i class="fa-solid fa-video text-lg"></i>
                            </button>
                        </td>
                    </tr>
                `).join('');
            }
        } catch(err) { console.error(err); }
    };

    const loadWorkSessions = async () => {
        const tbody = document.getElementById("workSessionTbody");
        if (!tbody) return;
        try {
            const res = await fetch('/Manager/GetAllWorkSessions');
            const data = await res.json();
            if (data.success) {
                tbody.innerHTML = data.data.map(ws => `
                    <tr class="hover:bg-slate-50 border-b border-slate-100">
                        <td class="p-4 py-3 text-slate-900 font-medium">${ws.employeeCode}</td>
                        <td class="p-4 py-3 text-slate-500">${new Date(ws.date).toLocaleDateString('vi-VN')}</td>
                        <td class="p-4 py-3 text-slate-500">${new Date(ws.checkInTime).toLocaleTimeString('vi-VN')}</td>
                        <td class="p-4 py-3 text-slate-500">${ws.checkOutTime ? new Date(ws.checkOutTime).toLocaleTimeString('vi-VN') : 'Đang làm việc'}</td>
                        <td class="p-4 py-3"><span class="px-2.5 py-1 text-[10px] font-bold rounded-full ${ws.status === 'Late' ? 'bg-amber-100 text-amber-700' : 'bg-green-100 text-green-700'}">${ws.status}</span></td>
                    </tr>
                `).join('');
            }
        } catch(err) { console.error(err); }
    };

    const loadViolations = async () => {
        const tbody = document.getElementById("violationTbody");
        if (!tbody) return;
        try {
            const res = await fetch('/Manager/GetAllViolations');
            const data = await res.json();
            if (data.success) {
                tbody.innerHTML = data.data.map(v => `
                    <tr class="hover:bg-slate-50 border-b border-slate-100">
                        <td class="p-4 py-3 text-slate-900 font-medium">${v.employeeCode}</td>
                        <td class="p-4 py-3 text-slate-700">${v.violationType}</td>
                        <td class="p-4 py-3 text-slate-500">${v.cameraLocation}</td>
                        <td class="p-4 py-3 text-slate-500">${new Date(v.detectedAtUtc).toLocaleString('vi-VN')}</td>
                        <td class="p-4 py-3"><span class="px-2.5 py-1 text-[10px] font-bold rounded-full bg-red-100 text-red-700">${v.severity}</span></td>
                    </tr>
                `).join('');
            }
        } catch(err) { console.error(err); }
    };

    const loadRequests = async () => {
        const tbody = document.getElementById("requestTbody");
        if (!tbody) return;
        try {
            const res = await fetch('/Manager/GetAllRequests');
            const data = await res.json();
            if (data.success) {
                tbody.innerHTML = data.data.map(r => `
                    <tr class="hover:bg-slate-50 border-b border-slate-100">
                        <td class="p-4 py-3 text-slate-900 font-medium">${r.employeeId}</td>
                        <td class="p-4 py-3 text-slate-700">${r.requestType}</td>
                        <td class="p-4 py-3 text-slate-500">${new Date(r.submittedAt).toLocaleDateString('vi-VN')}</td>
                        <td class="p-4 py-3"><span class="px-2.5 py-1 text-[10px] font-bold rounded-full ${r.status === 'Pending' ? 'bg-amber-100 text-amber-700' : 'bg-green-100 text-green-700'}">${r.status}</span></td>
                    </tr>
                `).join('');
            }
        } catch(err) { console.error(err); }
    };

    const loadMessages = async () => {
        const tbody = document.getElementById("messageTbody");
        if (!tbody) return;
        try {
            const res = await fetch('/Manager/GetAllMessages');
            const data = await res.json();
            if (data.success) {
                if (data.data.length === 0) {
                    tbody.innerHTML = `<tr><td colspan="4" class="p-8 text-center text-slate-400">Chưa có tin nhắn nào.</td></tr>`;
                    return;
                }
                tbody.innerHTML = data.data.map(m => `
                    <tr class="hover:bg-slate-50 border-b border-slate-100">
                        <td class="p-4 py-3 text-slate-900 font-medium">${m.senderId || 'Hệ thống'}</td>
                        <td class="p-4 py-3 text-slate-700">${m.title || 'Không có tiêu đề'}</td>
                        <td class="p-4 py-3 text-slate-500">${m.content}</td>
                        <td class="p-4 py-3 text-slate-500">${new Date(m.sentAt).toLocaleString('vi-VN')}</td>
                    </tr>
                `).join('');
            }
        } catch(err) { console.error(err); }
    };

    const loadForms = async () => {
        const tbody = document.getElementById("formTbody");
        if (!tbody) return;
        try {
            const res = await fetch('/Manager/GetAllForms');
            const data = await res.json();
            if (data.success) {
                if (data.data.length === 0) {
                    tbody.innerHTML = `<tr><td colspan="4" class="p-8 text-center text-slate-400">Kho tài liệu đang trống.</td></tr>`;
                    return;
                }
                tbody.innerHTML = data.data.map(f => `
                    <tr class="hover:bg-slate-50 border-b border-slate-100">
                        <td class="p-4 py-3 text-slate-900 font-medium">${f.title}</td>
                        <td class="p-4 py-3 text-slate-500">${f.description || ''}</td>
                        <td class="p-4 py-3 text-slate-500">${new Date(f.lastUpdated).toLocaleDateString('vi-VN')}</td>
                        <td class="p-4 py-3 text-center">
                            <a href="${f.fileUrl || '#'}" target="_blank" class="text-blue-500 hover:underline"><i class="fa-solid fa-download"></i> Tải xuống</a>
                        </td>
                    </tr>
                `).join('');
            }
        } catch(err) { console.error(err); }
    };

    const loadHomeStats = async () => {
        try {
            const res = await fetch('/Manager/GetHomeStats');
            const data = await res.json();
            if (data.success) {
                document.getElementById('statEmployees').innerText = data.data.employees;
                document.getElementById('statAttendance').innerText = data.data.attendance;
                document.getElementById('statViolations').innerText = data.data.violations;
                document.getElementById('statRequests').innerText = data.data.requests;
            }
        } catch (err) { console.error(err); }
    };

    const loadTasks = async () => {
        const tbody = document.getElementById("managerTaskListTbody");
        if (!tbody) return;
        try {
            const res = await fetch('/Manager/GetAllTasks');
            const data = await res.json();
            if (data.success) {
                tbody.innerHTML = data.data.map(t => `
                    <tr class="hover:bg-slate-50 border-b border-slate-100">
                        <td class="p-4 py-3 text-slate-900 font-medium">${t.employeeName}</td>
                        <td class="p-4 py-3 text-slate-700">${t.title}</td>
                        <td class="p-4 py-3 text-slate-500">${t.description}</td>
                        <td class="p-4 py-3 text-slate-500">${new Date(t.dueDate).toLocaleString('vi-VN')}</td>
                        <td class="p-4 py-3"><span class="px-2.5 py-1 text-[10px] font-bold rounded-full ${t.status === 'Done' ? 'bg-green-100 text-green-700' : 'bg-amber-100 text-amber-700'}">${t.status}</span></td>
                    </tr>
                `).join('');
            }
        } catch(err) { console.error(err); }
    };

    const loadPayrolls = async () => {
        const tbody = document.getElementById("managerPayrollTbody");
        if (!tbody) return;
        const month = document.getElementById("payrollMonth").value;
        const year = document.getElementById("payrollYear").value;

        try {
            const res = await fetch(`/Manager/GetAllPayrolls?month=${month}&year=${year}`);
            const data = await res.json();
            if (data.success) {
                if (data.data.length === 0) {
                    tbody.innerHTML = `<tr><td colspan="7" class="p-8 text-center text-slate-400">Chưa có dữ liệu lương tháng ${month}/${year}. Hãy bấm "Tính lương tháng này".</td></tr>`;
                    return;
                }
                tbody.innerHTML = data.data.map(p => `
                    <tr class="hover:bg-slate-50 border-b border-slate-100">
                        <td class="p-4 py-3 text-slate-900 font-medium">${p.employeeName}</td>
                        <td class="p-4 py-3 text-slate-700 text-right">${p.baseSalary.toLocaleString('vi-VN')} ₫</td>
                        <td class="p-4 py-3 text-emerald-600 font-medium text-right">+${p.kpiBonus.toLocaleString('vi-VN')} ₫</td>
                        <td class="p-4 py-3 text-red-600 font-medium text-right">-${p.violationDeduction.toLocaleString('vi-VN')} ₫</td>
                        <td class="p-4 py-3 text-slate-900 font-bold text-right">${p.netSalary.toLocaleString('vi-VN')} ₫</td>
                        <td class="p-4 py-3 text-center"><span class="px-2.5 py-1 text-[10px] font-bold rounded-full ${p.status === 'Đã thanh toán' ? 'bg-green-100 text-green-700' : 'bg-slate-100 text-slate-700'}">${p.status}</span></td>
                        <td class="p-4 py-3 text-center">
                            ${p.status !== 'Đã thanh toán' ? `<button onclick="window.updatePayrollStatus('${p.id}', 'Đã thanh toán')" class="text-xs bg-emerald-500 text-white px-2 py-1 rounded hover:bg-emerald-600 transition shadow-sm">Thanh toán</button>` : ''}
                        </td>
                    </tr>
                `).join('');
            }
        } catch(err) { console.error(err); }
    };

    window.calculatePayroll = async () => {
        const month = document.getElementById("payrollMonth").value;
        const year = document.getElementById("payrollYear").value;
        try {
            await fetch(`/Manager/CalculateMonthlyPayroll?month=${month}&year=${year}`, { method: 'POST' });
            loadPayrolls();
        } catch(err) { console.error(err); }
    };

    window.updatePayrollStatus = async (id, status) => {
        try {
            await fetch(`/Manager/UpdatePayrollStatus?id=${id}&status=${status}`, { method: 'POST' });
            loadPayrolls();
        } catch(err) { console.error(err); }
    };

    window.openAssignTaskModal = async () => {
        // Load employees into select
        try {
            const res = await fetch('/Manager/GetAllEmployees');
            const data = await res.json();
            const select = document.getElementById("taskEmployeeId");
            select.innerHTML = data.data.map(e => `<option value="${e.id}">${e.fullName} (${e.employeeCode})</option>`).join('');
        } catch(err) {}
        
        const modal = document.getElementById("assignTaskModal");
        modal.classList.remove("hidden");
        modal.classList.add("flex");
        setTimeout(() => modal.querySelector('.saas-card').style.transform = 'scale(1)', 10);
    };

    window.closeAssignTaskModal = () => {
        const modal = document.getElementById("assignTaskModal");
        modal.classList.add("hidden");
        modal.classList.remove("flex");
    };

    window.submitAssignTask = async () => {
        const payload = {
            EmployeeId: document.getElementById("taskEmployeeId").value,
            Title: document.getElementById("taskTitle").value,
            Description: document.getElementById("taskDescription").value,
            DueDate: document.getElementById("taskDueDate").value
        };
        try {
            await fetch('/Manager/AssignTask', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });
            window.closeAssignTaskModal();
            loadTasks();
        } catch(err) { console.error(err); }
    };

    window.openCameraModal = (employeeCode) => {
        const modal = document.getElementById("cameraModal");
        document.getElementById("cameraModalTitle").textContent = "Camera: " + employeeCode;
        modal.classList.remove("hidden");
        modal.classList.add("flex");
        document.getElementById("cameraLoading").classList.remove("hidden");
        document.getElementById("cameraVideo").classList.add("hidden");
        document.getElementById("cameraOverlay").classList.add("hidden");

        // Simulate connection
        setTimeout(() => {
            document.getElementById("cameraLoading").classList.add("hidden");
            document.getElementById("cameraVideo").classList.remove("hidden");
            document.getElementById("cameraOverlay").classList.remove("hidden");
            
            setInterval(() => {
                const now = new Date();
                document.getElementById("cameraLiveTime").textContent = now.toLocaleDateString('vi-VN') + " " + now.toLocaleTimeString('vi-VN');
            }, 1000);
        }, 1500);
    };

    window.closeCameraModal = () => {
        const modal = document.getElementById("cameraModal");
        modal.classList.add("hidden");
        modal.classList.remove("flex");
    };

    tabButtons.forEach((button) => {
        button.addEventListener("click", () => {
            setActiveTab(button.dataset.tabTrigger);
        });
    });

    // Initialize
    setActiveTab(initialTab);

    // PROFILE & SETTINGS LOGIC FOR MANAGER VIEW
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

})();
