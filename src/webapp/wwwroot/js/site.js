// PicPick: перемикач теми і блискітки на фоні.

(function () {
    const THEME_KEY = "picpick-theme";
    const themes = {
        sunset: { next: "pink", label: "Рожева тема", ariaLabel: "Увімкнути рожеву тему" },
        pink: { next: "sunset", label: "Тема «Захід»", ariaLabel: "Увімкнути тему «Захід»" }
    };

    function currentTheme() {
        return document.documentElement.dataset.theme === "pink" ? "pink" : "sunset";
    }

    function applyTheme(theme) {
        if (theme === "pink") {
            document.documentElement.dataset.theme = "pink";
        } else {
            delete document.documentElement.dataset.theme;
        }
        updateToggle();
    }

    function updateToggle() {
        const toggle = document.getElementById("theme-toggle");
        if (!toggle) return;
        const info = themes[currentTheme()];
        toggle.querySelector(".theme-label").textContent = info.label;
        toggle.setAttribute("aria-label", info.ariaLabel);
    }

    function initThemeToggle() {
        const toggle = document.getElementById("theme-toggle");
        if (!toggle) return;
        updateToggle();
        toggle.addEventListener("click", () => {
            const next = themes[currentTheme()].next;
            applyTheme(next);
            try {
                localStorage.setItem(THEME_KEY, next);
            } catch (e) {
                // Без localStorage тема просто не запам'ятається між сторінками.
            }
        });
    }

    // Додає в секцію з data-sparkles="N" шар із N блискітками у випадкових місцях.
    function initSparkles() {
        document.querySelectorAll("[data-sparkles]").forEach((section) => {
            const count = parseInt(section.dataset.sparkles, 10) || 0;
            const layer = document.createElement("div");
            layer.className = "sparkles" + (section.dataset.sparklesSoft !== undefined ? " sparkles-soft" : "");
            layer.setAttribute("aria-hidden", "true");
            for (let i = 0; i < count; i++) {
                const sparkle = document.createElement("span");
                sparkle.className = "sparkle";
                sparkle.style.left = (Math.random() * 100).toFixed(1) + "%";
                sparkle.style.top = (Math.random() * 100).toFixed(1) + "%";
                sparkle.style.setProperty("--size", (8 + Math.random() * 16).toFixed(0) + "px");
                sparkle.style.setProperty("--duration", (2 + Math.random() * 3).toFixed(1) + "s");
                sparkle.style.setProperty("--delay", (Math.random() * 4).toFixed(1) + "s");
                layer.appendChild(sparkle);
            }
            section.prepend(layer);
        });
    }

    document.addEventListener("DOMContentLoaded", () => {
        initThemeToggle();
        initSparkles();
    });
})();
