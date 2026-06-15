// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

window.buildDocHtml = (tieuDe, kinhGui, bodyLines, name, department, date, reason) => {
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

window.buildDoc = {
    "Nghỉ phép": (name, department, date, reason) => window.buildDocHtml(
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
    "Đi muộn": (name, department, date, reason) => window.buildDocHtml(
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
    "Tăng ca": (name, department, date, reason) => window.buildDocHtml(
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
    "Điều chỉnh ca": (name, department, date, reason) => window.buildDocHtml(
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

// Notification Bell Logic for Manager, Admin, and Employee
window.loadNotifications = async function() {
    try {
        let res = await fetch("/Employee/GetNotifications");
        if (res.redirected || !res.ok || res.status === 404 || res.status === 403) {
            res = await fetch("/Manager/GetNotifications");
        }
        if (!res.ok || res.redirected) return;
        
        let json;
        try {
            json = await res.json();
        } catch(e) { return; }
        
        if (!json.success) return;

        // Cập nhật số lượng chưa đọc (badge)
        const unreadCount = json.unreadCount || 0;
        const badges = document.querySelectorAll('[data-notification-unread-badge]');
        badges.forEach(b => {
            if (unreadCount > 0) {
                b.classList.remove('hidden');
            } else {
                b.classList.add('hidden');
            }
        });

        // Cập nhật danh sách thả xuống
        const listContainers = document.querySelectorAll('[data-notification-list], #layoutNotificationList, #notificationListContainer');
        listContainers.forEach(container => {
            container.innerHTML = '';

            if (!json.data || json.data.length === 0) {
                container.innerHTML = '<div class="px-4 py-3 text-sm text-slate-500 text-center">Không có thông báo mới</div>';
                return;
            }

            json.data.forEach(item => {
                const itemDiv = document.createElement('div');
                itemDiv.className = `cursor-pointer border-b border-slate-50 px-4 py-3 transition hover:bg-slate-50 last:border-0 ${item.isRead ? 'opacity-70' : 'bg-red-50/30'}`;
                
                const timeStr = new Date(item.createdAt).toLocaleString('vi-VN');
                itemDiv.innerHTML = `
                    <p class="text-sm font-bold text-slate-800">${item.title}</p>
                    <p class="mt-1 text-xs text-slate-500 line-clamp-1">${item.body}</p>
                    <p class="mt-1 text-[10px] text-slate-400">${timeStr}</p>
                `;

                itemDiv.onclick = (e) => {
                    e.preventDefault();
                    
                    // Call API mark as read
                    const markUrl = window.location.pathname.toLowerCase().includes('/manager') || window.location.pathname.toLowerCase().includes('/admin')
                        ? '/Manager/MarkNotificationRead?id=' + item.id
                        : '/Employee/MarkNotificationRead?id=' + item.id;
                    fetch(markUrl, { method: 'POST' }).catch(() => {});
                    
                    itemDiv.classList.remove('bg-red-50/30');
                    itemDiv.classList.add('opacity-70');
                    
                    // Giảm số đếm
                    badges.forEach(b => b.classList.add('hidden'));

                    // Ẩn dropdown
                    const dropdowns = document.querySelectorAll('#dashboardNotificationMenu, #layoutNotificationMenu');
                    dropdowns.forEach(d => d.classList.add('hidden'));

                    // Điều hướng thông minh
                    const type = item.tab || '';
                    const bodyLower = (item.body || '').toLowerCase();
                    const titleLower = (item.title || '').toLowerCase();
                    
                    let targetTab = type;
                    if (!targetTab) {
                        if (titleLower.includes('tin nhắn')) targetTab = 'messages';
                        else if (titleLower.includes('đơn') || titleLower.includes('duyệt') || titleLower.includes('từ chối')) targetTab = 'requests';
                        else if (titleLower.includes('công việc')) targetTab = 'tasks';
                        else if (titleLower.includes('vi phạm') || titleLower.includes('yolo')) targetTab = 'violations';
                    }

                    if (targetTab) {
                        const btn = document.querySelector(`[data-tab-trigger="${targetTab}"]`);
                        if (btn) {
                            btn.click();
                        } else if (typeof window.setActiveTab === 'function') {
                            window.setActiveTab(targetTab);
                        } else {
                            // Cố gắng điều hướng qua URL nếu không ở dashboard
                            const currentUrl = new URL(window.location.href);
                            currentUrl.searchParams.set('tab', targetTab);
                            window.location.href = currentUrl.toString();
                        }
                    }
                };

                container.appendChild(itemDiv);
            });
        });
    } catch (err) {
        console.error("Error loading notifications:", err);
    }
};

document.addEventListener('DOMContentLoaded', () => {
    if (typeof window.loadNotifications === 'function') {
        window.loadNotifications();
    }
    // Toggle Admin Layout dropdown
    const trigger = document.getElementById('layoutNotificationTrigger');
    const menu = document.getElementById('layoutNotificationMenu');
    
    if (trigger && menu) {
        trigger.addEventListener('click', (e) => {
            e.stopPropagation();
            menu.classList.toggle('hidden');
            if(!menu.classList.contains('hidden')) {
                window.loadNotifications();
            }
        });
        document.addEventListener('click', (e) => {
            if (!trigger.contains(e.target) && !menu.contains(e.target)) {
                menu.classList.add('hidden');
            }
        });
    }
});
