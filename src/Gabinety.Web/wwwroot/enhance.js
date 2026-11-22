/* Progressive enhancement of the web host (DESIGN-CONTRACT rule 1, ADR-0010; UX-005). Same-origin, no dependency, no network, no framework.
   Every page works without this file: it only adds what a static page cannot do (live counters, a pending button, a modal dialog, show-password, copy, auto-submit).
   Behaviours are keyed on markup the pages already render or on data-enhance attributes; each has a no-script fallback that is the current behaviour.
   A page that must not be touched by it carries data-enhance="off" on the element. */
(function () {
  "use strict";
  var doc = document;

  function closest(el, selector) { return el && el.closest ? el.closest(selector) : null; }
  function off(el) { return !!closest(el, '[data-enhance="off"]'); }

  /* (a) the character counter under a control follows what is typed. The server counts the trimmed text with a newline as CR LF (what a browser posts): so does this. */
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

  /* (b) a submitting form marks the clicked button busy and ignores a second submit. The button is NOT disabled: a disabled submitter does not post its name and value, which the
     pages use to tell the assistant, apply and pay buttons apart. */
  doc.addEventListener("submit", function (e) {
    var form = e.target;
    if (!form || form.tagName !== "FORM" || off(form) || (form.method || "").toLowerCase() !== "post") return;
    if (form.getAttribute("data-submitting") === "true") { e.preventDefault(); return; }
    form.setAttribute("data-submitting", "true");
    var button = e.submitter;
    if (button && button.classList.contains("btn")) button.setAttribute("aria-busy", "true");
  });
  /* the back button restores a page from the cache with the busy state still on it */
  window.addEventListener("pageshow", function (e) {
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

  /* (e) show or hide a password; (f) copy a link. Both buttons are rendered with the hidden attribute and shown here, so without this file they are simply not there. */
  doc.addEventListener("click", function (e) {
    var button = closest(e.target, "button[data-enhance]");
    if (!button || off(button)) return;
    var kind = button.getAttribute("data-enhance");
    if (kind === "reveal") {
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

  /* (c) a dialog the server renders OPEN (visible without this file, as a block in the page) becomes a real modal: focus trap, backdrop, Esc. The state of such a dialog lives in the URL
     (?action=cancel), so closing it follows the link marked data-dialog-dismiss. */
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

  /* reveal and copy buttons appear only when the script can serve them */
  function showControls() {
    doc.querySelectorAll('button[data-enhance="reveal"][hidden]').forEach(function (b) { b.hidden = false; });
    if (navigator.clipboard) doc.querySelectorAll('button[data-enhance="copy"][hidden]').forEach(function (b) { b.hidden = false; });
  }

  function init() { upgradeDialogs(); showControls(); }

  if (doc.readyState === "loading") doc.addEventListener("DOMContentLoaded", init); else init();
  /* Blazor enhanced navigation swaps the page body without a load event */
  if (window.Blazor && window.Blazor.addEventListener) window.Blazor.addEventListener("enhancedload", init);
  else doc.addEventListener("enhancedload", init);
})();
