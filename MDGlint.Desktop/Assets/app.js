// Preview page wiring. Receives messages from the desktop app and updates the page in place; the
// decisions are made in preview.js, which is tested on its own. This file is covered by the
// end-to-end tests, which run it in the app.
//
// Host -> page:
//   { type: "render", html: "<p>...</p>", base: "https://doc.mdglint.example/C/docs/" | null,
//     theme: "light" | "dark", resetScroll: true | false, offersOpen: true | false,
//     printLight: true | false, title: "notes.md" | null }
//     offersOpen is true only for the no-file page, which then gets the shell's Open button.
//     printLight prints (and exports PDFs) in the light theme whatever the window shows.
//   { type: "theme", theme: "light" | "dark", printLight: true | false }
// Page -> host:
//   { type: "ready" } once the page has loaded (again after any reload).
//   { type: "drop" } with the dropped File objects, when files are dropped on the preview.
//     WebView2 is a window of its own, so the WPF window never sees drops over the preview.
//   { type: "open" } when the user clicks Open on the no-file page; the app shows its Open dialog.
// Each code block gets the shell's Copy button, which copies the block's text with the page's
// own clipboard access; the app isn't involved.
// For Export ▸ HTML the app calls mdglintExport() (ExecuteScriptAsync), which returns the shown
// content's HTML.
(() => {
  'use strict';

  const preview = globalThis.MDGlintPreview;
  const webview = window.chrome && window.chrome.webview;
  const content = document.getElementById('mdglint-content');
  const base = document.getElementById('mdglint-base');
  const markdownThemeLink = document.getElementById('mdglint-markdown-theme');
  const codeThemeLink = document.getElementById('mdglint-code-theme');
  const printLinks = [
    document.getElementById('mdglint-print-markdown-theme'),
    document.getElementById('mdglint-print-code-theme')
  ];
  // Taken from the shell before any content arrives, so only this element can ask for the dialog.
  const openPrompt = document.getElementById('mdglint-open-prompt').content.firstElementChild.cloneNode(true);
  const copyButtonTemplate = document.getElementById('mdglint-copy-button').content.firstElementChild;
  // The buttons this script made; a click on anything else that looks like one does nothing.
  const copyButtons = new WeakSet();
  const copyResetMs = 2000;

  // Pin the shell's own stylesheet links to absolute URLs before the <base> is ever moved to the
  // file's folder, so they keep pointing at the app host.
  for (const link of document.querySelectorAll('link[rel="stylesheet"]')) {
    link.setAttribute('href', link.href);
  }

  function setHref(link, href) {
    if (link.href !== href) {
      link.href = href;
    }
  }

  function applyTheme(theme, printLight) {
    const urls = preview.themeUrls(location.origin, theme);
    const print = preview.printPlan(urls.name, printLight);
    setHref(markdownThemeLink, urls.markdown);
    setHref(codeThemeLink, urls.code);
    markdownThemeLink.media = print.screenMedia;
    codeThemeLink.media = print.screenMedia;
    for (const link of printLinks) {
      link.media = print.printMedia;
    }
    document.documentElement.dataset.theme = urls.name;
    document.documentElement.dataset.printTheme = print.printTheme;
  }

  function render(message) {
    const update = preview.readRender(message);
    applyTheme(update.theme, update.printLight);
    // The page title names the file in a printout's header and a PDF's properties.
    document.title = update.title ?? 'MDGlint';

    const anchor = update.resetScroll
      ? null
      : preview.captureAnchor(Array.from(content.children), window.scrollY, document.body.hidden);
    const previousScrollY = window.scrollY;

    // Set the base before inserting content so relative URLs resolve against the file's folder.
    if (update.base) {
      base.setAttribute('href', update.base);
    } else {
      base.removeAttribute('href');
    }

    // Parse into an inert template first, so absolute paths are rewritten before anything loads.
    const template = document.createElement('template');
    template.innerHTML = update.html;
    preview.rewriteLocalPaths(template.content);
    content.replaceChildren(template.content);
    if (update.offersOpen) {
      content.append(openPrompt);
    }

    preview.highlightCode(content, window.hljs);
    addCopyButtons();

    const scroll = preview.scrollAfterRender(update.resetScroll, anchor, Array.from(content.children), previousScrollY);
    if (scroll.by !== undefined) {
      window.scrollBy(0, scroll.by);
    } else {
      window.scrollTo(0, scroll.top);
    }

    document.body.hidden = false;
  }

  // The shown file as it reads on screen, highlighting included, without the shell's own
  // controls: code blocks lose their Copy buttons, and the no-file page's Open prompt is dropped.
  // Image and media addresses become absolute, as relative ones only make sense under <base>.
  globalThis.mdglintExport = () => {
    const copy = content.cloneNode(true);
    for (const block of copy.querySelectorAll('.code-block')) {
      block.replaceWith(...block.querySelectorAll(':scope > pre'));
    }

    for (const prompt of copy.querySelectorAll('.open-prompt')) {
      prompt.remove();
    }

    for (const element of copy.querySelectorAll('[src]')) {
      element.setAttribute('src', new URL(element.getAttribute('src'), document.baseURI).href);
    }

    return copy.innerHTML;
  };

  // Wraps each code block so its Copy button can sit in the corner without scrolling with the code.
  function addCopyButtons() {
    for (const pre of content.querySelectorAll('pre')) {
      const block = document.createElement('div');
      block.className = 'code-block';
      const button = copyButtonTemplate.cloneNode(true);
      copyButtons.add(button);
      pre.replaceWith(block);
      block.append(pre, button);
    }
  }

  function showCopyState(button, state) {
    const label = preview.copyLabel(state);
    button.setAttribute('aria-label', label);
    button.title = label;
    button.dataset.state = state;
    clearTimeout(button.copyReset);
    if (state !== 'ready') {
      button.copyReset = setTimeout(() => showCopyState(button, 'ready'), copyResetMs);
    }
  }

  content.addEventListener('click', async (event) => {
    const button = event.target instanceof Element ? event.target.closest('button.copy-code') : null;
    if (!button || !copyButtons.has(button) || !event.isTrusted) {
      return;
    }

    try {
      await navigator.clipboard.writeText(preview.codeText(button.parentElement.querySelector(':scope > pre')));
      showCopyState(button, 'copied');
    } catch {
      showCopyState(button, 'failed');
    }
  });

  document.addEventListener('click', (event) => {
    const link = event.target instanceof Element ? event.target.closest('a[href]') : null;
    const id = link ? preview.inPageTarget(link.getAttribute('href')) : null;
    if (id === null) {
      return;
    }

    event.preventDefault();
    if (id === '') {
      window.scrollTo(0, 0);
      return;
    }

    const target = preview.findTarget(content, id, CSS.escape);
    if (target) {
      target.scrollIntoView();
    }
  });

  if (!webview) {
    return;
  }

  webview.addEventListener('message', (event) => {
    switch (preview.messageType(event.data)) {
      case 'render':
        render(event.data);
        break;
      case 'theme':
        applyTheme(event.data.theme, event.data.printLight === true);
        break;
    }
  });

  openPrompt.querySelector('button').addEventListener('click', (event) => {
    if (event.isTrusted) {
      webview.postMessage({ type: 'open' });
    }
  });

  // Without preventDefault the page would navigate to a dropped file (which the host cancels).
  document.addEventListener('dragover', (event) => {
    if (preview.isFileDrag(event.dataTransfer)) {
      event.preventDefault();
      event.dataTransfer.dropEffect = 'copy';
    }
  });

  document.addEventListener('drop', (event) => {
    if (!preview.isFileDrag(event.dataTransfer)) {
      return;
    }

    event.preventDefault();
    if (event.dataTransfer.files.length > 0) {
      webview.postMessageWithAdditionalObjects({ type: 'drop' }, event.dataTransfer.files);
    }
  });

  webview.postMessage({ type: 'ready' });
})();
