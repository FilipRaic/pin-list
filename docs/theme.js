// The light / dark switch at the top right of every page. Dark is the default, a choice is remembered in the browser.
// Loaded in <head> without defer, so a saved light theme is set before the page is drawn (no dark flash).
(function () {
    "use strict";

    const root = document.documentElement;

    function read() {
        try {
            return localStorage.getItem("theme");
        } catch {
            return null; // storage blocked: always dark
        }
    }

    function save(theme) {
        try {
            localStorage.setItem("theme", theme);
        } catch {
            // storage blocked: the choice lasts until the page is left
        }
    }

    function apply(theme) {
        if (theme === "light") root.dataset.theme = "light";
        else delete root.dataset.theme;
        const button = document.getElementById("themeToggle");
        if (!button) return;
        const light = theme === "light";
        button.setAttribute("aria-label", light ? "Switch to dark mode" : "Switch to light mode");
        button.title = light ? "Dark mode" : "Light mode";
    }

    apply(read());

    document.addEventListener("DOMContentLoaded", () => {
        apply(read());
        document.getElementById("themeToggle").addEventListener("click", () => {
            const next = root.dataset.theme === "light" ? "dark" : "light";
            save(next);
            apply(next);
        });
    });
})();
