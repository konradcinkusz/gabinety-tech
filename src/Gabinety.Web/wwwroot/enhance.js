/* Progressive enhancement (DESIGN-CONTRACT rule 1, ADR-0010). Same-origin, no dependency. Every page works without this file; each behaviour has a no-script
   fallback that is the page as served. Keyed on existing markup or data-enhance attributes; data-enhance="off" opts an element out. */
(function () {
  "use strict";
  var doc = document;

  function closest(el, selector) { return el && el.closest ? el.closest(selector) : null; }
  function off(el) { return !!closest(el, '[data-enhance="off"]'); }

  /* progress bar while a link or form is followed: html.is-navigating (app.css); cleared on pageshow, enhanced load, after 10 s */
  var navTimer;
  function navigating() { doc.documentElement.classList.add("is-navigating"); clearTimeout(navTimer); navTimer = setTimeout(idle, 10000); }
  function idle() { doc.documentElement.classList.remove("is-navigating"); }
  doc.addEventListener("click", function (e) {
    var a = closest(e.target, "a[href]");
    if (!a || e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey || off(a)) return;
    if ((a.target && a.target !== "_self") || a.hasAttribute("download") || a.origin !== location.origin) return;
    if (a.pathname === location.pathname && a.search === location.search) return; /* a hash jump or the same page */
    navigating();
  });

  /* (a) the counter under a control follows the text; newline counts as CR LF like the server */
  doc.addEventListener("input", function (e) {
    var control = e.target;
    if (!control || !control.classList || !control.classList.contains("field__control") || off(control)) return;
    var field = closest(control, ".field");
    var counter = field && field.querySelector(".field__counter");
    if (!counter) return;
    var max = /\/\s*(\d+)/.exec(counter.textContent || "");
    if (!max) return;
    var length = control.value.replace(/\r?\n/g, "\r\n").trim().length;
    counter.textContent = length + " / " + max[1];
  });

  /* (b) a submitting form marks the clicked button busy and refuses a second submit; NOT disabled: a disabled submitter would not post its name and value */
  doc.addEventListener("submit", function (e) {
    var form = e.target;
    if (!form || form.tagName !== "FORM" || off(form) || (form.method || "").toLowerCase() !== "post") return;
    if (form.getAttribute("data-submitting") === "true") { e.preventDefault(); return; }
    form.setAttribute("data-submitting", "true");
    navigating();
    var button = e.submitter;
    if (button && button.classList.contains("btn")) button.setAttribute("aria-busy", "true");
  });
  window.addEventListener("pageshow", function (e) {
    idle();
    if (!e.persisted) return;
    doc.querySelectorAll('form[data-submitting="true"]').forEach(function (f) { f.removeAttribute("data-submitting"); });
    doc.querySelectorAll('.btn[aria-busy="true"]').forEach(function (b) { b.removeAttribute("aria-busy"); });
  });

  /* (d) a select that filters a GET form applies at once */
  doc.addEventListener("change", function (e) {
    var select = e.target;
    if (!select || select.getAttribute("data-enhance") !== "autosubmit" || !select.form || off(select)) return;
    if (select.form.requestSubmit) select.form.requestSubmit(); else select.form.submit();
  });

  /* (e) show/hide a password, (f) copy a link, dismiss a flash: buttons rendered hidden and shown here */
  doc.addEventListener("click", function (e) {
    var button = closest(e.target, "button[data-enhance]");
    if (!button || off(button)) return;
    var kind = button.getAttribute("data-enhance");
    if (kind === "dismiss") {
      var region = closest(button, button.getAttribute("data-target") || "");
      if (region) region.classList.add("flash--dismissed");
    } else if (kind === "reveal") {
      var input = doc.querySelector(button.getAttribute("data-target") || "");
      if (!input) return;
      var show = input.type === "password";
      input.type = show ? "text" : "password";
      button.setAttribute("aria-pressed", show ? "true" : "false");
      var label = button.getAttribute(show ? "data-hide" : "data-show");
      if (label) button.textContent = label;
    } else if (kind === "copy" && navigator.clipboard) {
      var text = button.getAttribute("data-copy") || "";
      var original = button.textContent;
      navigator.clipboard.writeText(text.charAt(0) === "/" ? location.origin + text : text).then(function () {
        var done = button.getAttribute("data-done");
        if (done) { button.textContent = done; setTimeout(function () { button.textContent = original; }, 3000); }
      });
    }
  });

  /* (c) a server-rendered OPEN dialog becomes a modal; its state lives in the URL, so closing follows the a[data-dialog-dismiss] link */
  function upgradeDialogs() {
    doc.querySelectorAll('dialog[data-enhance="dialog"][open]').forEach(function (dialog) {
      if (dialog.getAttribute("data-upgraded") === "true" || off(dialog) || typeof dialog.showModal !== "function") return;
      dialog.setAttribute("data-upgraded", "true");
      var opener = doc.activeElement;
      dialog.removeAttribute("open"); dialog.showModal(); // not close(): that queues a close event, which would follow the dismiss link at once
      dialog.addEventListener("close", function () {
        var dismiss = dialog.querySelector("a[data-dialog-dismiss]");
        if (dialog.returnValue === "confirm") return;
        if (dismiss && dismiss.href) location.href = dismiss.href; else if (opener && opener.focus) opener.focus();
      });
    });
  }

  function showControls() {
    doc.querySelectorAll('button[data-enhance="reveal"][hidden], button[data-enhance="dismiss"][hidden]').forEach(function (b) { b.hidden = false; });
    if (navigator.clipboard) doc.querySelectorAll('button[data-enhance="copy"][hidden]').forEach(function (b) { b.hidden = false; });
  }

  /* (g) slot times are Warsaw times: a visitor in another zone gets a note and each time's local equivalent as a title */
  function hm(date, zone) { return new Intl.DateTimeFormat("pl-PL", { hour: "2-digit", minute: "2-digit", hour12: false, timeZone: zone }).format(date); }
  function localTimes() {
    var zone; try { zone = Intl.DateTimeFormat().resolvedOptions().timeZone; } catch (e) { return; }
    if (!zone || zone === "Europe/Warsaw") return;
    doc.querySelectorAll('[data-enhance="local-time-note"]').forEach(function (note) {
      if (off(note) || note.getAttribute("data-done") === "true") return;
      var picker = closest(note, ".slot-picker") || doc;
      var slots = picker.querySelectorAll("a[data-utc]");
      if (!slots.length) return;
      var any = false;
      slots.forEach(function (a) {
        var at = new Date(a.getAttribute("data-utc")), pl = hm(at, "Europe/Warsaw"), local = hm(at, zone);
        if (pl === local) return;
        any = true;
        a.title = (note.getAttribute("data-title") || "").replace("{local}", local).replace("{zone}", zone);
      });
      if (!any) return;
      var first = new Date(slots[0].getAttribute("data-utc"));
      note.textContent = (note.getAttribute("data-template") || "").replace("{zone}", zone).replace("{pl}", hm(first, "Europe/Warsaw")).replace("{local}", hm(first, zone));
      note.hidden = false; note.setAttribute("data-done", "true");
    });
  }

  function init() { idle(); upgradeDialogs(); showControls(); localTimes(); }

  if (doc.readyState === "loading") doc.addEventListener("DOMContentLoaded", init); else init();
  if (window.Blazor && window.Blazor.addEventListener) window.Blazor.addEventListener("enhancedload", init);
  else doc.addEventListener("enhancedload", init);
})();
