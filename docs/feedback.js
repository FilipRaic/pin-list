// The feedback form: sends by email (through FormSubmit) or opens a pre-filled GitHub issue.
// PinList's Settings opens this page as feedback?v=2.8.0&os=Android, which fills in the Device field.
(function () {
    "use strict";

    // The site has no server, so FormSubmit (formsubmit.co) relays the form to email.
    // The address is FormSubmit's alias of the maintainer's email, so the email isn't public.
    const ENDPOINT = "https://formsubmit.co/ajax/c1b44d8c0e8e8c7d5eb802ea9cf41123";
    const REPO = "https://github.com/FilipRaic/pin-list";

    const KINDS = {
        bug: {prefix: "Bug", label: "bug"},
        idea: {prefix: "Idea", label: "enhancement"},
        question: {prefix: "Question", label: "question"},
        other: {prefix: "Feedback", label: ""},
    };

    // what the form says and does for each "Send via" choice
    const VIA = {
        email: {note: "Sent straight to the developer's inbox. No account needed.", button: "Send"},
        github: {note: "Opens a pre-filled public issue on GitHub. You need a GitHub account.", button: "Continue on GitHub"},
    };

    const form = document.getElementById("feedbackForm");
    const status = document.getElementById("status");
    const send = document.getElementById("send");
    const thanks = document.getElementById("thanks");
    const f = form.elements;
    const via = () => f.via.value;

    function setStatus(text, error) {
        status.textContent = text;
        status.classList.toggle("error", !!error);
    }

    function showVia() {
        const v = VIA[via()];
        setStatus(v.note);
        send.textContent = v.button;
        document.getElementById("emailField").hidden = via() !== "email";
    }

    // "PinList 2.8.0 on Android" from the address the app opened
    function deviceFromAddress() {
        const q = new URLSearchParams(location.search);
        const version = (q.get("v") || "").slice(0, 20);
        const os = (q.get("os") || "").slice(0, 120);
        if (!version && !os) return "";
        return `PinList ${version || "(version unknown)"}${os ? ` on ${os}` : ""}`;
    }

    // title, body and issue label built from the form
    function compose() {
        const k = KINDS[f.kind.value] || KINDS.other;
        const lines = [f.body.value.trim() || "(No details given)"];
        const device = f.device.value.trim();
        if (device) lines.push("", "---", `Device: ${device}`);
        return {title: `${k.prefix}: ${f.title.value.trim()}`, body: lines.join("\n"), label: k.label};
    }

    function openIssue(m) {
        const q = new URLSearchParams({title: m.title, body: m.body});
        if (m.label) q.set("labels", m.label);
        window.open(`${REPO}/issues/new?${q}`, "_blank", "noopener");
    }

    async function sendEmail(m) {
        const res = await fetch(ENDPOINT, {
            method: "POST",
            headers: {"Content-Type": "application/json", Accept: "application/json"},
            body: JSON.stringify({
                _subject: `PinList: ${m.title}`,
                _template: "box",
                _honey: f._honey.value,
                email: f.email.value.trim() || undefined, // FormSubmit uses it as Reply-To
                type: f.kind.value,
                summary: f.title.value.trim(),
                message: m.body,
            }),
        });
        const data = await res.json().catch(() => ({}));
        if (!res.ok || String(data.success) !== "true") throw new Error(data.message || res.statusText);
    }

    function done(text) {
        form.hidden = true;
        document.getElementById("thanksText").textContent = text;
        thanks.hidden = false;
        thanks.focus();
    }

    f.device.value = deviceFromAddress();
    document.getElementById("again").href = `feedback${location.search}`; // keeps the device filled in
    f.via.forEach((r) => r.addEventListener("change", showVia));
    showVia();

    form.addEventListener("submit", async (e) => {
        e.preventDefault();
        if (!f.title.value.trim()) {
            setStatus("Please write a one line summary.", true);
            f.title.focus();
            return;
        }
        if (via() === "email" && f.email.value.trim() && !f.email.checkValidity()) {
            setStatus("That email address doesn't look right. Fix it or leave it empty.", true);
            f.email.focus();
            return;
        }
        const m = compose();
        if (via() === "github") {
            openIssue(m);
            done("Finish the issue on GitHub, in the tab that just opened.");
            return;
        }
        send.disabled = true;
        setStatus("Sending");
        try {
            await sendEmail(m);
            done("Your feedback was sent.");
        } catch (err) {
            send.disabled = false;
            setStatus(`Couldn't send it (${err.message || "network error"}). ` +
                "Please try again, or choose GitHub issue above.", true);
        }
    });
})();
