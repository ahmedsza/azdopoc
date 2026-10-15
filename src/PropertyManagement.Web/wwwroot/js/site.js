(() => {
    window.lucide?.createIcons();
    const themeButton = document.getElementById("theme-toggle");
    const syncThemeToggle = () => {
        themeButton?.setAttribute("aria-checked", String(document.documentElement.getAttribute("data-theme") === "dark"));
    };
    syncThemeToggle();
    themeButton?.addEventListener("click", () => {
        const theme = document.documentElement.getAttribute("data-theme") === "dark" ? "light" : "dark";
        document.documentElement.setAttribute("data-theme", theme);
        syncThemeToggle();
        try {
            localStorage.setItem("yardTheme", theme);
        } catch (error) {
            console.warn("Unable to save the theme preference. The theme will only apply to this page.", error);
        }
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