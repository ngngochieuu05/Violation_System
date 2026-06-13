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
                if (typeof loadChatContacts === "function") {
                    loadChatContacts();
                }
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
                        <td class="p-4 py-3 text-center flex items-center justify-center gap-2">
                            <button onclick="window.openCameraModal('${e.employeeCode}')" class="px-2 py-1 bg-zinc-100 hover:bg-zinc-200 text-zinc-600 rounded text-xs transition" title="Xem Camera">
                                <i class="fa-solid fa-video"></i>
                            </button>
                            <button onclick="window.resetEmployeePassword('${e.username}')" class="px-2 py-1 bg-amber-100 hover:bg-amber-200 text-amber-700 rounded text-xs transition font-semibold" title="Cấp lại mật khẩu mặc định (123)">
                                <i class="fa-solid fa-key mr-1"></i>Reset MK
                            </button>
                        </td>
                    </tr>
                `).join('');
            }
        } catch(err) { console.error(err); }
    };

    window.submitNewEmployee = async () => {
        const code = document.getElementById('newEmpCode').value;
        const name = document.getElementById('newEmpName').value;
        const email = document.getElementById('newEmpEmail').value;
        const dept = document.getElementById('newEmpDept').value;

        if (!name || !email) {
            alert('Vui lòng nhập Họ & Tên và Tài khoản (Email)!');
            return;
        }

        try {
            const res = await fetch('/Manager/CreateEmployee', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ Username: email, FullName: name, Department: dept, EmployeeCode: code })
            });
            const data = await res.json();
            if (data.success) {
                alert(`Thêm nhân viên thành công!\nMật khẩu mặc định: ${data.defaultPassword}\nNhân viên sẽ được yêu cầu cập nhật lại trong lần đăng nhập đầu.`);
                document.getElementById('addEmployeeModal').classList.add('hidden');
                document.getElementById('addEmployeeModal').classList.remove('flex');
                
                document.getElementById('newEmpCode').value = '';
                document.getElementById('newEmpName').value = '';
                document.getElementById('newEmpEmail').value = '';
                document.getElementById('newEmpDept').value = '';
                
                loadEmployees();
            } else {
                alert(data.message || 'Có lỗi xảy ra');
            }
        } catch (e) {
            console.error(e);
            alert('Không thể kết nối đến máy chủ');
        }
    };

    window.resetEmployeePassword = async (username) => {
        if (!confirm(`Bạn có chắc muốn cấp lại mật khẩu cho tài khoản ${username}?\nMật khẩu mới sẽ là "123" và nhân viên phải đổi mật khẩu khi đăng nhập.`)) return;

        try {
            const res = await fetch('/Manager/ResetEmployeePassword', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ Username: username })
            });
            const data = await res.json();
            if (data.success) {
                alert(`Cấp lại mật khẩu thành công!\nMật khẩu mới: ${data.newPassword}`);
            } else {
                alert(data.message || 'Có lỗi xảy ra');
            }
        } catch (e) {
            console.error(e);
            alert('Không thể kết nối đến máy chủ');
        }
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
                        <td class="p-4 py-3 text-slate-500">${ws.checkOutTime ? new Date(ws.checkOutTime).toLocaleTimeString('vi-VN') : 'Äang lÃ m viá»‡c'}</td>
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
                if (data.data.length === 0) {
                    tbody.innerHTML = `<tr><td colspan="8" class="p-8 text-center text-slate-400">Chưa có vi phạm nào.</td></tr>`;
                    return;
                }

                tbody.innerHTML = data.data.map(v => `
                    <tr class="hover:bg-slate-50 border-b border-slate-100">
                        <td class="p-4 py-3 text-slate-900 font-semibold">${v.trackingId || 'N/A'}</td>
                        <td class="p-4 py-3 text-slate-900 font-medium">${v.employeeCode}</td>
                        <td class="p-4 py-3 text-slate-700">${v.violationType}</td>
                        <td class="p-4 py-3 text-slate-500">${v.cameraLocation}</td>
                        <td class="p-4 py-3 text-slate-500">${new Date(v.detectedAtUtc).toLocaleString('vi-VN')}</td>
                        <td class="p-4 py-3"><span class="px-2.5 py-1 text-[10px] font-bold rounded-full bg-red-100 text-red-700">${v.severity}</span></td>
                        <td class="p-4 py-3">
                            <div class="flex flex-col gap-1">
                                <span class="px-2.5 py-1 text-[10px] font-bold rounded-full ${v.status === 'Approved' ? 'bg-green-100 text-green-700' : v.status === 'Rejected' ? 'bg-red-100 text-red-700' : 'bg-amber-100 text-amber-700'}">${v.status}</span>
                                ${(v.reviewedBy || v.reviewedAtUtc) ? `<span class="text-[11px] text-slate-400">${v.reviewedBy || 'Manager'}${v.reviewedAtUtc ? ' • ' + new Date(v.reviewedAtUtc).toLocaleString('vi-VN') : ''}</span>` : ''}
                            </div>
                        </td>
                        <td class="p-4 py-3 text-right">
                            ${v.status === 'Pending' ? `
                            <div class="flex justify-end gap-2">
                                <button onclick="window.reviewViolation('${v.id}', 'Approved')" class="rounded bg-emerald-500 px-2.5 py-1 text-xs font-semibold text-white hover:bg-emerald-600">Duyệt</button>
                                <button onclick="window.reviewViolation('${v.id}', 'Rejected')" class="rounded bg-red-500 px-2.5 py-1 text-xs font-semibold text-white hover:bg-red-600">Từ chối</button>
                            </div>` : `<span class="text-xs text-slate-400">${v.reviewChannel || 'Đã xử lý'}</span>`}
                        </td>
                    </tr>
                `).join('');
            }
        } catch(err) { console.error(err); }
    };

    window.reviewViolation = async (id, status) => {
        const note = status === 'Rejected'
            ? (prompt('Nhập ghi chú từ chối vi phạm:') || 'Manager từ chối từ dashboard')
            : 'Manager duyệt từ dashboard';

        if (!confirm(`Xác nhận cập nhật vi phạm sang trạng thái ${status}?`)) return;

        try {
            const res = await fetch(`/Manager/ReviewViolation?id=${id}&status=${encodeURIComponent(status)}&note=${encodeURIComponent(note)}`, { method: 'POST' });
            const data = await res.json();
            if (data.success) {
                loadViolations();
                loadHomeStats();
            } else {
                alert(data.message || 'CÃ³ lá»—i xảy ra');
            }
        } catch (err) {
            console.error(err);
            alert('Không thể cập nhật vi phạm');
        }
    };

        window.managerRequestsList = [];
        const loadRequests = async () => {
        const tbody = document.getElementById("requestTbody");
        if (!tbody) return;
        try {
            const res = await fetch('/Manager/GetAllRequests');
            const data = await res.json();
            if (data.success) {
                window.managerRequestsList = data.data;
                if (data.data.length === 0) {
                    tbody.innerHTML = `<tr><td colspan="5" class="p-4 text-center text-slate-500">Không có đơn từ nào</td></tr>`;
                    return;
                }
                tbody.innerHTML = data.data.map(r => {
                    let tone = 'bg-slate-100 text-slate-700';
                    if (r.status === 'ÄÃ£ duyá»‡t' || r.status === 'Approved') tone = 'bg-green-100 text-green-700';
                    else if (r.status === 'Từ chối' || r.status === 'Rejected') tone = 'bg-red-100 text-red-700';
                    else tone = 'bg-amber-100 text-amber-700';
                    
                    return `
                    <tr class="hover:bg-slate-50 border-b border-slate-100">
                        <td class="p-4 py-3 text-slate-900 font-medium">${r.employeeName || 'N/A'}</td>
                        <td class="p-4 py-3 text-slate-700">
                            <div class="font-bold">${r.requestType}</div>
                            <div class="text-[10px] text-slate-400 mt-1">${r.content.replace(/\r?\n/g, '<br>')}</div>
                        </td>
                        <td class="p-4 py-3 text-slate-500">${new Date(r.submittedAt).toLocaleDateString('vi-VN')}</td>
                        <td class="p-4 py-3"><span class="px-2.5 py-1 text-[10px] font-bold rounded-full ${tone}">${r.status}</span></td>
                        <td class="p-4 py-3 text-right">
                            ${r.status === 'Chờ duyệt' || r.status === 'Pending' || r.status.includes('Chờ') ? `
                            <button onclick="updateRequestStatus(${r.id}, 'Đã duyệt')" class="px-2 py-1.5 bg-green-500 text-white rounded shadow text-xs hover:bg-green-600 mr-1" title="Duyệt"><i class="fa-solid fa-check"></i></button>
                            <button onclick="updateRequestStatus(${r.id}, 'Từ chối')" class="px-2 py-1.5 bg-red-500 text-white rounded shadow text-xs hover:bg-red-600 mr-1" title="Từ chối"><i class="fa-solid fa-xmark"></i></button>
                            <button onclick="openRequestDetailModal(${r.id})" class="px-2 py-1.5 bg-blue-500 text-white rounded shadow text-xs hover:bg-blue-600" title="Chi tiết"><i class="fa-solid fa-eye"></i></button>
                            ` : `
                            <button onclick="openRequestDetailModal(${r.id})" class="px-3 py-1.5 border border-slate-200 text-slate-600 rounded text-xs hover:bg-slate-50"><i class="fa-solid fa-eye"></i> Chi tiết</button>
                            `}
                        </td>
                    </tr>
                `}).join('');
            }
        } catch(err) { console.error(err); }
    };
    
    window.openRequestDetailModal = (id) => {
        const req = window.managerRequestsList.find(x => x.id === id);
        if(!req) return;
        
        document.getElementById("managerReqDetailEmployeeName").textContent = req.employeeName;
        document.getElementById("managerReqDetailDate").textContent = new Date(req.submittedAt).toLocaleDateString('vi-VN');
        document.getElementById("managerReqDetailTitle").textContent = req.requestType.toUpperCase();
        document.getElementById("managerReqDetailContent").innerHTML = req.content.replace(/\n/g, '<br>');
        document.getElementById("managerReqDetailSignName").textContent = req.employeeName;
        
        const statusEl = document.getElementById("managerReqDetailStatus");
        statusEl.textContent = req.status;
        if(req.status === 'ÄÃ£ duyá»‡t' || req.status === 'Approved') {
            statusEl.className = "absolute top-8 right-8 border-4 px-4 py-2 text-xl font-bold uppercase rotate-12 opacity-80 border-green-500 text-green-500";
        } else if (req.status === 'Từ chối' || req.status === 'Rejected') {
            statusEl.className = "absolute top-8 right-8 border-4 px-4 py-2 text-xl font-bold uppercase rotate-12 opacity-80 border-red-500 text-red-500";
        } else {
            statusEl.className = "absolute top-8 right-8 border-4 px-4 py-2 text-xl font-bold uppercase rotate-12 opacity-80 border-amber-500 text-amber-500";
        }
        
        document.getElementById("managerRequestDetailModal").dataset.currentId = id;
        document.getElementById("managerRequestDetailModal").classList.remove("hidden");
    };

    window.closeManagerRequestModal = () => {
        document.getElementById("managerRequestDetailModal").classList.add("hidden");
    };

    window.approveCurrentRequest = () => {
        const id = document.getElementById("managerRequestDetailModal").dataset.currentId;
        if(id) {
            updateRequestStatus(id, 'ÄÃ£ duyá»‡t');
            closeManagerRequestModal();
        }
    };

    window.rejectCurrentRequest = () => {
        const id = document.getElementById("managerRequestDetailModal").dataset.currentId;
        if(id) {
            updateRequestStatus(id, 'Từ chối');
            closeManagerRequestModal();
        }
    };
    
    window.updateRequestStatus = async (id, status) => {
        if (!confirm('Xác nhận ' + status + ' đơn này?')) return;
        try {
            const res = await fetch(`/Manager/UpdateRequestStatus?id=${id}&status=${encodeURIComponent(status)}`, { method: 'POST' });
            const data = await res.json();
            if (data.success) {
                loadRequests();
            } else {
                alert('Có lỗi xảy ra');
            }
        } catch(e) { console.error(e); }
    };

    let managerChatContacts = [];
    let managerActiveEmployeeId = null;
    let managerMessages = [];
    let managerEditingMessageId = null;

    const loadChatContacts = async () => {
        try {
            const res = await fetch('/Manager/GetChatContacts');
            const data = await res.json();
            if (data.success) {
                managerChatContacts = data.data;
                renderManagerChatContacts();
                if (!managerActiveEmployeeId && managerChatContacts.length > 0) {
                    managerActiveEmployeeId = managerChatContacts[0].userId;
                    loadManagerConversation();
                }
            }
        } catch (e) { console.error(e); }
    };

    const renderManagerChatContacts = () => {
        const list = document.getElementById("managerMessageChannelList");
        if (!list) return;

        if (managerChatContacts.length === 0) {
            list.innerHTML = `<div class="text-sm text-slate-500 text-center py-4">Chưa có liên hệ nào</div>`;
            return;
        }

        list.innerHTML = managerChatContacts.map(c => {
            const isActive = c.userId === managerActiveEmployeeId;
            const bgClass = isActive ? "bg-red-50" : "hover:bg-slate-50";
            return `
                <div class="flex items-center justify-between p-3 rounded-xl cursor-pointer transition ${bgClass}" onclick="window.selectManagerContact('${c.userId}')">
                    <div class="flex items-center gap-3 w-full">
                        <div class="relative shrink-0">
                            ${c.avatarUrl 
                                ? `<img src="${c.avatarUrl}" class="w-10 h-10 rounded-full object-cover">`
                                : `<div class="w-10 h-10 rounded-full bg-slate-200 flex items-center justify-center text-slate-500 font-bold">${c.fullName.charAt(0)}</div>`
                            }
                            ${c.unreadCount > 0 ? `<div class="absolute -top-1 -right-1 w-4 h-4 bg-red-500 border-2 border-white rounded-full flex items-center justify-center text-[9px] font-bold text-white">${c.unreadCount > 9 ? '9+' : c.unreadCount}</div>` : ''}
                        </div>
                        <div class="flex-1 min-w-0">
                            <div class="font-semibold text-slate-800 text-sm truncate">${c.fullName}</div>
                            <div class="text-xs text-slate-500 truncate">${c.lastMessage ? c.lastMessage : "Chưa có tin nhắn"}</div>
                        </div>
                    </div>
                </div>
            `;
        }).join('');
    };

    window.selectManagerContact = (userId) => {
        if (managerActiveEmployeeId === userId) return;
        managerActiveEmployeeId = userId;
        renderManagerChatContacts();
        loadManagerConversation();
        markConversationRead(userId);
    };

    const markConversationRead = async (userId) => {
        try {
            await fetch('/Manager/MarkConversationRead', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ EmployeeUserId: userId })
            });
            const contact = managerChatContacts.find(c => c.userId === userId);
            if (contact) {
                contact.unreadCount = 0;
                renderManagerChatContacts();
            }
        } catch (e) { console.error(e); }
    };

    const loadManagerConversation = async () => {
        if (!managerActiveEmployeeId) return;
        const contact = managerChatContacts.find(c => c.userId === managerActiveEmployeeId);
        if (contact) {
            document.getElementById("managerChatTitle").textContent = contact.fullName;
        }

        try {
            const res = await fetch(`/Manager/GetConversation?employeeUserId=${managerActiveEmployeeId}`);
            const data = await res.json();
            if (data.success) {
                managerMessages = data.data;
                renderManagerMessages();
                setTimeout(() => {
                    const thread = document.getElementById("managerChatThread");
                    if (thread) thread.scrollTop = thread.scrollHeight;
                }, 10);
            }
        } catch (e) { console.error(e); }
    };

    const renderManagerMessages = () => {
        const thread = document.getElementById("managerChatThread");
        if (!thread) return;

        if (managerMessages.length === 0) {
            thread.innerHTML = `
                <div class="rounded-xl border border-slate-100 bg-white p-4 text-sm text-slate-500 text-center">
                    Chưa có tin nhắn nào trong kênh này.
                </div>
            `;
            return;
        }

        thread.innerHTML = managerMessages.map(m => {
            const isSelf = m.senderRole === "Manager";
            if (isSelf) {
                return `
                <div class="flex gap-4 flex-row-reverse">
                    <div class="flex flex-col items-end">
                        <div class="flex items-center gap-2 mb-1 flex-row-reverse">
                            <span class="text-xs font-semibold text-slate-700">Bạn</span>
                            <span class="text-[10px] text-slate-400">${new Date(m.sentAt + (!m.sentAt.endsWith('Z') ? 'Z' : '')).toLocaleTimeString('vi-VN')}</span>
                            ${m.editedAtUtc && !m.isRevoked ? '<span class="text-[10px] text-slate-400 italic">(đã chỉnh sửa)</span>' : ''}
                        </div>
                        <div class="flex items-center gap-2">
                            ${!m.isRevoked ? `
                            <button onclick="window.editManagerMessage(${m.id})" class="text-[11px] text-slate-400 hover:text-blue-600 font-medium transition-colors px-2 py-1">Chỉnh sửa</button>
                            <button onclick="window.revokeManagerMessage(${m.id})" class="text-[11px] text-slate-400 hover:text-red-600 font-medium transition-colors px-2 py-1">Thu hồi</button>
                            ` : ''}
                            <div class="${m.isRevoked ? 'max-w-md rounded-2xl bg-slate-100 px-4 py-3 text-sm text-slate-400 italic border border-slate-200' : 'max-w-md rounded-2xl bg-red-600 px-4 py-3 text-sm text-white rounded-tr-none'} shadow-sm break-words">
                                ${m.content}
                            </div>
                        </div>
                    </div>
                </div>`;
            } else {
                return `
                <div class="flex gap-4">
                    <div class="flex flex-col items-start">
                        <div class="flex items-center gap-2 mb-1">
                            <span class="text-xs font-semibold text-slate-700">${m.senderName}</span>
                            <span class="text-[10px] text-slate-400">${new Date(m.sentAt + (!m.sentAt.endsWith('Z') ? 'Z' : '')).toLocaleTimeString('vi-VN')}</span>
                            ${m.editedAtUtc && !m.isRevoked ? '<span class="text-[10px] text-slate-400 italic">(đã chỉnh sửa)</span>' : ''}
                        </div>
                        <div class="bg-white p-3 rounded-2xl rounded-tl-none border border-slate-200 shadow-sm text-sm text-slate-700 max-w-md ${m.isRevoked ? 'opacity-50 italic' : ''}">
                            ${m.content}
                        </div>
                    </div>
                </div>`;
            }
        }).join('');
    };

    document.getElementById("managerChatSend")?.addEventListener("click", async () => {
        const input = document.getElementById("managerChatInput");
        if (!input || !managerActiveEmployeeId) return;
        const text = input.value.trim();
        if (!text) return;

        if (managerEditingMessageId) {
            try {
                await fetch('/Manager/EditMessage', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ Id: managerEditingMessageId, Content: text })
                });
                clearManagerMessageEditing();
                loadManagerConversation();
            } catch (e) { console.error(e); }
        } else {
            try {
                await fetch('/Manager/SendMessage', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ EmployeeId: managerActiveEmployeeId, Content: text })
                });
                input.value = "";
                loadManagerConversation();
            } catch (e) { console.error(e); }
        }
    });

    document.getElementById("managerChatInput")?.addEventListener("keypress", (e) => {
        if (e.key === "Enter") {
            document.getElementById("managerChatSend")?.click();
        }
    });

    window.editManagerMessage = (id) => {
        const msg = managerMessages.find(m => m.id === id);
        if (!msg) return;
        managerEditingMessageId = id;
        const input = document.getElementById("managerChatInput");
        const bar = document.getElementById("managerChatEditBar");
        if (input) {
            input.value = msg.content;
            input.focus();
        }
        if (bar) bar.classList.remove("hidden");
        if (bar) bar.classList.add("flex");
    };

    const clearManagerMessageEditing = () => {
        managerEditingMessageId = null;
        const input = document.getElementById("managerChatInput");
        const bar = document.getElementById("managerChatEditBar");
        if (input) input.value = "";
        if (bar) bar.classList.add("hidden");
        if (bar) bar.classList.remove("flex");
    };

    document.getElementById("managerChatEditCancel")?.addEventListener("click", clearManagerMessageEditing);

    window.revokeManagerMessage = async (id) => {
        if (!confirm("Xác nhận thu hồi tin nhắn này?")) return;
        try {
            await fetch('/Manager/RevokeMessage', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ Id: id })
            });
            loadManagerConversation();
        } catch (e) { console.error(e); }
    };

    const loadForms = async () => {
        const tbody = document.getElementById("formTbody");
        if (!tbody) return;
        try {
            const res = await fetch('/Manager/GetAllForms');
            const data = await res.json();
            if (data.success) {
                if (data.data.length === 0) {
                    tbody.innerHTML = `<tr><td colspan="4" class="p-8 text-center text-slate-400">Kho tÃ i liá»‡u đang trống.</td></tr>`;
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
                    tbody.innerHTML = `<tr><td colspan="9" class="p-8 text-center text-slate-400">Chưa có dữ liệu lương tháng ${month}/${year}. Hãy bấm "Tính lương tháng này".</td></tr>`;
                    return;
                }
                tbody.innerHTML = data.data.map(p => `
                    <tr class="hover:bg-slate-50 border-b border-slate-100">
                        <td class="p-4 py-3 text-slate-900 font-medium">${p.employeeName}</td>
                        <td class="p-4 py-3 text-slate-700 text-right">${p.baseSalary.toLocaleString('vi-VN')} đ</td>
                        <td class="p-4 py-3 text-slate-700 text-right">${p.actualWorkingDays}/${p.standardWorkingDays}</td>
                        <td class="p-4 py-3 text-slate-700 text-right">${p.salaryPerDay.toLocaleString('vi-VN')} đ</td>
                        <td class="p-4 py-3 text-emerald-600 font-medium text-right">+${p.kpiBonus.toLocaleString('vi-VN')} đ</td>
                        <td class="p-4 py-3 text-red-600 font-medium text-right">-${p.violationDeduction.toLocaleString('vi-VN')} đ</td>
                        <td class="p-4 py-3 text-slate-900 font-bold text-right">${p.netSalary.toLocaleString('vi-VN')} đ</td>
                        <td class="p-4 py-3 text-center"><span class="px-2.5 py-1 text-[10px] font-bold rounded-full ${p.status === 'Đã thanh toán' ? 'bg-green-100 text-green-700' : 'bg-slate-100 text-slate-700'}">${p.status}</span></td>
                        <td class="p-4 py-3 text-center flex flex-col gap-1 items-center justify-center">
                            ${p.status !== 'Đã thanh toán' ? `<button onclick="window.updatePayrollStatus('${p.id}', 'Đã thanh toán')" class="text-xs bg-emerald-500 text-white px-2 py-1 rounded hover:bg-emerald-600 transition shadow-sm w-full">Thanh toán</button>` : ''}
                            <button onclick="window.openEditPayrollModal('${p.id}', ${p.baseSalary}, ${p.standardWorkingDays}, ${p.actualWorkingDays}, ${p.kpiBonus})" class="text-xs bg-slate-200 text-slate-700 px-2 py-1 rounded hover:bg-slate-300 transition shadow-sm w-full">Chỉnh sửa</button>
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

    window.openEditPayrollModal = (id, baseSalary, standardDays, actualDays, kpiBonus) => {
        document.getElementById('editPayrollId').value = id;
        document.getElementById('editPayrollBase').value = baseSalary;
        document.getElementById('editPayrollStandardDays').value = standardDays;
        document.getElementById('editPayrollActualDays').value = actualDays;
        document.getElementById('editPayrollKpi').value = kpiBonus;
        
        const modal = document.getElementById('editPayrollModal');
        modal.classList.remove('hidden');
        modal.classList.add('flex');
    };

    window.closeEditPayrollModal = () => {
        const modal = document.getElementById('editPayrollModal');
        modal.classList.remove('flex');
        modal.classList.add('hidden');
    };

    window.submitEditPayroll = async () => {
        const id = document.getElementById('editPayrollId').value;
        const baseSalary = parseFloat(document.getElementById('editPayrollBase').value) || 0;
        const standardDays = parseInt(document.getElementById('editPayrollStandardDays').value) || 0;
        const actualDays = parseInt(document.getElementById('editPayrollActualDays').value) || 0;
        const kpiBonus = parseFloat(document.getElementById('editPayrollKpi').value) || 0;

        try {
            const res = await fetch('/Manager/EditPayrollRecord', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    Id: id,
                    BaseSalary: baseSalary,
                    StandardWorkingDays: standardDays,
                    ActualWorkingDays: actualDays,
                    KpiBonus: kpiBonus
                })
            });
            const data = await res.json();
            if (data.success) {
                closeEditPayrollModal();
                loadPayrolls();
            } else {
                alert(data.message);
            }
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
            msg.innerHTML = '<i class="fa-solid fa-triangle-exclamation mr-1"></i>Mật khẩu xÃ¡c nháº­n không khớp.';
            msg.classList.remove("hidden");
            return;
        }

        if (newPwd.length < 8) {
            msg.className = "text-xs font-semibold text-red-600";
            msg.innerHTML = '<i class="fa-solid fa-triangle-exclamation mr-1"></i>Mật khẩu má»›i pháº£i tá»« 8 kÃ½ tá»±.';
            msg.classList.remove("hidden");
            return;
        }

        msg.className = "text-xs font-semibold text-emerald-600";
        msg.innerHTML = '<i class="fa-solid fa-circle-check mr-1"></i>Äá»•i máº­t kháº©u thÃ nh cÃ´ng!';
        msg.classList.remove("hidden");

        document.querySelector("[data-profile-pwd-old]").value = "";
        document.querySelector("[data-profile-pwd-new]").value = "";
        document.querySelector("[data-profile-pwd-confirm]").value = "";
        
        setTimeout(() => {
            msg.classList.add("hidden");
        }, 3000);
    });

    let hasPayrollPin = false;

    const renderProfile = () => {
        document.querySelectorAll("[data-profile-input]").forEach(input => {
            const btn = input.nextElementSibling;
            if (btn && btn.tagName === 'BUTTON') {
                btn.innerHTML = '<i class="fa-solid fa-lock text-slate-400"></i>';
                btn.title = hasPayrollPin ? "YÃªu cáº§u mÃ£ PIN" : "ChÆ°a cÃ i mÃ£ PIN";
                btn.onclick = () => alert("Báº¡n cáº§n xÃ¡c thá»±c PIN trÃªn á»©ng dá»¥ng Ä‘iá»‡n thoáº¡i Ä‘á»ƒ chá»‰nh sá»­a.");
            }
        });
    };

    const loadProfile = async () => {
        try {
            const res = await fetch("/Manager/GetProfile");
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
                document.querySelectorAll("[data-profile-name]").forEach(el => {
                    if (el.tagName === "INPUT") el.value = result.data.fullName || "";
                    else el.textContent = result.data.fullName || "";
                });
                document.querySelectorAll("[data-profile-department]").forEach(el => el.value = result.data.department || "");
                document.querySelectorAll("[data-profile-phone]").forEach(el => el.value = result.data.phone || "");
                document.querySelectorAll("[data-profile-email]").forEach(el => el.value = result.data.email || "");
                document.querySelectorAll("[data-profile-role]").forEach(el => el.textContent = "Quáº£n lÃ½");
                renderProfile();
            }
        } catch (e) { console.error("Error loading profile", e); }
    };

    const compressImage = (file, callback) => {
        const reader = new FileReader();
        reader.onload = (event) => {
            const img = new Image();
            img.onload = () => {
                const canvas = document.createElement("canvas");
                let width = img.width;
                let height = img.height;
                const maxSize = 800;
                if (width > height && width > maxSize) {
                    height *= maxSize / width;
                    width = maxSize;
                } else if (height > maxSize) {
                    width *= maxSize / height;
                    height = maxSize;
                }
                canvas.width = width;
                canvas.height = height;
                const ctx = canvas.getContext("2d");
                ctx.drawImage(img, 0, 0, width, height);
                const dataUrl = canvas.toDataURL("image/jpeg", 0.7);
                callback(dataUrl);
            };
            img.src = event.target.result;
        };
        reader.readAsDataURL(file);
    };

    document.querySelectorAll("[data-avatar-input]").forEach((input) => {
        input.addEventListener("change", async (e) => {
            const file = e.target.files[0];
            if (!file) return;

            const uploadAvatar = async (dataUrl) => {
                document.querySelectorAll("[data-avatar-image]").forEach(img => {
                    img.src = dataUrl;
                    img.classList.remove("hidden");
                });
                document.querySelectorAll("[data-avatar-fallback]").forEach(icon => icon.classList.add("hidden"));
                
                const formData = new FormData();
                formData.append("avatarBase64", dataUrl);
                formData.append("fileName", file.name);
                try {
                    await fetch("/Manager/UploadAvatar", { method: "POST", body: formData });
                } catch(err) { console.error("Error uploading avatar", err); }
            };

            if (file.type === "image/gif") {
                const reader = new FileReader();
                reader.onload = (event) => uploadAvatar(event.target.result);
                reader.readAsDataURL(file);
            } else {
                compressImage(file, uploadAvatar);
            }
        });
    });

    // --- Face Update ---
    const faceUpdateBtn = document.querySelector("[data-profile-update-face]");
    if (faceUpdateBtn) {
        faceUpdateBtn.classList.remove("hidden");
    }

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

    faceUpdateBtn?.addEventListener("click", openFaceModal);
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



    loadProfile();

})();


window.handleTestVideoSelect = async (event) => {
    const file = event.target.files[0];
    if (!file) return;
    const formData = new FormData();
    formData.append('file', file);
    
    try {
        const res = await fetch('/Manager/UploadTestVideo', {
            method: 'POST',
            body: formData
        });
        const data = await res.json();
        if (data.success) {
            alert(data.message);
        } else {
            alert('Lỗi: ' + data.message);
        }
    } catch(e) {
        console.error(e);
        alert('Lỗi khi tải lên video');
    }
};

