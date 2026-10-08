(() => {
    const form = document.getElementById("loginForm");
    if (!form) return;

    const message = document.getElementById("authMessage");
    const submitButton = form.querySelector("button[type='submit']");
    const antiforgeryToken = () => form.querySelector("input[name='__RequestVerificationToken']")?.value ?? "";
    let accessToken = null;
    let refreshTimer = null;
    window.adminSession = {
        getAccessToken: () => accessToken
    };

    function showMessage(text, isError = false) {
        message.textContent = text;
        message.className = isError ? "alert alert-danger" : "alert alert-success";
    }

    function scheduleRefresh(expiresAtUtc) {
        window.clearTimeout(refreshTimer);
        const delay = Math.max(0, Date.parse(expiresAtUtc) - Date.now() + 1000);
        refreshTimer = window.setTimeout(() => refreshAccessToken(false), delay);
    }

    async function refreshAccessToken(silent) {
        try {
            const response = await fetch("/Account/Refresh", {
                method: "POST",
                credentials: "same-origin",
                headers: { "RequestVerificationToken": antiforgeryToken() }
            });
            const result = await response.json();
            if (!response.ok) {
                accessToken = null;
                window.clearTimeout(refreshTimer);
                if (!silent) showMessage(result.message || "Phiên đăng nhập hết hạn. Vui lòng đăng nhập lại.", true);
                return false;
            }

            accessToken = result.accessToken;
            scheduleRefresh(result.accessTokenExpiresAtUtc);
            if (!silent) showMessage("Phiên đăng nhập đã được gia hạn.");
            return true;
        } catch {
            if (!silent) showMessage("Không thể làm mới phiên đăng nhập. Vui lòng thử lại.", true);
            return false;
        }
    }

    form.addEventListener("submit", async event => {
        event.preventDefault();
        if (!$(form).valid()) return;

        submitButton.disabled = true;
        message.className = "d-none";
        try {
            const response = await fetch(form.action, {
                method: "POST",
                credentials: "same-origin",
                headers: { "X-Requested-With": "XMLHttpRequest" },
                body: new FormData(form)
            });
            const result = await response.json();
            if (!response.ok) {
                showMessage(result.message || "Không thể đăng nhập.", true);
                return;
            }

            accessToken = result.accessToken;
            scheduleRefresh(result.accessTokenExpiresAtUtc);
            if (result.redirectUrl) {
                window.location.assign(result.redirectUrl);
                return;
            }
            window.location.assign(form.dataset.dashboardUrl || "/Home/Index");
        } catch {
            showMessage("Không thể kết nối máy chủ. Vui lòng thử lại.", true);
        } finally {
            submitButton.disabled = false;
        }
    });
})();
