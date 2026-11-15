/* =========================================================================
   NesLifter GUI front end.

   Talks to the C# host through window.chrome.webview.postMessage and receives
   replies on the "neslifter" message channel. Every host call is optional: when
   the page is opened in a plain browser for design work, the UI degrades to
   static markup instead of throwing.
   ========================================================================= */

(function () {
    "use strict";

    var host = window.chrome && window.chrome.webview ? window.chrome.webview : null;

    var state = {
        roms: [],
        selected: new Set(),
        busy: false,
        theme: "dark",
        sound: false
    };

    // ---------------------------------------------------------------- dom

    var el = {
        path: document.getElementById("pathInput"),
        browse: document.getElementById("browseBtn"),
        scan: document.getElementById("scanBtn"),
        recursive: document.getElementById("recursive"),
        list: document.getElementById("romList"),
        selectAll: document.getElementById("selectAllBtn"),
        selectNone: document.getElementById("selectNoneBtn"),
        selectionInfo: document.getElementById("selectionInfo"),

        packageMode: document.getElementById("packageMode"),
        iconSeconds: document.getElementById("iconSeconds"),
        iconPath: document.getElementById("iconPath"),
        iconPick: document.getElementById("iconPickBtn"),
        outputPath: document.getElementById("outputPath"),
        saveSource: document.getElementById("saveSource"),
        lift: document.getElementById("liftBtn"),
        selfTest: document.getElementById("selfTestBtn"),

        log: document.getElementById("log"),
        clearLog: document.getElementById("clearLogBtn"),
        openOutput: document.getElementById("openOutputBtn"),
        status: document.getElementById("statusText"),
        progressTrack: document.getElementById("progressTrack"),
        progressFill: document.getElementById("progressFill"),
        progressText: document.getElementById("progressText"),

        themeToggle: document.getElementById("themeToggle"),
        themeLabel: document.getElementById("themeLabel"),
        soundToggle: document.getElementById("soundToggle")
    };

    // ------------------------------------------------------------- helpers

    function post(type, payload) {
        if (!host) return;

        var message = Object.assign({ type: type }, payload || {});
        host.postMessage(JSON.stringify(message));
    }

    function setStatus(text) {
        el.status.textContent = text;
    }

    function setBusy(busy) {
        state.busy = busy;
        document.body.classList.toggle("busy", busy);

        if (busy) {
            el.progressTrack.hidden = false;
        }
    }

    function setProgress(done, total) {
        var ratio = total > 0 ? done / total : 0;
        el.progressFill.style.width = Math.round(ratio * 100) + "%";
        el.progressText.textContent = total > 0 ? done + " / " + total : "";
    }

    function clearProgress() {
        el.progressTrack.hidden = true;
        el.progressFill.style.width = "0";
        el.progressText.textContent = "";
    }

    function appendLog(text, kind) {
        if (!text) return;

        var atBottom = el.log.scrollTop + el.log.clientHeight >= el.log.scrollHeight - 24;

        var span = document.createElement("span");
        span.className = "line-" + (kind || "dim");
        span.textContent = text + "\n";
        el.log.appendChild(span);

        // Keep the DOM from growing without bound during long batches.
        while (el.log.childNodes.length > 4000) {
            el.log.removeChild(el.log.firstChild);
        }

        if (atBottom) el.log.scrollTop = el.log.scrollHeight;
    }

    function classifyLine(line) {
        if (/error|failed|trap/i.test(line)) return "error";
        if (/packaged|ok=1|summary: ok=/i.test(line)) return "ok";
        if (/^===|^---/.test(line)) return "head";
        return "dim";
    }

    function escapeHtml(text) {
        return String(text)
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;");
    }

    // ------------------------------------------------------------- rom list

    function renderRoms() {
        if (state.roms.length === 0) {
            el.list.innerHTML = '<p class="empty">No cartridges yet. Pick a path and press Scan.</p>';
            updateSelectionInfo();
            return;
        }

        var html = "";

        for (var i = 0; i < state.roms.length; i++) {
            var rom = state.roms[i];
            var selected = state.selected.has(i);
            var supported = rom.mapperSupported;

            html += '<div class="rom-row' +
                (selected ? " selected" : "") +
                (supported ? "" : " unsupported") +
                '" data-index="' + i + '">' +
                '<span class="rom-check"><i class="fas ' + (selected ? "fa-square-check" : "fa-square") + '"></i></span>' +
                '<span class="rom-name" title="' + escapeHtml(rom.fileName) + '">' + escapeHtml(rom.fileName) + "</span>" +
                '<span class="rom-mapper">' + (rom.mapper < 0 ? "?" : rom.mapper) + "</span>" +
                '<span class="rom-board">' + escapeHtml(rom.mapperName) + "</span>" +
                '<span class="rom-size">' + escapeHtml(rom.sizeLabel) + "</span>" +
                "</div>";
        }

        el.list.innerHTML = html;
        updateSelectionInfo();
    }

    function updateSelectionInfo() {
        var count = state.selected.size;
        el.selectionInfo.textContent = count + " selected";
    }

    function toggleRow(index) {
        if (state.selected.has(index)) state.selected.delete(index);
        else state.selected.add(index);

        var row = el.list.querySelector('[data-index="' + index + '"]');

        if (row) {
            row.classList.toggle("selected", state.selected.has(index));
            var icon = row.querySelector(".rom-check i");
            icon.className = "fas " + (state.selected.has(index) ? "fa-square-check" : "fa-square");
        }

        updateSelectionInfo();

        if (window.NesSfx) window.NesSfx.tick();
    }

    // --------------------------------------------------------------- theme

    var THEMES = ["light", "dark", "oled"];
    var THEME_LABELS = { light: "Light", dark: "Dark", oled: "OLED" };

    function applyTheme(theme) {
        state.theme = theme;
        document.documentElement.setAttribute("data-theme", theme);
        el.themeLabel.textContent = THEME_LABELS[theme] || theme;
        post("saveTheme", { theme: theme });
    }

    function cycleTheme() {
        var at = THEMES.indexOf(state.theme);
        applyTheme(THEMES[(at + 1) % THEMES.length]);
        if (window.NesSfx) window.NesSfx.tick();
    }

    // ---------------------------------------------------------------- wire

    el.scan.addEventListener("click", function () {
        var path = el.path.value.trim();

        if (!path) {
            setStatus("Enter a path first");
            if (window.NesSfx) window.NesSfx.failure();
            return;
        }

        if (window.NesSfx) window.NesSfx.select();
        setStatus("Scanning\u2026");
        el.list.innerHTML = '<p class="empty">Reading headers\u2026</p>';
        post("scan", { path: path, recursive: el.recursive.checked });
    });

    el.browse.addEventListener("click", function () {
        post("pickFolder", {});
        if (window.NesSfx) window.NesSfx.tick();
    });

    el.path.addEventListener("keydown", function (e) {
        if (e.key === "Enter") el.scan.click();
    });

    el.list.addEventListener("click", function (e) {
        var row = e.target.closest(".rom-row");
        if (!row) return;
        toggleRow(parseInt(row.dataset.index, 10));
    });

    el.selectAll.addEventListener("click", function () {
        for (var i = 0; i < state.roms.length; i++) state.selected.add(i);
        renderRoms();
        if (window.NesSfx) window.NesSfx.tick();
    });

    el.selectNone.addEventListener("click", function () {
        state.selected.clear();
        renderRoms();
        if (window.NesSfx) window.NesSfx.tick();
    });

    el.lift.addEventListener("click", function () {
        var indices = Array.from(state.selected);

        if (indices.length === 0) {
            setStatus("Nothing selected");
            if (window.NesSfx) window.NesSfx.failure();
            return;
        }

        if (window.NesSfx) window.NesSfx.start();
        setBusy(true);
        setStatus("Lifting\u2026");
        setProgress(0, indices.length);

        post("lift", {
            indices: indices,
            packageMode: el.packageMode.value,
            iconSeconds: parseInt(el.iconSeconds.value, 10) || 0,
            iconPath: el.iconPath.value.trim(),
            outputPath: el.outputPath.value.trim(),
            saveSource: el.saveSource.checked
        });
    });

    el.selfTest.addEventListener("click", function () {
        if (window.NesSfx) window.NesSfx.start();
        setBusy(true);
        setStatus("Self-test running\u2026");
        setProgress(0, 0);
        post("selfTest", {});
    });

    el.clearLog.addEventListener("click", function () {
        el.log.innerHTML = "";
        if (window.NesSfx) window.NesSfx.tick();
    });

    el.openOutput.addEventListener("click", function () {
        post("openOutput", { path: el.outputPath.value.trim() });
        if (window.NesSfx) window.NesSfx.tick();
    });

    el.iconPick.addEventListener("click", function () {
        post("pickIcon", {});
        if (window.NesSfx) window.NesSfx.tick();
    });

    el.themeToggle.addEventListener("click", cycleTheme);

    el.soundToggle.addEventListener("click", function () {
        state.sound = !state.sound;

        if (window.NesSfx) window.NesSfx.setEnabled(state.sound);

        el.soundToggle.innerHTML = state.sound
            ? '<i class="fas fa-volume-high"></i>'
            : '<i class="fas fa-volume-xmark"></i>';

        post("saveSound", { enabled: state.sound });

        if (state.sound && window.NesSfx) window.NesSfx.select();
    });

    // ---------------------------------------------------------- host events

    function handleHostMessage(raw) {
        var msg;

        // PostWebMessageAsJson delivers an already-parsed object; a plain
        // PostWebMessageAsString would deliver a string. Accept both so the
        // bridge does not silently drop every message.
        if (typeof raw === "string") {
            try {
                msg = JSON.parse(raw);
            } catch (e) {
                return;
            }
        } else if (raw && typeof raw === "object") {
            msg = raw;
        } else {
            return;
        }

        switch (msg.type) {
            case "init":
                if (msg.theme) applyTheme(msg.theme);
                if (msg.sound) {
                    state.sound = true;
                    if (window.NesSfx) window.NesSfx.setEnabled(true);
                    el.soundToggle.innerHTML = '<i class="fas fa-volume-high"></i>';
                }
                if (msg.outputPath) el.outputPath.value = msg.outputPath;
                if (msg.packageMode) el.packageMode.value = msg.packageMode;
                if (msg.iconSeconds !== undefined) el.iconSeconds.value = msg.iconSeconds;
                if (msg.recursive !== undefined) el.recursive.checked = !!msg.recursive;
                if (msg.saveSource !== undefined) el.saveSource.checked = !!msg.saveSource;

                if (msg.path) {
                    el.path.value = msg.path;
                }

                break;

            case "roms":
                state.roms = msg.roms || [];
                state.selected.clear();

                // Pre-select everything the lifter can actually handle.
                for (var i = 0; i < state.roms.length; i++) {
                    if (state.roms[i].mapperSupported) state.selected.add(i);
                }

                renderRoms();
                setStatus(state.roms.length + " cartridges");
                break;

            case "log":
                appendLog(msg.text, classifyLine(msg.text || ""));
                break;

            case "progress":
                setProgress(msg.done, msg.total);
                break;

            case "romResult":
                if (msg.index >= 0 && msg.index < state.roms.length) {
                    var row = el.list.querySelector('[data-index="' + msg.index + '"]');
                    if (row) {
                        var existing = row.querySelector(".rom-result");
                        if (!existing) {
                            existing = document.createElement("span");
                            existing.className = "rom-result";
                            row.appendChild(existing);
                        }
                        existing.textContent = msg.ok ? "ok" : "failed";
                        existing.classList.add(msg.ok ? "ok" : "failed");
                    }
                }
                break;

            case "done":
                setBusy(false);
                clearProgress();
                setStatus(msg.summary || "Done");

                if (window.NesSfx) {
                    if (msg.failed > 0) window.NesSfx.failure();
                    else window.NesSfx.success();
                }
                break;

            case "iconPicked":
                if (msg.path) el.iconPath.value = msg.path;
                break;

            case "folderPicked":
                if (msg.path) {
                    el.path.value = msg.path;
                    el.scan.click();
                }
                break;

            case "error":
                appendLog(msg.text || "Unknown error", "error");
                setBusy(false);
                clearProgress();
                setStatus("Error");
                if (window.NesSfx) window.NesSfx.failure();
                break;
        }
    }

    if (host) {
        host.addEventListener("message", function (e) {
            handleHostMessage(e.data);
        });

        post("ready", {});
    } else {
        appendLog("Preview mode: no host attached. The UI is static.", "dim");
    }
    // Keyboard shortcuts, matching the TUI where it makes sense.
    document.addEventListener("keydown", function (e) {
        if (e.key === "F5") {
            e.preventDefault();
            el.scan.click();
        } else if (e.key === "F2") {
            e.preventDefault();
            cycleTheme();
        }
    });
})();
