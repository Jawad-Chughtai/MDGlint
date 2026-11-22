// The preview page's decisions, kept apart from the page itself so they can be tested in Node
// (MDGlint.Desktop.Tests/Preview). Nothing here reads or changes the page: app.js passes in the
// elements and values these functions need and applies what they return.
globalThis.MDGlintPreview = (() => {
  'use strict';

  // Messages from the desktop app (see app.js).
  function messageType(data) {
    const type = data !== null && typeof data === 'object' ? data.type : undefined;
    return type === 'render' || type === 'theme' ? type : null;
  }

  function readRender(message) {
    return {
      theme: message.theme,
      html: typeof message.html === 'string' ? message.html : '',
      base: typeof message.base === 'string' && message.base.length > 0 ? message.base : null,
      resetScroll: message.resetScroll === true,
      offersOpen: message.offersOpen === true,
      printLight: message.printLight === true,
      title: typeof message.title === 'string' && message.title.length > 0 ? message.title : null
    };
  }

  // How the theme's stylesheets apply when printing. With printLight and the dark theme, the dark
  // stylesheets are for the screen only and the light ones apply to print (and PDF export);
  // otherwise the shown theme applies to both, and the print links are switched off.
  function printPlan(theme, printLight) {
    const lightForPrint = printLight === true && theme === 'dark';
    return {
      screenMedia: lightForPrint ? 'screen' : 'all',
      printMedia: lightForPrint ? 'print' : 'not all',
      printTheme: lightForPrint || theme !== 'dark' ? 'light' : 'dark'
    };
  }

  // Absolute, so the stylesheets never resolve against the document <base>.
  function themeUrls(origin, theme) {
    const name = theme === 'dark' ? 'dark' : 'light';
    const root = origin + '/';
    return {
      name,
      markdown: `${root}vendor/github-markdown-css/github-markdown-${name}.css`,
      code: `${root}vendor/highlight.js/${name === 'dark' ? 'github-dark' : 'github'}.min.css`
    };
  }

  // Local files are served from the document host, whose URL path spells out the full path:
  // C:\docs\a.png -> https://doc.mdglint.example/C/docs/a.png,
  // \\server\share\a.png -> https://doc.mdglint.example/UNC/server/share/a.png.
  const documentRoot = 'https://doc.mdglint.example/';

  function decodeSafely(text) {
    try {
      return decodeURIComponent(text);
    } catch {
      return text;
    }
  }

  function toDocumentUrl(segments, suffix) {
    return documentRoot + segments
      .filter((segment) => segment !== '')
      .map((segment) => encodeURIComponent(decodeSafely(segment)))
      .join('/') + suffix;
  }

  // Rewrites an absolute local path (C:\x.png, C:/x.png, \\server\share\x.png, file:///C:/x.png)
  // to its document-host URL. Returns null for anything else, which is left untouched.
  function localPathToUrl(value) {
    const text = value.trim().replace(/%5C/gi, '\\');

    if (/^file:/i.test(text)) {
      let url;
      try {
        url = new URL(text);
      } catch {
        return null;
      }

      const suffix = url.search + url.hash;
      if (url.hostname) {
        return toDocumentUrl(['UNC', url.hostname, ...url.pathname.split('/')], suffix);
      }

      const drive = /^\/([A-Za-z])[:|](\/.*)?$/.exec(url.pathname);
      return drive ? toDocumentUrl([drive[1].toUpperCase(), ...(drive[2] || '').split('/')], suffix) : null;
    }

    const hashIndex = text.indexOf('#');
    const path = hashIndex >= 0 ? text.slice(0, hashIndex) : text;
    const suffix = hashIndex >= 0 ? text.slice(hashIndex) : '';

    const drive = /^([A-Za-z]):[\\/](.*)$/.exec(path);
    if (drive) {
      return toDocumentUrl([drive[1].toUpperCase(), ...drive[2].split(/[\\/]/)], suffix);
    }

    // Only the backslash form: "//host/path" is a protocol-relative web URL, not a share.
    const share = /^\\\\([^\\/]+)[\\/]([^\\/]+)(?:[\\/](.*))?$/.exec(path);
    if (share) {
      return toDocumentUrl(['UNC', share[1], share[2], ...(share[3] || '').split(/[\\/]/)], suffix);
    }

    return null;
  }

  const localPathAttributes = [
    ['a', 'href'], ['img', 'src'], ['source', 'src'], ['video', 'src'], ['video', 'poster'], ['audio', 'src']
  ];

  function rewriteLocalPaths(root) {
    for (const [tag, attribute] of localPathAttributes) {
      for (const element of root.querySelectorAll(`${tag}[${attribute}]`)) {
        const url = localPathToUrl(element.getAttribute(attribute));
        if (url) {
          element.setAttribute(attribute, url);
        }
      }
    }
  }

  // Keeping the reader's place when the same file is rendered again. A plain scroll offset is
  // wrong as soon as content above the viewport grows or shrinks, so the position is recorded
  // relative to the block at the top of the viewport: which heading it follows, how many blocks
  // after that heading it is, its text, and how far it sits from the viewport top. After the swap
  // the same block is found again and scrolled to the same spot. Later size changes above it
  // (images finishing loading) are handled by the browser's own scroll anchoring.
  function isHeading(element) {
    return /^H[1-6]$/.test(element.tagName);
  }

  function blockText(element) {
    return element.textContent.trim().replace(/\s+/g, ' ').slice(0, 300);
  }

  function describeHeading(heading, blocks) {
    const headings = blocks.filter(isHeading);
    const text = blockText(heading);
    return {
      id: heading.id,
      tag: heading.tagName,
      text,
      // Which occurrence of this exact heading it is, for documents that repeat a heading.
      occurrence: headings.filter((h) => h.tagName === heading.tagName && blockText(h) === text).indexOf(heading),
      index: headings.indexOf(heading),
      count: headings.length
    };
  }

  function findHeading(description, blocks) {
    const headings = blocks.filter(isHeading);
    if (description.id) {
      const byId = headings.find((h) => h.id === description.id);
      if (byId) {
        return byId;
      }
    }

    const sameText = headings.filter((h) => h.tagName === description.tag && blockText(h) === description.text);
    if (sameText.length > 0) {
      return sameText[Math.min(description.occurrence, sameText.length - 1)];
    }

    // Renamed heading: fall back to its position, but only if no heading was added or removed.
    return headings.length === description.count ? headings[description.index] : null;
  }

  // blocks: the top-level elements of the rendered Markdown, in order.
  function captureAnchor(blocks, scrollY, hidden) {
    if (hidden || scrollY === 0 || blocks.length === 0) {
      return null;
    }

    let index = blocks.findIndex((block) => block.getBoundingClientRect().bottom > 0);
    if (index < 0) {
      index = blocks.length - 1;
    }

    let headingIndex = index;
    while (headingIndex >= 0 && !isHeading(blocks[headingIndex])) {
      headingIndex--;
    }

    const block = blocks[index];
    return {
      heading: headingIndex >= 0 ? describeHeading(blocks[headingIndex], blocks) : null,
      offsetInSection: index - Math.max(headingIndex, 0),
      tag: block.tagName,
      text: blockText(block),
      top: block.getBoundingClientRect().top
    };
  }

  // How far to scroll so the anchored block is back where it was, or null when it's gone.
  function restoreAnchor(anchor, blocks) {
    let start = 0;
    if (anchor.heading) {
      const heading = findHeading(anchor.heading, blocks);
      if (!heading) {
        return null;
      }
      start = blocks.indexOf(heading);
    }

    let end = blocks.length;
    for (let i = start + 1; i < blocks.length; i++) {
      if (isHeading(blocks[i])) {
        end = i;
        break;
      }
    }

    // Prefer the same block by content (blocks may have been added or removed before it in the
    // section); if it was edited, use its position in the section.
    const section = blocks.slice(start, end);
    const block = section.find((b) => b.tagName === anchor.tag && blockText(b) === anchor.text)
      || section[Math.min(anchor.offsetInSection, section.length - 1)];
    return block ? block.getBoundingClientRect().top - anchor.top : null;
  }

  // Where to scroll after a render: { top } to scroll to, or { by } to scroll by.
  function scrollAfterRender(resetScroll, anchor, blocks, previousScrollY) {
    if (resetScroll) {
      return { top: 0 };
    }

    const by = anchor ? restoreAnchor(anchor, blocks) : null;
    return by === null ? { top: previousScrollY } : { by };
  }

  // Highlights fenced code blocks that name a language highlight.js knows (```js). Like GitHub,
  // blocks without a language, or with an unknown one, stay plain instead of being guessed.
  function highlightCode(root, hljs) {
    if (!hljs) {
      return;
    }

    for (const code of root.querySelectorAll('pre > code[class*="language-"]')) {
      const match = /(?:^|\s)language-(\S+)/.exec(code.className);
      if (match && hljs.getLanguage(match[1])) {
        hljs.highlightElement(code);
      }
    }
  }

  // What a code block's Copy button copies: the code as written in the file, without the newline
  // every rendered block ends with. Highlighting only wraps the text in spans, so it stays the same.
  function codeText(pre) {
    const code = pre.querySelector('code') || pre;
    return code.textContent.replace(/\n$/, '');
  }

  // The Copy button's label: "Copy", then for a moment what happened.
  function copyLabel(state) {
    switch (state) {
      case 'copied':
        return 'Copied';
      case 'failed':
        return "Couldn't copy";
      default:
        return 'Copy';
    }
  }

  // With a <base> pointing at the file's folder, "#heading" links would otherwise resolve to a
  // different document, so app.js handles them as in-page jumps. Returns the target's id ("" for
  // the top of the page), or null for any other link.
  function inPageTarget(href) {
    return href.startsWith('#') ? decodeSafely(href.slice(1)) : null;
  }

  function findTarget(root, id, escape) {
    return root.querySelector('#' + escape(id)) || root.querySelector('[name="' + escape(id) + '"]');
  }

  // A drag that carries files from Explorer (not text or a link dragged within the page).
  function isFileDrag(dataTransfer) {
    return Boolean(dataTransfer && dataTransfer.types && Array.from(dataTransfer.types).includes('Files'));
  }

  return {
    messageType,
    isFileDrag,
    readRender,
    printPlan,
    themeUrls,
    localPathToUrl,
    rewriteLocalPaths,
    captureAnchor,
    restoreAnchor,
    scrollAfterRender,
    highlightCode,
    codeText,
    copyLabel,
    inPageTarget,
    findTarget
  };
})();
