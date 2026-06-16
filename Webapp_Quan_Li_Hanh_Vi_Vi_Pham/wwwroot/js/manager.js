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

    window.managerEmployeeList = [];
    window.managerViolationAssignees = [];

    const escapeHtml = (value) => String(value ?? "")
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;")
        .replaceAll("'", "&#039;");

    const loadViolationAssignees = async () => {
        if (Array.isArray(window.managerViolationAssignees) && window.managerViolationAssignees.length > 0) {
            return window.managerViolationAssignees;
        }

        try {
            const res = await fetch('/Manager/GetViolationAssignees');
            const data = await res.json();
            window.managerViolationAssignees = data.success && Array.isArray(data.data) ? data.data : [];
        } catch (err) {
            console.error("Failed to load violation assignees", err);
            window.managerViolationAssignees = [];
        }

        return window.managerViolationAssignees;
    };
    const loadEmployees = async () => {
        const tbody = document.getElementById("employeeListTbody");
        if (!tbody) return;
        try {
            const res = await fetch('/Manager/GetAllEmployees');
            const data = await res.json();
            if (data.success) {
                window.managerEmployeeList = data.data;
                renderEmployees(data.data);
            }
        } catch(err) { console.error(err); }
    };

    const renderEmployees = (list) => {
        const tbody = document.getElementById("employeeListTbody");
        if (!tbody) return;
        if (list.length === 0) {
            tbody.innerHTML = `<tr><td colspan="5" class="p-4 text-center text-slate-500">Không tìm thấy nhân viên</td></tr>`;
            return;
        }
        tbody.innerHTML = list.map(e => `
            <tr class="hover:bg-slate-50 border-b border-slate-100 transition-colors">
                <td class="p-4 py-3"><span class="font-medium text-slate-900">${e.employeeCode || 'N/A'}</span></td>
                <td class="p-4 py-3 text-slate-700">${e.fullName}</td>
                <td class="p-4 py-3 text-slate-500">${e.department || 'N/A'}</td>
                <td class="p-4 py-3 text-slate-500">${e.username}</td>
                <td class="p-4 py-3 text-center">
                    <button onclick="window.resetEmployeePassword('${e.id}', '${e.employeeCode || e.fullName}')" class="text-slate-400 hover:text-amber-500 transition" title="Reset mật khẩu">
                        <i class="fa-solid fa-key text-lg"></i>
                    </button>
                </td>
            </tr>
        `).join('');
    };

    document.getElementById("employeeSearchInput")?.addEventListener("input", (e) => {
        const term = e.target.value.toLowerCase();
        if (!term) {
            renderEmployees(window.managerEmployeeList);
            return;
        }
        const filtered = window.managerEmployeeList.filter(emp => 
            (emp.fullName && emp.fullName.toLowerCase().includes(term)) ||
            (emp.employeeCode && emp.employeeCode.toLowerCase().includes(term)) ||
            (emp.username && emp.username.toLowerCase().includes(term))
        );
        renderEmployees(filtered);
    });

    window.openAddEmployeeModal = () => {
        document.getElementById("addEmpCode").value = "";
        document.getElementById("addEmpName").value = "";
        document.getElementById("addEmpUsername").value = "";
        document.getElementById("addEmpPassword").value = "";
        document.getElementById("addEmpDept").value = "";
        
        const modal = document.getElementById("addEmployeeModal");
        modal.classList.remove("hidden");
        modal.classList.add("flex");
        setTimeout(() => modal.querySelector('.saas-card').style.transform = 'scale(1)', 10);
    };

    window.closeAddEmployeeModal = () => {
        const modal = document.getElementById("addEmployeeModal");
        modal.classList.add("hidden");
        modal.classList.remove("flex");
    };

    window.submitAddEmployee = async () => {
        const payload = {
            EmployeeCode: document.getElementById("addEmpCode").value,
            FullName: document.getElementById("addEmpName").value,
            Username: document.getElementById("addEmpUsername").value,
            Password: document.getElementById("addEmpPassword").value,
            Department: document.getElementById("addEmpDept").value
        };

        if (!payload.EmployeeCode || !payload.FullName || !payload.Username) {
            alert("Vui lòng điền các trường bắt buộc (*)");
            return;
        }

        try {
            const res = await fetch('/Manager/AddEmployee', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });
            const data = await res.json();
            if (data.success) {
                alert("Thêm nhân viên thành công!");
                window.closeAddEmployeeModal();
                loadEmployees();
                if (typeof loadHomeStats === 'function') loadHomeStats();
            } else {
                alert(data.message || "Có lỗi xảy ra");
            }
        } catch (err) {
            console.error(err);
            alert("Lỗi kết nối");
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
            const assignees = await loadViolationAssignees();
            const res = await fetch('/Manager/GetAllViolations');
            const data = await res.json();
            if (!data.success) {
                tbody.innerHTML = `<tr><td colspan="10" class="p-8 text-center text-red-500">${escapeHtml(data.message || 'Không tải được lịch sử vi phạm.')}</td></tr>`;
                return;
            }

            if (data.success) {
                if (data.data.length === 0) {
                    tbody.innerHTML = `<tr><td colspan="10" class="p-8 text-center text-slate-400">Chưa có vi phạm nào.</td></tr>`;
                    return;
                }

                const buildTelegramState = (violation) => {
                    if (violation.telegramSent) {
                        return `
                            <div class="flex flex-col gap-1">
                                <span class="px-2.5 py-1 text-[10px] font-bold rounded-full bg-emerald-100 text-emerald-700">Đã đồng bộ</span>
                                ${violation.telegramSentAtUtc ? `<span class="text-[11px] text-slate-400">${new Date(violation.telegramSentAtUtc).toLocaleString('vi-VN')}</span>` : ''}
                                ${violation.telegramTargetChatIds ? `<span class="text-[11px] text-slate-400">Chat: ${violation.telegramTargetChatIds}</span>` : ''}
                            </div>`;
                    }

                    return `
                        <div class="flex flex-col gap-1">
                            <span class="px-2.5 py-1 text-[10px] font-bold rounded-full bg-amber-100 text-amber-700">Chưa đồng bộ</span>
                            ${violation.telegramLastError ? `<span class="text-[11px] text-red-500">${violation.telegramLastError}</span>` : '<span class="text-[11px] text-slate-400">Chưa có phản hồi gửi.</span>'}
                        </div>`;
                };

                const buildEmployeeCell = (violation) => {
                    const currentCode = violation.employeeCode || "";
                    const currentName = violation.employeeName || "";
                    if (violation.status !== 'Pending') {
                        return `
                            <div class="flex flex-col gap-1">
                                <span class="font-semibold text-slate-800">${escapeHtml(currentName || currentCode || 'Chưa gán')}</span>
                                <span class="text-[11px] text-slate-400">${escapeHtml(currentCode || 'N/A')}</span>
                            </div>`;
                    }

                    const options = assignees.map(emp => {
                        const code = emp.employeeCode || emp.username || '';
                        const label = `${emp.fullName || emp.username} (${code || 'N/A'})`;
                        const selected = currentCode && (currentCode === emp.employeeCode || currentCode === emp.username) ? 'selected' : '';
                        return `<option value="${emp.id}" ${selected}>${escapeHtml(label)}</option>`;
                    }).join('');

                    return `
                        <select data-violation-employee="${violation.id}" class="min-w-[220px] rounded-lg border border-slate-200 bg-white px-3 py-2 text-xs font-semibold text-slate-700 shadow-sm focus:border-red-400 focus:outline-none focus:ring-2 focus:ring-red-100">
                            <option value="">Chọn nhân viên vi phạm</option>
                            ${options}
                        </select>
                        ${currentCode ? `<div class="mt-1 text-[11px] text-slate-400">Đang gán: ${escapeHtml(currentName || currentCode)}</div>` : ''}
                    `;
                };

                tbody.innerHTML = data.data.map(v => {
                    const evidencePreview = v.hasEvidenceImage
                        ? `<div id="violationEvidence-${v.id}" class="min-w-32 text-left">
                                <span class="text-[11px] text-slate-400">Đang tải ảnh...</span>
                           </div>`
                        : `<div class="mb-2 text-[11px] text-slate-400">Chưa có ảnh minh chứng</div>`;

                    return `
                    <tr class="hover:bg-slate-50 border-b border-slate-100">
                        <td class="p-4 py-3 text-slate-900 font-semibold">${escapeHtml(v.trackingId || 'N/A')}</td>
                        <td class="p-4 py-3 text-slate-900 font-medium">${buildEmployeeCell(v)}</td>
                        <td class="p-4 py-3 text-slate-700">${escapeHtml(v.violationType)}</td>
                        <td class="p-4 py-3 text-slate-500">${escapeHtml(v.cameraLocation)}</td>
                        <td class="p-4 py-3 text-slate-500">${new Date(v.detectedAtUtc).toLocaleString('vi-VN')}</td>
                        <td class="p-4 py-3"><span class="px-2.5 py-1 text-[10px] font-bold rounded-full bg-red-100 text-red-700">${escapeHtml(v.severity)}</span></td>
                        <td class="p-4 py-3">
                            <div class="flex flex-col gap-1">
                                <span class="px-2.5 py-1 text-[10px] font-bold rounded-full ${v.status === 'Approved' ? 'bg-green-100 text-green-700' : v.status === 'Rejected' ? 'bg-red-100 text-red-700' : 'bg-amber-100 text-amber-700'}">${v.status}</span>
                                ${(v.reviewedBy || v.reviewedAtUtc) ? `<span class="text-[11px] text-slate-400">${v.reviewedBy || 'Manager'}${v.reviewedAtUtc ? ' • ' + new Date(v.reviewedAtUtc).toLocaleString('vi-VN') : ''}</span>` : ''}
                            </div>
                        </td>
                        <td class="p-4 py-3">${evidencePreview}</td>
                        <td class="p-4 py-3">${buildTelegramState(v)}</td>
                        <td class="p-4 py-3 text-right">
                            <div class="flex justify-end gap-2 flex-wrap">
                                ${v.complaintReason ? `<button
                                    data-vid="${v.id}"
                                    data-vtid="${(v.trackingId || 'N/A').replace(/"/g, '&quot;')}"
                                    data-vreason="${(v.complaintReason || '').replace(/"/g, '&quot;').replace(/\n/g, '&#10;')}"
                                    data-vtime="${v.complaintSubmittedAtUtc || ''}"
                                    data-vreviewed="${v.reviewChannel === 'ComplaintReview' ? '1' : '0'}"
                                    data-vstatus="${v.status || ''}"
                                    data-vreviewedby="${(v.reviewedBy || '').replace(/"/g, '&quot;')}"
                                    data-vreviewnote="${(v.reviewNote || '').replace(/"/g, '&quot;').replace(/\n/g, '&#10;')}"
                                    data-vreviewedat="${v.reviewedAtUtc || ''}"
                                    onclick="window.openViolationComplaintModal(this.dataset.vid, this.dataset.vtid, this.dataset.vreason, this.dataset.vtime, this.dataset.vreviewed, this.dataset.vstatus, this.dataset.vreviewedby, this.dataset.vreviewnote, this.dataset.vreviewedat)"
                                    class="rounded border border-amber-200 bg-amber-50 px-2.5 py-1 text-xs font-semibold text-amber-700 hover:bg-amber-100">Xem khiếu nại</button>` : ''}
                                <button onclick="window.resendViolationTelegram('${v.id}')" class="rounded border border-slate-200 px-2.5 py-1 text-xs font-semibold text-slate-600 hover:border-red-200 hover:text-red-600">Gửi Telegram</button>
                                ${v.status === 'Pending' ? `
                                <button onclick="window.reviewViolation('${v.id}', 'Approved')" class="rounded bg-emerald-500 px-2.5 py-1 text-xs font-semibold text-white hover:bg-emerald-600">Duyệt</button>
                                <button onclick="window.reviewViolation('${v.id}', 'Rejected')" class="rounded bg-red-500 px-2.5 py-1 text-xs font-semibold text-white hover:bg-red-600">Từ chối</button>
                                ` : `<span class="px-2 py-1 text-xs text-slate-400">${v.reviewChannel || 'Đã xử lý'}</span>`}
                            </div>
                        </td>
                    </tr>
                `;
                }).join('');
                data.data
                    .filter(v => v.hasEvidenceImage)
                    .forEach(v => window.previewViolationEvidence(v.id, false));
            }
        } catch(err) {
            console.error(err);
            tbody.innerHTML = `<tr><td colspan="10" class="p-8 text-center text-red-500">Lỗi tải lịch sử vi phạm. Vui lòng thử lại.</td></tr>`;
        }
    };

    window.previewViolationEvidence = async (id, showLoading = true) => {
        const container = document.getElementById(`violationEvidence-${id}`);
        if (!container) return;

        if (showLoading) {
            container.innerHTML = `<span class="text-[11px] text-slate-400">Đang tải ảnh...</span>`;
        }
        try {
            const res = await fetch(`/Manager/GetViolationEvidence?id=${encodeURIComponent(id)}`);
            const payload = await res.json();
            if (!payload.success || !payload.data?.evidenceImageDataUrl) {
                throw new Error(payload.message || "Không tải được ảnh minh chứng.");
            }

            container.innerHTML = `
                <div class="overflow-hidden rounded-lg border border-slate-200 bg-slate-50">
                    <img src="${escapeHtml(payload.data.evidenceImageDataUrl)}" alt="Ảnh minh chứng vi phạm" class="h-20 w-32 object-cover select-none" draggable="false">
                </div>
                <p class="mt-1 text-[10px] font-semibold text-emerald-600"><i class="fa-solid fa-lock mr-1"></i>Ảnh mã hóa nội bộ</p>`;
        } catch (err) {
            console.error(err);
            container.innerHTML = `<button type="button" onclick="window.previewViolationEvidence('${id}')" class="rounded border border-red-200 bg-red-50 px-2.5 py-1 text-xs font-semibold text-red-600">Tải lại ảnh</button>`;
        }
    };

    window.reviewViolation = async (id, status) => {
        const employeeSelect = document.querySelector(`[data-violation-employee="${id}"]`);
        const employeeId = employeeSelect?.value || "";
        if (status === 'Approved' && !employeeId) {
            alert('Vui lòng chọn nhân viên vi phạm trước khi duyệt.');
            employeeSelect?.focus();
            return;
        }

        const note = status === 'Rejected'
            ? (prompt('Nhập ghi chú từ chối vi phạm:') || 'Manager từ chối từ dashboard')
            : 'Manager duyệt từ dashboard';

        if (!confirm(`Xác nhận cập nhật vi phạm sang trạng thái ${status}?`)) return;

        try {
            const params = new URLSearchParams({
                id,
                status,
                note,
                employeeId
            });
            const res = await fetch(`/Manager/ReviewViolationAssignment?${params.toString()}`, { method: 'POST' });
            const data = await res.json();
            if (data.success) {
                alert(data.message || 'Đã cập nhật vi phạm.');
                loadViolations();
                loadHomeStats();
            } else {
                alert(data.message || 'Có lỗi xảy ra');
            }
        } catch (err) {
            console.error(err);
            alert('Không thể cập nhật vi phạm');
        }
    };

    window.resendViolationTelegram = async (id) => {
        if (!confirm('Gửi lại thông báo Telegram cho vi phạm này?')) return;
        try {
            const res = await fetch(`/Manager/ResendViolationTelegram?id=${id}`, { method: 'POST' });
            const data = await res.json();
            const hint = document.getElementById('violationTelegramSyncHint');
            if (hint) {
                hint.textContent = data.message || (data.success ? 'Đã gửi Telegram.' : 'Gửi Telegram thất bại.');
                hint.className = `border-b border-slate-100 px-4 py-3 text-xs ${data.success ? 'bg-emerald-50 text-emerald-700' : 'bg-red-50 text-red-700'}`;
            }
            if (data.success) {
                loadViolations();
            } else {
                alert(data.message || 'Không thể gửi lại Telegram');
            }
        } catch (err) {
            console.error(err);
            alert('Không thể gửi lại Telegram');
        }
    };
    window.openViolationComplaintModal = (violationId, trackingId, reason, submittedAtUtc, reviewed, status, reviewedBy, reviewNote, reviewedAt) => {
        const modal = document.getElementById('violationComplaintModal');
        if (!modal) return;

        const idEl = document.getElementById('violationComplaintId');
        const trackingEl = document.getElementById('violationComplaintTrackingId');
        const timeEl = document.getElementById('violationComplaintSubmittedAt');
        const reasonEl = document.getElementById('violationComplaintReason');
        const noteEl = document.getElementById('violationComplaintReviewNote');
        const actionBtns = document.getElementById('violationComplaintActions');
        const reviewResultEl = document.getElementById('violationComplaintReviewResult');

        if (idEl) idEl.value = violationId || '';
        if (trackingEl) trackingEl.textContent = trackingId || 'N/A';
        if (timeEl) {
            timeEl.textContent = submittedAtUtc
                ? new Date(submittedAtUtc).toLocaleString('vi-VN')
                : '--';
        }
        if (reasonEl) reasonEl.textContent = reason || '--';
        if (noteEl) noteEl.value = '';

        // More robust isReviewed check
        const isReviewed = reviewed === '1' || status === 'Approved' || status === 'Rejected';

        const closeOnlyBtn = document.getElementById('violationComplaintCloseOnlyBtn');

        // Show/hide action buttons
        if (actionBtns) actionBtns.style.display = isReviewed ? 'none' : 'flex';
        if (closeOnlyBtn) closeOnlyBtn.style.display = isReviewed ? 'flex' : 'none';

        // Show/hide review result section
        if (reviewResultEl) {
            if (isReviewed) {
                const isAccepted = status === 'Approved';
                const decisionLabel = isAccepted ? 'Chấp nhận' : 'Từ chối';
                const decisionColor = isAccepted ? '#059669' : '#dc2626';
                const decisionBg = isAccepted ? '#ecfdf5' : '#fef2f2';
                const decisionBorder = isAccepted ? '#a7f3d0' : '#fecaca';
                const icon = isAccepted ? 'fa-check-circle' : 'fa-times-circle';
                reviewResultEl.style.display = 'block';
                reviewResultEl.innerHTML = `
                    <div style="border: 1px solid ${decisionBorder}; background: ${decisionBg}; border-radius: 0.75rem; padding: 1rem;">
                        <p style="font-size: 10px; font-weight: 700; text-transform: uppercase; letter-spacing: 0.05em; color: #94a3b8; margin: 0 0 0.75rem;">Kết quả xử lý</p>
                        <div style="display: flex; align-items: center; gap: 0.5rem; margin-bottom: 0.5rem;">
                            <i class="fa-solid ${icon}" style="color: ${decisionColor}; font-size: 1.1rem;"></i>
                            <span style="font-weight: 700; color: ${decisionColor}; font-size: 0.9rem;">${decisionLabel} khiếu nại</span>
                        </div>
                        ${reviewedBy ? `<p style="font-size: 0.75rem; color: #64748b; margin: 0.25rem 0;"><b>Người xử lý:</b> ${reviewedBy}</p>` : ''}
                        ${reviewedAt ? `<p style="font-size: 0.75rem; color: #64748b; margin: 0.25rem 0;"><b>Thời gian:</b> ${new Date(reviewedAt).toLocaleString('vi-VN')}</p>` : ''}
                        ${reviewNote ? `<p style="font-size: 0.75rem; color: #64748b; margin: 0.25rem 0;"><b>Ghi chú:</b> ${reviewNote}</p>` : ''}
                    </div>`;
            } else {
                reviewResultEl.style.display = 'none';
                reviewResultEl.innerHTML = '';
            }
        }

        modal.classList.remove('hidden');
        modal.style.display = 'flex';
        modal.style.alignItems = 'center';
        modal.style.justifyContent = 'center';
    };

    window.closeViolationComplaintModal = () => {
        const modal = document.getElementById('violationComplaintModal');
        if (!modal) return;
        modal.classList.add('hidden');
        modal.style.display = '';
    };

    window.submitComplaintReview = async (decision) => {
        const violationId = document.getElementById('violationComplaintId')?.value;
        const note = document.getElementById('violationComplaintReviewNote')?.value.trim() || '';
        const trackingId = document.getElementById('violationComplaintTrackingId')?.textContent;

        if (!violationId) {
            alert('Không xác định được mã vi phạm.');
            return;
        }

        const decisionText = decision === 'Accepted' ? 'chấp nhận' : 'từ chối';
        if (!confirm(`Bạn có chắc muốn ${decisionText} khiếu nại vi phạm ${trackingId}?`)) return;

        try {
            const res = await fetch('/Manager/ReviewComplaint', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ ViolationId: violationId, Decision: decision, ReviewNote: note })
            });
            const data = await res.json();
            if (data.success) {
                const actionBtns = document.getElementById('violationComplaintActions');
                const reviewResultEl = document.getElementById('violationComplaintReviewResult');
                const closeOnlyBtn = document.getElementById('violationComplaintCloseOnlyBtn');

                if (actionBtns) actionBtns.style.display = 'none';
                if (closeOnlyBtn) closeOnlyBtn.style.display = 'flex';

                if (reviewResultEl) {
                    const isAccepted = decision === 'Accepted';
                    const decisionLabel = isAccepted ? 'Chấp nhận' : 'Từ chối';
                    const decisionColor = isAccepted ? '#059669' : '#dc2626';
                    const decisionBg = isAccepted ? '#ecfdf5' : '#fef2f2';
                    const decisionBorder = isAccepted ? '#a7f3d0' : '#fecaca';
                    const icon = isAccepted ? 'fa-check-circle' : 'fa-times-circle';
                    reviewResultEl.style.display = 'block';
                    reviewResultEl.innerHTML = `
                        <div style="border: 1px solid ${decisionBorder}; background: ${decisionBg}; border-radius: 0.75rem; padding: 1rem;">
                            <p style="font-size: 10px; font-weight: 700; text-transform: uppercase; letter-spacing: 0.05em; color: #94a3b8; margin: 0 0 0.75rem;">Kết quả xử lý</p>
                            <div style="display: flex; align-items: center; gap: 0.5rem; margin-bottom: 0.5rem;">
                                <i class="fa-solid ${icon}" style="color: ${decisionColor}; font-size: 1.1rem;"></i>
                                <span style="font-weight: 700; color: ${decisionColor}; font-size: 0.9rem;">${decisionLabel} khiếu nại</span>
                            </div>
                            <p style="font-size: 0.75rem; color: #64748b; margin: 0.25rem 0;"><b>Thời gian xử lý:</b> Vừa xong</p>
                            ${note ? `<p style="font-size: 0.75rem; color: #64748b; margin: 0.25rem 0;"><b>Ghi chú:</b> ${note.replace(/</g, '&lt;').replace(/>/g, '&gt;')}</p>` : ''}
                        </div>`;
                }
                loadViolations();
            } else {
                alert('Lỗi: ' + (data.message || 'Không thể xử lý khiếu nại.'));
            }
        } catch (err) {
            console.error(err);
            alert('Có lỗi xảy ra khi gửi kết quả khiếu nại.');
        }
    };

    document.getElementById('violationTelegramTestBtn')?.addEventListener('click', async () => {
        try {
            const res = await fetch('/Manager/SendViolationTelegramTest', { method: 'POST' });
            const data = await res.json();
            const hint = document.getElementById('violationTelegramSyncHint');
            if (hint) {
                hint.textContent = data.success
                    ? `Testcase Telegram OA thành công. Chat: ${data.chatId || 'N/A'}`
                    : `Testcase Telegram OA thất bại. ${data.message || ''}`;
                hint.className = `border-b border-slate-100 px-4 py-3 text-xs ${data.success ? 'bg-emerald-50 text-emerald-700' : 'bg-red-50 text-red-700'}`;
            }
            if (!data.success) {
                alert(data.message || 'Gửi testcase Telegram thất bại.');
            }
        } catch (err) {
            console.error(err);
            alert('Không thể gửi testcase Telegram OA.');
        }
    });

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
                    if (r.status === 'Đã duyệt' || r.status === 'Approved') tone = 'bg-green-100 text-green-700';
                    else if (r.status === 'Từ chối' || r.status === 'Rejected') tone = 'bg-red-100 text-red-700';
                    else tone = 'bg-amber-100 text-amber-700';
                    
                    return `
                    <tr class="hover:bg-slate-50 border-b border-slate-100">
                        <td class="p-4 py-3 text-slate-900 font-medium">${r.employeeName || 'N/A'}</td>
                        <td class="p-4 py-3 text-slate-700">
                            <div class="font-bold">${r.requestType}</div>
                            <div class="text-[10px] text-slate-400 mt-1">${r.content.replace(/\n/g, '<br>')}</div>
                        </td>
                        <td class="p-4 py-3 text-slate-500">${new Date(r.submittedAt).toLocaleDateString('vi-VN')}</td>
                        <td class="p-4 py-3"><span class="px-2.5 py-1 text-[10px] font-bold rounded-full ${tone}">${r.status}</span></td>
                        <td class="p-4 py-3 text-right">
                            ${r.status === 'Chờ duyệt' || r.status === 'Pending' ? `
                            <button onclick="openRequestDetailModal(${r.id})" class="px-3 py-1.5 bg-blue-500 text-white rounded shadow text-xs hover:bg-blue-600 mr-1"><i class="fa-solid fa-eye"></i> Chi tiết</button>
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
        
        const contentContainer = document.getElementById("requestDetailPreviewContent");
        if(contentContainer) {
            let formattedContent = req.content.replace(/\r?\n/g, '<br>');
            if (window.buildDocHtml && window.buildDoc) {
                let applyDate = '';
                let reason = '';
                req.content.split('\n').forEach(line => {
                    if(line.startsWith('Ngày áp dụng:')) applyDate = line.replace('Ngày áp dụng:', '').trim();
                    else if(line.startsWith('Lý do:')) reason = line.replace('Lý do:', '').trim();
                });
                
                const name = req.employeeName || "[Tên nhân viên]";
                const department = "[Bộ phận]"; // Or try to look up in managerEmployeeList
                const dateStr = applyDate || new Date(req.submittedAt).toLocaleDateString('vi-VN');
                const reasonStr = reason || req.content;
                
                let builder = window.buildDoc["Nghỉ phép"];
                if (req.requestType.includes("Nghỉ phép")) builder = window.buildDoc["Nghỉ phép"];
                else if (req.requestType.includes("muộn") || req.requestType.includes("về sớm")) builder = window.buildDoc["Đi muộn"];
                else if (req.requestType.includes("Tăng ca") || req.requestType.includes("thêm giờ")) builder = window.buildDoc["Tăng ca"];
                else if (req.requestType.includes("Điều chỉnh ca")) builder = window.buildDoc["Điều chỉnh ca"];
                
                if (builder) formattedContent = builder(name, department, dateStr, reasonStr);
            }

            contentContainer.innerHTML = formattedContent;
            
            let stamp = document.getElementById("managerReqDetailStatus");
            if (!stamp) {
                stamp = document.createElement("div");
                stamp.id = "managerReqDetailStatus";
                contentContainer.appendChild(stamp);
            }
            stamp.textContent = req.status;
            let displayStatus = req.status.toUpperCase();
            const actionButtonsContainer = document.getElementById("managerRequestDetailActions");
            
            if(displayStatus.includes('DUY') || displayStatus.includes('APPROVED')) {
                stamp.className = "absolute bottom-12 left-12 border-4 px-6 py-3 text-2xl font-bold uppercase -rotate-12 opacity-80 border-emerald-500 text-emerald-500 rounded-xl pointer-events-none bg-white/50 backdrop-blur-sm";
                stamp.textContent = "ĐÃ DUYỆT";
                if(actionButtonsContainer) actionButtonsContainer.classList.add('hidden');
            } else if (displayStatus.includes('TỪ') || displayStatus.includes('CHỐI') || displayStatus.includes('REJECTED')) {
                stamp.className = "absolute bottom-12 left-12 border-4 px-6 py-3 text-2xl font-bold uppercase -rotate-12 opacity-80 border-red-500 text-red-500 rounded-xl pointer-events-none bg-white/50 backdrop-blur-sm";
                stamp.textContent = "TỪ CHỐI";
                if(actionButtonsContainer) actionButtonsContainer.classList.add('hidden');
            } else {
                stamp.className = "absolute bottom-12 left-12 border-4 px-6 py-3 text-2xl font-bold uppercase -rotate-12 opacity-80 border-amber-500 text-amber-500 rounded-xl pointer-events-none bg-white/50 backdrop-blur-sm";
                stamp.textContent = "CHỜ DUYỆT";
                if(actionButtonsContainer) actionButtonsContainer.classList.remove('hidden');
                if(actionButtonsContainer) actionButtonsContainer.classList.add('flex');
            }
        }
        
        document.getElementById("managerRequestDetailModal").dataset.currentId = id;
        document.getElementById("managerRequestDetailModal").classList.remove("hidden");
        document.getElementById("managerRequestDetailModal").classList.add("flex");
    };

    window.closeManagerRequestModal = () => {
        document.getElementById("managerRequestDetailModal").classList.add("hidden");
        document.getElementById("managerRequestDetailModal").classList.remove("flex");
    };

    window.approveCurrentRequest = () => {
        const id = document.getElementById("managerRequestDetailModal").dataset.currentId;
        if(id) {
            updateRequestStatus(id, 'Đã duyệt');
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
    
    window.updateRequestStatus = (id, status) => {
        if (!confirm('Xác nhận ' + status + ' đơn này?')) return;
        fetch(`/Manager/UpdateRequestStatus?id=${id}&status=${encodeURIComponent(status)}`, { method: 'POST' })
            .then(res => res.json())
            .then(data => {
                if (data.success) {
                    loadRequests();
                } else {
                    alert('Có lỗi xảy ra');
                }
            })
            .catch(e => {
                console.error(e);
            });
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

    window.loadManagerChatContacts = loadChatContacts;
    window.loadManagerConversation = loadManagerConversation;

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
            const sentAtFormatted = new Date(m.sentAt).toLocaleTimeString('vi-VN');
            const editedHtml = m.editedAtUtc && !m.isRevoked ? '<span class="text-[10px] text-slate-400 italic ml-1">(đã chỉnh sửa)</span>' : '';
            
            if (isSelf) {
                return `
                <div class="flex flex-col items-end group mb-3">
                    <div class="text-[10px] text-slate-400 mb-1 px-1">${sentAtFormatted}${editedHtml}</div>
                    ${m.isRevoked ? `
                    <div class="max-w-md rounded-2xl bg-slate-100 px-4 py-3 text-sm text-slate-400 italic border border-dashed border-slate-200 opacity-75">
                        Tin nhắn đã bị thu hồi
                    </div>
                    ` : `
                    <div class="flex items-center gap-2">
                        <button onclick="window.editManagerMessage(${m.id})" class="text-[11px] text-slate-400 hover:text-blue-600 font-medium transition-colors px-2 py-1">Chỉnh sửa</button>
                        <button onclick="window.revokeManagerMessage(${m.id})" class="text-[11px] text-slate-400 hover:text-red-600 font-medium transition-colors px-2 py-1">Thu hồi</button>
                        <div class="max-w-md rounded-2xl bg-red-600 px-4 py-3 text-sm text-white">
                            ${m.content}
                        </div>
                    </div>
                    `}
                </div>`;
            } else {
                return `
                <div class="flex flex-col items-start mb-3">
                    <div class="text-[10px] text-slate-400 mb-1 px-1">${sentAtFormatted}${editedHtml}</div>
                    ${m.isRevoked ? `
                    <div class="max-w-md rounded-2xl bg-slate-50 px-4 py-3 text-sm text-slate-400 italic border border-dashed border-slate-100 opacity-75">
                        Tin nhắn đã bị thu hồi
                    </div>
                    ` : `
                    <div class="max-w-md rounded-2xl bg-slate-100 px-4 py-3 text-sm text-slate-700">
                        ${m.content}
                    </div>
                    `}
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
                const res = await fetch('/Manager/EditMessage', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ Id: managerEditingMessageId, Content: text })
                });
                const data = await res.json();
                if (data.success) {
                    clearManagerMessageEditing();
                    loadManagerConversation();
                    loadChatContacts();
                }
            } catch (e) { console.error(e); }
        } else {
            try {
                const res = await fetch('/Manager/SendMessage', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ EmployeeId: managerActiveEmployeeId, Content: text })
                });
                const data = await res.json();
                if (data.success) {
                    input.value = "";
                    loadManagerConversation();
                    loadChatContacts();
                }
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
            const res = await fetch('/Manager/RevokeMessage', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ Id: id })
            });
            const data = await res.json();
            if (data.success) {
                loadManagerConversation();
                loadChatContacts();
            }
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

        // Load Recent Requests widget
        try {
            const reqTbody = document.getElementById('homeRecentRequestsTbody');
            if (reqTbody) {
                const res = await fetch('/Manager/GetAllRequests');
                const data = await res.json();
                if (data.success && data.data.length > 0) {
                    const pending = data.data.filter(r => r.status === 'Pending' || r.status === 'Chờ duyệt').slice(0, 8);
                    const display = pending.length > 0 ? pending : data.data.slice(0, 8);
                    reqTbody.innerHTML = display.map(r => `
                        <tr class="border-b border-slate-50 hover:bg-slate-50 transition">
                            <td class="px-4 py-3">
                                <p class="font-semibold text-slate-800 text-xs">${r.employeeName || 'N/A'}</p>
                                <p class="text-[11px] text-slate-400">${r.requestType || ''}</p>
                            </td>
                            <td class="px-4 py-3 text-right">
                                <span class="px-2 py-0.5 text-[10px] font-bold rounded-full ${r.status === 'Approved' || r.status === 'Đã duyệt' ? 'bg-green-100 text-green-700' : r.status === 'Rejected' || r.status === 'Từ chối' ? 'bg-red-100 text-red-700' : 'bg-amber-100 text-amber-700'}">${r.status}</span>
                            </td>
                        </tr>`).join('');
                } else {
                    reqTbody.innerHTML = '<tr><td class="p-4 text-center text-slate-400 text-sm">Không có đơn từ chờ duyệt.</td></tr>';
                }
            }
        } catch (err) { console.error('homeRecentRequests error', err); }

        // Load Recent Violations widget
        try {
            const violTbody = document.getElementById('homeRecentViolationsTbody');
            if (violTbody) {
                const res = await fetch('/Manager/GetAllViolations');
                const data = await res.json();
                if (data.success && data.data.length > 0) {
                    violTbody.innerHTML = data.data.slice(0, 8).map(v => `
                        <tr class="border-b border-slate-50 hover:bg-slate-50 transition">
                            <td class="px-4 py-3">
                                <p class="font-semibold text-slate-800 text-xs">${v.violationType || 'N/A'}</p>
                                <p class="text-[11px] text-slate-400">${v.employeeName || v.employeeCode || 'Chưa gán'} • ${new Date(v.detectedAtUtc).toLocaleString('vi-VN')}</p>
                            </td>
                            <td class="px-4 py-3 text-right">
                                <span class="px-2 py-0.5 text-[10px] font-bold rounded-full ${v.status === 'Approved' ? 'bg-green-100 text-green-700' : v.status === 'Rejected' ? 'bg-red-100 text-red-700' : 'bg-amber-100 text-amber-700'}">${v.status}</span>
                            </td>
                        </tr>`).join('');
                } else {
                    violTbody.innerHTML = '<tr><td class="p-4 text-center text-slate-400 text-sm">Chưa ghi nhận vi phạm nào.</td></tr>';
                }
            }
        } catch (err) { console.error('homeRecentViolations error', err); }
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
                    tbody.innerHTML = `<tr><td colspan="8" class="p-8 text-center text-slate-400">Chưa có dữ liệu lương tháng ${month}/${year}. Hãy bấm "Tính lương tháng này".</td></tr>`;
                    return;
                }
                tbody.innerHTML = data.data.map(p => `
                    <tr class="hover:bg-slate-50 border-b border-slate-100">
                        <td class="p-4 py-3 text-slate-900 font-medium">${p.employeeName}</td>
                        <td class="p-4 py-3 text-slate-700 text-right">${p.baseSalary.toLocaleString('vi-VN')} đ</td>
                        <td class="p-4 py-3 text-center text-slate-700 font-medium">${p.workingDays || 22}</td>
                        <td class="p-4 py-3 text-emerald-600 font-medium text-right">+${p.kpiBonus.toLocaleString('vi-VN')} đ</td>
                        <td class="p-4 py-3 text-red-600 font-medium text-right">-${p.violationDeduction.toLocaleString('vi-VN')} đ</td>
                        <td class="p-4 py-3 text-slate-900 font-bold text-right">${p.netSalary.toLocaleString('vi-VN')} đ</td>
                        <td class="p-4 py-3 text-center"><span class="px-2.5 py-1 text-[10px] font-bold rounded-full ${p.status === 'Đã thanh toán' ? 'bg-green-100 text-green-700' : 'bg-slate-100 text-slate-700'}">${p.status}</span></td>
                        <td class="p-4 py-3 text-center">
                            ${p.status !== 'Đã thanh toán' ? `<button onclick="window.updatePayrollStatus('${p.id}', 'Đã thanh toán')" class="text-xs bg-emerald-500 text-white px-2 py-1 rounded hover:bg-emerald-600 transition shadow-sm">Thanh toán</button>` : ''}
                        </td>
                    </tr>
                `).join('');
            }
        } catch(err) { console.error(err); }
    };

    window.resetEmployeePassword = async (id, displayName) => {
        if (!id) return;
        if (!confirm(`Xác nhận reset mật khẩu cho nhân viên ${displayName} về mặc định (123456)?`)) return;
        try {
            const res = await fetch(`/Manager/ResetPassword?id=${id}`, { method: 'POST' });
            const data = await res.json();
            if (data.success) {
                alert('Reset mật khẩu thành công!');
            } else {
                alert(data.message || 'Có lỗi xảy ra');
            }
        } catch(err) { console.error(err); alert('Lỗi kết nối'); }
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

    let hasPayrollPin = false;

    const renderProfile = () => {
        document.querySelectorAll("[data-profile-input]").forEach(input => {
            const btn = input.nextElementSibling;
            if (btn && btn.tagName === 'BUTTON') {
                btn.innerHTML = '<i class="fa-solid fa-lock text-slate-400"></i>';
                btn.title = hasPayrollPin ? "Yêu cầu mã PIN" : "Chưa cài mã PIN";
                btn.onclick = () => alert("Bạn cần xác thực PIN trên ứng dụng điện thoại để chỉnh sửa.");
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
                document.querySelectorAll("[data-profile-role]").forEach(el => el.textContent = "Quản lý");
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

    const faceScanline = document.querySelector("[data-face-scanline]");

    const updateFaceStepUI = (step) => {
        for (let i = 0; i < maxFaces; i++) {
            const el = document.getElementById(`step-${i}`);
            if (!el) continue;
            if (i === step) {
                el.className = "flex-1 rounded-lg bg-red-50 py-2 text-red-600 border border-red-200 transition-colors duration-300";
            } else if (i < step) {
                el.className = "flex-1 rounded-lg bg-emerald-50 py-2 text-emerald-600 border border-emerald-200 transition-colors duration-300";
                el.innerHTML = "Hoàn thành";
            } else {
                el.className = "flex-1 rounded-lg bg-slate-100 py-2 text-slate-500 transition-colors duration-300";
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
        
        // Show scan animation
        if (faceScanline) {
            faceScanline.classList.remove('hidden');
            faceScanline.style.animation = 'none';
            faceScanline.offsetHeight; // trigger reflow
            faceScanline.style.animation = 'scan 1.5s linear infinite';
        }
        
        const ctx = faceCanvas.getContext("2d");
        faceCanvas.width = faceVideo.videoWidth;
        faceCanvas.height = faceVideo.videoHeight;
        ctx.drawImage(faceVideo, 0, 0, faceCanvas.width, faceCanvas.height);
        
        const currentImageData = faceCanvas.toDataURL("image/jpeg", 0.85);
        
        setTimeout(async () => {
            if (faceScanline) faceScanline.classList.add('hidden');
            faceImages.push(currentImageData);
            
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
        }, 1000);
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


(() => {
    const videoPreview = document.getElementById('cameraSourcePreviewVideo');
    const imagePreview = document.getElementById('cameraSourcePreviewImage');
    const placeholder = document.getElementById('cameraSourcePreviewPlaceholder');
    const modelSelect = document.getElementById('cameraModelSelect');
    const statusText = document.getElementById('cameraStatusText');
    const outputGrid = document.getElementById('cameraOutputGrid');
    const logBox = document.getElementById('cameraLogBox');
    
    const sourceTypeSelect = document.getElementById('cameraSourceTypeSelect');
    const indexWrap = document.getElementById('cameraIndexWrap');
    const indexSelect = document.getElementById('cameraIndexSelect');
    const fileWrap = document.getElementById('cameraInputFileWrap');
    const fileInput = document.getElementById('cameraTestVideoInput');
    const fileBtn = document.getElementById('cameraChooseFileBtn');
    const fileLabel = document.getElementById('cameraSelectedVideoLabel');
    
    const startBtn = document.getElementById('cameraStartLiveBtn');
    const pauseBtn = document.getElementById('cameraPauseBtn');
    const stopBtn = document.getElementById('cameraStopLiveBtn');
    const refreshBtn = document.getElementById('cameraRefreshBtn');
    const realtimeIndicator = document.getElementById('cameraRealtimeIndicator');

    let stream = null;
    let isRunning = false;
    let isPaused = false;
    let isSendingFrame = false;
    let analysisTimerId = null;
    const analysisIntervalMs = 16; // Giảm xuống 16ms (~60 FPS) chuẩn thời gian thực
    let frameCounter = 0;
    
    const captureCanvas = document.createElement('canvas');
    const captureContext = captureCanvas.getContext('2d', { willReadFrequently: true });

    const log = (msg) => {
        if (!logBox) return;
        const time = new Date().toLocaleTimeString();
        logBox.textContent = `[${time}] ${msg}\n` + logBox.textContent;
    };

    const setStatus = (msg) => {
        if (statusText) statusText.innerHTML = msg;
    };

    // UI Toggles
    if (sourceTypeSelect) {
        sourceTypeSelect.addEventListener('change', () => {
            const type = sourceTypeSelect.value;
            if (type === 'webcam') {
                indexWrap.classList.remove('hidden');
                fileWrap.classList.add('hidden');
            } else {
                indexWrap.classList.add('hidden');
                fileWrap.classList.remove('hidden');
            }
        });
    }

    if (fileBtn) fileBtn.addEventListener('click', () => fileInput && fileInput.click());
    if (fileInput) {
        fileInput.addEventListener('change', (e) => {
            const file = e.target.files[0];
            if (!file) return;
            fileLabel.textContent = file.name;
            const url = URL.createObjectURL(file);
            
            placeholder.classList.add('hidden');
            if (file.type.startsWith('image/')) {
                videoPreview.classList.add('hidden');
                imagePreview.classList.remove('hidden');
                imagePreview.src = url;
            } else {
                imagePreview.classList.add('hidden');
                videoPreview.classList.remove('hidden');
                videoPreview.src = url;
            }
            log(`Đã nạp file ${file.name}`);
        });
    }

    const stopMedia = () => {
        if (stream) {
            stream.getTracks().forEach(t => t.stop());
            stream = null;
        }
        if (videoPreview) {
            videoPreview.pause();
            videoPreview.srcObject = null;
        }
    };

    if (refreshBtn) refreshBtn.addEventListener('click', () => {
        stopMedia();
        if (fileLabel) fileLabel.textContent = 'Chưa chọn file đầu vào.';
        if (imagePreview) imagePreview.src = '';
        if (videoPreview) videoPreview.src = '';
        placeholder.classList.remove('hidden');
        imagePreview.classList.add('hidden');
        videoPreview.classList.add('hidden');
        
        ['canvasSmoking', 'canvasLeaving'].forEach(id => {
            const cvs = document.getElementById(id);
            if (cvs) { cvs.classList.add('hidden'); cvs.getContext('2d')?.clearRect(0, 0, cvs.width, cvs.height); }
        });
        ['placeholderSmoking', 'placeholderLeaving'].forEach(id => {
            const p = document.getElementById(id);
            if (p) p.classList.remove('hidden');
        });

        log('Làm mới nguồn.');
    });

    const getCaptureElement = () => {
        return !imagePreview.classList.contains('hidden') ? imagePreview : videoPreview;
    };

    const captureFrameBlob = async () => {
        const element = getCaptureElement();
        if (!element || !captureContext || element.classList.contains('hidden')) return null;

        const isImg = element.tagName === 'IMG';
        const w = isImg ? element.naturalWidth : element.videoWidth;
        const h = isImg ? element.naturalHeight : element.videoHeight;
        
        if (!w || !h) return null;

        const maxDim = 640;
        let tw = w, th = h;
        if (w > maxDim || h > maxDim) {
            const r = Math.min(maxDim / w, maxDim / h);
            tw = Math.round(w * r);
            th = Math.round(h * r);
        }

        captureCanvas.width = tw;
        captureCanvas.height = th;
        captureContext.drawImage(element, 0, 0, tw, th);

        return await new Promise(res => captureCanvas.toBlob(res, 'image/jpeg', 0.82));
    };

    const analyzeCurrentFrame = async () => {
        if (!isRunning || isPaused || isSendingFrame) return;
        isSendingFrame = true;
        
        const abortController = new AbortController();
        const timeoutId = setTimeout(() => abortController.abort(), 25000);
        
        try {
            const blob = await captureFrameBlob();
            if (!blob) {
                isSendingFrame = false;
                return;
            }

            frameCounter++;
            const form = new FormData();
            form.append('frame', blob, `frame_${frameCounter}.jpg`);
            form.append('modelType', modelSelect ? modelSelect.value : 'all');

            const res = await fetch('/Manager/AnalyzeMonitoringFrame', {
                method: 'POST', body: form, signal: abortController.signal
            });

            clearTimeout(timeoutId);
            const payload = await res.json();
            if (!res.ok) throw new Error(payload.message || 'Lỗi HTTP');
            if (!payload.success) throw new Error(payload.message);

            if (payload.data && payload.data.length > 0) {
                payload.data.forEach(run => {
                    if (!run.annotatedImageBase64) return;
                    
                    const src = `data:${run.annotatedImageMimeType || 'image/jpeg'};base64,${run.annotatedImageBase64}`;
                    let targetCanvasId = "canvasSmoking";
                    let targetPlaceholderId = "placeholderSmoking";

                    if (run.modelType && run.modelType.includes("Smoking")) {
                        targetCanvasId = "canvasSmoking";
                        targetPlaceholderId = "placeholderSmoking";
                    } else if (run.modelType && run.modelType.includes("Leaving")) {
                        targetCanvasId = "canvasLeaving";
                        targetPlaceholderId = "placeholderLeaving";
                    }

                    const canvas = document.getElementById(targetCanvasId);
                    const placeholderEl = document.getElementById(targetPlaceholderId);
                    
                    if (canvas) {
                        const img = new Image();
                        img.onload = () => {
                            if (placeholderEl) placeholderEl.classList.add('hidden');
                            canvas.classList.remove('hidden');
                            canvas.width = img.width;
                            canvas.height = img.height;
                            const ctx = canvas.getContext('2d');
                            ctx.clearRect(0, 0, canvas.width, canvas.height);
                            ctx.drawImage(img, 0, 0, img.width, img.height);
                        };
                        img.src = src;
                    }
                });
            }
        } catch (e) {
            log(`Lỗi: ${e.message}`);
        } finally {
            isSendingFrame = false;
        }
    };

    if (startBtn) startBtn.addEventListener('click', async () => {
        if (isRunning) return;
        
        const type = sourceTypeSelect?.value;
        if (type === 'webcam') {
            try {
                stream = await navigator.mediaDevices.getUserMedia({ 
                    video: true 
                });
                placeholder.classList.add('hidden');
                imagePreview.classList.add('hidden');
                videoPreview.classList.remove('hidden');
                videoPreview.srcObject = stream;
                await videoPreview.play();
                log('Đã mở webcam thật.');
            } catch (e) {
                log(`Lỗi webcam: ${e.message}`);
                return;
            }
        } else {
            if (videoPreview && !videoPreview.classList.contains('hidden')) {
                await videoPreview.play().catch(()=>{});
            }
        }

        isRunning = true;
        isPaused = false;
        frameCounter = 0;
        if (realtimeIndicator) { realtimeIndicator.classList.remove('hidden'); realtimeIndicator.classList.add('flex'); }
        setStatus('Đang chạy giám sát realtime...');
        log('Bắt đầu phân tích nguồn.');

        await analyzeCurrentFrame();
        if (analysisTimerId) clearInterval(analysisTimerId);
        analysisTimerId = setInterval(analyzeCurrentFrame, analysisIntervalMs);
    });

    if (pauseBtn) pauseBtn.addEventListener('click', () => {
        if (!isRunning) return;
        isPaused = !isPaused;
        if (videoPreview && !videoPreview.classList.contains('hidden')) {
            if (isPaused) videoPreview.pause();
            else videoPreview.play().catch(()=>{});
        }
        setStatus(isPaused ? 'Đã tạm dừng' : 'Đang chạy giám sát realtime...');
        log(isPaused ? 'Tạm dừng.' : 'Tiếp tục.');
    });

    if (stopBtn) stopBtn.addEventListener('click', () => {
        isRunning = false;
        isPaused = false;
        if (analysisTimerId) clearInterval(analysisTimerId);
        if (realtimeIndicator) { realtimeIndicator.classList.add('hidden'); realtimeIndicator.classList.remove('flex'); }
        
        if (sourceTypeSelect?.value === 'webcam') {
            stopMedia();
            placeholder.classList.remove('hidden');
            videoPreview.classList.add('hidden');
        } else {
            if (videoPreview) videoPreview.pause();
        }
        
        setStatus('Đã dừng phân tích.');
        log('Dừng giám sát.');
    });

    document.addEventListener("DOMContentLoaded", () => {
        if (window.connection) {
            window.connection.on("MessagesChanged", () => {
                const messagesTab = document.querySelector('[data-tab-panel="messages"]');
                if (messagesTab && !messagesTab.classList.contains("hidden")) {
                    loadManagerConversation();
                    loadChatContacts();
                } else {
                    if (typeof loadChatContacts === "function") loadChatContacts();
                }
            });
        } else {
            setTimeout(() => {
                if (window.connection) {
                    window.connection.on("MessagesChanged", () => {
                        const messagesTab = document.querySelector('[data-tab-panel="messages"]');
                        if (messagesTab && !messagesTab.classList.contains("hidden")) {
                            loadManagerConversation();
                            loadChatContacts();
                        } else {
                            if (typeof loadChatContacts === "function") loadChatContacts();
                        }
                    });
                }
            }, 2000);
        }
    });
})();
