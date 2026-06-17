document.addEventListener("DOMContentLoaded", function () {
    if (typeof signalR === 'undefined') {
        console.warn("SignalR not loaded for notifications.");
        return;
    }

    const connection = new signalR.HubConnectionBuilder()
        .withUrl("/hubs/internal-chat")
        .withAutomaticReconnect()
        .build();

    window.connection = connection;

    connection.on("ReceiveNotification", function (message) {
        updateBadges();
        reloadChatPanels();
    });

    connection.on("MessagesChanged", function (payload) {
        if (payload && payload.channel === "violations" && typeof window.loadMyViolations === "function") {
            window.loadMyViolations();
            updateBadges();
            return;
        }

        updateBadges();
        reloadChatPanels(payload);
    });

    connection.start().catch(function (err) {
        console.error("SignalR Connection Error: ", err.toString());
    });

    function updateBadges() {
        const badges = document.querySelectorAll('[data-notification-unread-badge]');
        badges.forEach(b => {
            b.classList.remove('hidden');
        });
        
        // If employee dropdown
        if (typeof window.loadNotifications === 'function') {
            window.loadNotifications();
        }
    }

    function reloadChatPanels(payload) {
        const messagesPanel = document.querySelector('[data-tab-panel="messages"]');
        const isMessagesOpen = messagesPanel && !messagesPanel.classList.contains("hidden");

        if (typeof window.loadManagerChatContacts === "function") {
            window.loadManagerChatContacts();
            if (isMessagesOpen && typeof window.loadManagerConversation === "function") {
                window.loadManagerConversation(payload?.employeeUserId);
            }
        }

        if (typeof window.loadEmployeeMessages === "function") {
            window.loadEmployeeMessages();
        }
    }


    // Inject floating bell for Manager/Admin if they don't have dashboardNotificationDropdown
    if (!document.getElementById('dashboardNotificationDropdown')) {
        const floatingBell = document.createElement('div');
        floatingBell.className = 'fixed bottom-20 left-6 z-[9998]'; // Above logout button
        floatingBell.innerHTML = `
            <button type="button" class="relative bg-white text-slate-500 hover:text-red-600 border border-slate-200 shadow-lg rounded-full w-12 h-12 flex items-center justify-center transition-all duration-200 hover:-translate-y-0.5 focus:outline-none">
                <i class="fa-regular fa-bell text-xl"></i>
                <span data-notification-unread-badge class="absolute top-0 right-0 hidden h-3 w-3">
                    <span class="animate-ping absolute inline-flex h-full w-full rounded-full bg-red-400 opacity-75"></span>
                    <span class="relative inline-flex h-3 w-3 rounded-full bg-red-500"></span>
                </span>
            </button>
        `;
        floatingBell.onclick = () => {
            floatingBell.querySelector('[data-notification-unread-badge]').classList.add('hidden');
            // Check role to redirect
            const isManager = document.querySelector('[data-ai-assistant-role="Manager"]') != null;
            if (isManager) {
                window.location.href = '/Manager/Messages';
            } else {
                // Admin has Monitoring or Dashboard
                window.location.href = '/Admin/Monitoring';
            }
        };
        document.body.appendChild(floatingBell);
    }
});

window.navigateToTabFromMessage = function(message) {
    if (!message) return;
    const msgLower = message.toLowerCase();
    
    let targetTab = null;
    let url = "";

    const isManager = document.querySelector('[data-ai-assistant-role="Manager"]');
    const isAdmin = document.querySelector('[data-ai-assistant-role="Admin"]');

    if (isManager) {
        url = "/Manager/Dashboard";
        if (msgLower.includes("đơn") || msgLower.includes("duyệt")) targetTab = "requests";
        else if (msgLower.includes("tin nhắn")) targetTab = "messages";
        else if (msgLower.includes("lương")) targetTab = "payroll";
        else targetTab = "overview";
    } else if (isAdmin) {
        url = "/Admin/Monitoring";
        targetTab = "overview";
    } else {
        url = "/Employee/Dashboard";
        if (msgLower.includes("đơn") || msgLower.includes("duyệt")) targetTab = "requests";
        else if (msgLower.includes("tin nhắn")) targetTab = "messages";
        else if (msgLower.includes("công việc")) targetTab = "tasks";
        else if (msgLower.includes("vi phạm") || msgLower.includes("kỷ luật")) targetTab = "violations";
        else targetTab = "overview";
    }

    if (window.location.pathname.toLowerCase().includes("dashboard")) {
        if (typeof window.setActiveTab === 'function' && targetTab) {
            window.setActiveTab(targetTab);
            const dropdown = document.getElementById('dashboardNotificationDropdown');
            if (dropdown) dropdown.classList.add('hidden');
        }
    } else {
        window.location.href = url + (targetTab ? "?tab=" + targetTab : "");
    }
};
