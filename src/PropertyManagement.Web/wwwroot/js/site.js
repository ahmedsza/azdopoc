(() => {
    window.lucide?.createIcons();
    const themeButton = document.getElementById("theme-toggle");
    try {
        const saved = sessionStorage.getItem("yardTheme");
        if (saved && !new URLSearchParams(location.search).has("scoutTheme")) {
            document.documentElement.setAttribute("data-theme", saved);
        }
    } catch { }
    themeButton?.addEventListener("click", () => {
        const theme = document.documentElement.getAttribute("data-theme") === "dark" ? "light" : "dark";
        document.documentElement.setAttribute("data-theme", theme);
        themeButton.setAttribute("aria-pressed", String(theme === "dark"));
        try { sessionStorage.setItem("yardTheme", theme); } catch { }
    });
    document.querySelectorAll('form[method="post"]').forEach(form => {
        form.addEventListener("submit", event => {
            if (form.dataset.confirm && !window.confirm(form.dataset.confirm)) {
                event.preventDefault();
                return;
            }
            form.querySelectorAll('button:not([type="button"])').forEach(button => {
                button.disabled = true;
                button.setAttribute("aria-busy", "true");
            });
        });
    });
    window.addEventListener("pageshow", () => {
        document.querySelectorAll('button[aria-busy="true"]').forEach(button => {
            button.disabled = false;
            button.removeAttribute("aria-busy");
        });
    });
})();