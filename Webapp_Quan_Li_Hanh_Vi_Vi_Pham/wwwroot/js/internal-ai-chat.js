(function () {
    const root = document.body;
    if (!root || root.dataset.aiAssistantMounted === "true") {
        return;
    }

    const role = root.dataset.aiAssistantRole || "";
    const endpoint = root.dataset.aiAssistantEndpoint || "/api/ai-assistant/chat";
    const title = role === "Manager" ? "Trợ lý AI quản lý" : "Trợ lý AI nội bộ";
    const greeting =
        role === "Manager"
            ? "Tôi có thể hỗ trợ thông tin vi phạm hệ thống và thông tin tài khoản quản lý hiện tại."
            : "Tôi có thể hỗ trợ thông tin vi phạm và thông tin tài khoản nội bộ của bạn.";

    root.dataset.aiAssistantMounted = "true";

    const widget = document.createElement("div");
    widget.innerHTML = `
        <div id="aiAssistantWidget" style="position:fixed;right:24px;bottom:24px;z-index:9999;font-family:Inter,sans-serif;">
            <div id="aiAssistantPanel" style="display:none;width:min(380px,calc(100vw - 32px));height:560px;max-height:calc(100vh - 120px);background:#fff;border:1px solid #e2e8f0;border-radius:22px;box-shadow:0 20px 50px rgba(15,23,42,.18);overflow:hidden;flex-direction:column;">
                <div style="display:flex;align-items:center;justify-content:space-between;padding:16px 18px;background:linear-gradient(135deg,#dc2626,#991b1b);color:#fff;">
                    <div>
                        <div style="font-size:12px;letter-spacing:.08em;text-transform:uppercase;opacity:.8;">Internal AI</div>
                        <div style="font-size:16px;font-weight:700;">${title}</div>
                    </div>
                    <button type="button" id="aiAssistantClose" style="border:none;background:rgba(255,255,255,.12);color:#fff;width:34px;height:34px;border-radius:999px;cursor:pointer;">
                        <i class="fa-solid fa-xmark"></i>
                    </button>
                </div>
                <div id="aiAssistantMessages" style="flex:1;overflow-y:auto;padding:16px;background:#f8fafc;"></div>
                <form id="aiAssistantForm" style="padding:14px;border-top:1px solid #e2e8f0;background:#fff;">
                    <div style="display:flex;gap:10px;align-items:flex-end;">
                        <textarea id="aiAssistantInput" rows="3" placeholder="Hỏi về vi phạm hoặc thông tin nội bộ tài khoản..." style="flex:1;resize:none;border:1px solid #cbd5e1;border-radius:16px;padding:12px 14px;font-size:14px;outline:none;"></textarea>
                        <button type="submit" id="aiAssistantSend" style="border:none;background:#dc2626;color:#fff;width:46px;height:46px;border-radius:14px;cursor:pointer;box-shadow:0 12px 24px rgba(220,38,38,.25);">
                            <i class="fa-solid fa-paper-plane"></i>
                        </button>
                    </div>
                    <div style="margin-top:8px;font-size:12px;color:#64748b;">Chỉ hỗ trợ thông tin vi phạm và thông tin nội bộ tài khoản trong hệ thống.</div>
                </form>
            </div>
            <button type="button" id="aiAssistantToggle" style="margin-left:auto;margin-top:14px;display:flex;align-items:center;justify-content:center;width:64px;height:64px;border:none;border-radius:999px;background:linear-gradient(135deg,#dc2626,#7f1d1d);color:#fff;box-shadow:0 18px 36px rgba(220,38,38,.28);cursor:pointer;">
                <i class="fa-solid fa-robot" style="font-size:24px;"></i>
            </button>
        </div>
    `;
    document.body.appendChild(widget);

    const panel = document.getElementById("aiAssistantPanel");
    const toggle = document.getElementById("aiAssistantToggle");
    const closeBtn = document.getElementById("aiAssistantClose");
    const form = document.getElementById("aiAssistantForm");
    const input = document.getElementById("aiAssistantInput");
    const sendBtn = document.getElementById("aiAssistantSend");
    const messages = document.getElementById("aiAssistantMessages");

    const history = [];

    function appendMessage(roleName, text) {
        history.push({ role: roleName, content: text });
        const wrap = document.createElement("div");
        wrap.style.marginBottom = "12px";
        wrap.style.display = "flex";
        wrap.style.justifyContent = roleName === "assistant" ? "flex-start" : "flex-end";

        const bubble = document.createElement("div");
        bubble.textContent = text;
        bubble.style.maxWidth = "85%";
        bubble.style.whiteSpace = "pre-wrap";
        bubble.style.fontSize = "14px";
        bubble.style.lineHeight = "1.5";
        bubble.style.padding = "12px 14px";
        bubble.style.borderRadius = roleName === "assistant" ? "16px 16px 16px 6px" : "16px 16px 6px 16px";
        bubble.style.background = roleName === "assistant" ? "#ffffff" : "#dc2626";
        bubble.style.color = roleName === "assistant" ? "#0f172a" : "#ffffff";
        bubble.style.border = roleName === "assistant" ? "1px solid #e2e8f0" : "none";
        bubble.style.boxShadow = roleName === "assistant" ? "0 8px 20px rgba(15,23,42,.05)" : "0 10px 22px rgba(220,38,38,.2)";

        wrap.appendChild(bubble);
        messages.appendChild(wrap);
        messages.scrollTop = messages.scrollHeight;
    }

    function setLoading(isLoading) {
        sendBtn.disabled = isLoading;
        input.disabled = isLoading;
        sendBtn.innerHTML = isLoading
            ? '<i class="fa-solid fa-spinner fa-spin"></i>'
            : '<i class="fa-solid fa-paper-plane"></i>';
    }

    async function sendMessage() {
        const content = input.value.trim();
        if (!content) {
            return;
        }

        appendMessage("user", content);
        input.value = "";
        setLoading(true);

        try {
            const response = await fetch(endpoint, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json"
                },
                credentials: "same-origin",
                body: JSON.stringify({
                    message: content,
                    history: history.slice(0, -1).slice(-8)
                })
            });

            const data = await response.json();
            appendMessage("assistant", data.message || "Không nhận được phản hồi phù hợp.");
        } catch (error) {
            appendMessage("assistant", "Không thể kết nối tới trợ lý AI nội bộ.");
        } finally {
            setLoading(false);
            input.focus();
        }
    }

    toggle.addEventListener("click", function () {
        const isOpen = panel.style.display === "flex";
        panel.style.display = isOpen ? "none" : "flex";
        if (!isOpen && messages.childElementCount === 0) {
            appendMessage("assistant", greeting);
        }
        if (!isOpen) {
            input.focus();
        }
    });

    closeBtn.addEventListener("click", function () {
        panel.style.display = "none";
    });

    form.addEventListener("submit", function (event) {
        event.preventDefault();
        sendMessage();
    });

    input.addEventListener("keydown", function (event) {
        if (event.key === "Enter" && !event.shiftKey) {
            event.preventDefault();
            sendMessage();
        }
    });
})();
