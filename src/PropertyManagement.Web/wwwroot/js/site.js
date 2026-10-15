(() => {
    window.lucide?.createIcons();
    const themeButton = document.getElementById("theme-toggle");
    themeButton?.addEventListener("click", () => {
        const enabled = themeButton.getAttribute("aria-checked") !== "true";
        themeButton.setAttribute("aria-checked", String(enabled));
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