// Tests for Assets/preview.js, with Node's built-in test runner (no npm packages):
//   node --test "MDGlint.Desktop.Tests/Preview/*.test.mjs"
// (docs/testing.md has the coverage command CI runs). preview.js never touches the page, so small stand-ins for elements are enough here. The page
// wiring in app.js runs in the end-to-end tests.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';

createRequire(import.meta.url)('../../MDGlint.Desktop/Assets/preview.js');
const preview = globalThis.MDGlintPreview;

// A top-level block: its tag, text and where it sits relative to the viewport top.
function block(tagName, textContent, top = 0, { id = '', height = 20 } = {}) {
  return { tagName, textContent, id, getBoundingClientRect: () => ({ top, bottom: top + height }) };
}

// Lays blocks out one under another, the viewport scrolled to scrollY.
function layout(blocks, scrollY) {
  let y = -scrollY;
  return blocks.map(([tag, text, id]) => {
    const element = block(tag, text, y, { id });
    y += 20;
    return element;
  });
}

// An element with attributes, found by a "tag[attribute]" selector.
function fragment(elements) {
  return {
    querySelectorAll(selector) {
      const [, tag, attribute] = /^(\w+)\[(\w+)\]$/.exec(selector);
      return elements.filter((e) => e.tag === tag && attribute in e.attributes);
    }
  };
}

function element(tag, attributes) {
  return {
    tag,
    attributes,
    getAttribute: (name) => attributes[name],
    setAttribute: (name, value) => { attributes[name] = value; }
  };
}

test('messageType accepts only the two messages', () => {
  assert.equal(preview.messageType({ type: 'render' }), 'render');
  assert.equal(preview.messageType({ type: 'theme' }), 'theme');
  assert.equal(preview.messageType({ type: 'other' }), null);
  assert.equal(preview.messageType({}), null);
  assert.equal(preview.messageType('render'), null);
  assert.equal(preview.messageType(null), null);
  assert.equal(preview.messageType(undefined), null);
});

test('isFileDrag is true only for drags that carry files', () => {
  assert.equal(preview.isFileDrag({ types: ['Files'] }), true);
  assert.equal(preview.isFileDrag({ types: ['text/plain', 'Files'] }), true);
  assert.equal(preview.isFileDrag({ types: ['text/plain', 'text/uri-list'] }), false);
  assert.equal(preview.isFileDrag({ types: [] }), false);
  assert.equal(preview.isFileDrag({}), false);
  assert.equal(preview.isFileDrag(null), false);
  assert.equal(preview.isFileDrag(undefined), false);
});

test('readRender fills in what a message leaves out', () => {
  assert.deepEqual(preview.readRender({ theme: 'dark', html: '<p>x</p>', base: 'https://doc.mdglint.example/C/', resetScroll: true, offersOpen: true, printLight: true, title: 'notes.md' }),
    { theme: 'dark', html: '<p>x</p>', base: 'https://doc.mdglint.example/C/', resetScroll: true, offersOpen: true, printLight: true, title: 'notes.md' });
  assert.deepEqual(preview.readRender({ html: 5, base: '', resetScroll: 'yes', offersOpen: 'yes', printLight: 'yes', title: 3 }),
    { theme: undefined, html: '', base: null, resetScroll: false, offersOpen: false, printLight: false, title: null });
  assert.equal(preview.readRender({ base: 7 }).base, null);
  assert.equal(preview.readRender({ title: '' }).title, null);
});

test('printPlan prints dark files in the light theme only when asked', () => {
  assert.deepEqual(preview.printPlan('dark', true), { screenMedia: 'screen', printMedia: 'print', printTheme: 'light' });
  assert.deepEqual(preview.printPlan('dark', false), { screenMedia: 'all', printMedia: 'not all', printTheme: 'dark' });
  assert.deepEqual(preview.printPlan('light', true), { screenMedia: 'all', printMedia: 'not all', printTheme: 'light' });
  assert.deepEqual(preview.printPlan('light', false), { screenMedia: 'all', printMedia: 'not all', printTheme: 'light' });
  assert.equal(preview.printPlan('dark', 'yes').printTheme, 'dark');
});

test('themeUrls names absolute stylesheets for each theme', () => {
  assert.deepEqual(preview.themeUrls('https://app.mdglint.example', 'dark'), {
    name: 'dark',
    markdown: 'https://app.mdglint.example/vendor/github-markdown-css/github-markdown-dark.css',
    code: 'https://app.mdglint.example/vendor/highlight.js/github-dark.min.css'
  });
  assert.deepEqual(preview.themeUrls('https://app.mdglint.example', 'light'), {
    name: 'light',
    markdown: 'https://app.mdglint.example/vendor/github-markdown-css/github-markdown-light.css',
    code: 'https://app.mdglint.example/vendor/highlight.js/github.min.css'
  });
  assert.equal(preview.themeUrls('x', 'purple').name, 'light');
});

test('localPathToUrl rewrites absolute local paths', () => {
  const cases = {
    'C:\\docs\\a.png': 'https://doc.mdglint.example/C/docs/a.png',
    'c:/docs/a b.png': 'https://doc.mdglint.example/C/docs/a%20b.png',
    '  C:\\docs\\a.png  ': 'https://doc.mdglint.example/C/docs/a.png',
    'C:%5Cdocs%5ca.png': 'https://doc.mdglint.example/C/docs/a.png',
    'C:\\docs\\notes.md#part-2': 'https://doc.mdglint.example/C/docs/notes.md#part-2',
    'C:\\docs\\100%.png': 'https://doc.mdglint.example/C/docs/100%25.png',
    'C:\\docs\\a%20b.png': 'https://doc.mdglint.example/C/docs/a%20b.png',
    'C:\\docs\\\\a.png': 'https://doc.mdglint.example/C/docs/a.png',
    '\\\\server\\share\\docs\\a.png': 'https://doc.mdglint.example/UNC/server/share/docs/a.png',
    '\\\\server\\share': 'https://doc.mdglint.example/UNC/server/share',
    'file:///C:/docs/a.png': 'https://doc.mdglint.example/C/docs/a.png',
    'file:///d|/docs/a.png?x=1#y': 'https://doc.mdglint.example/D/docs/a.png?x=1#y',
    'file:///C:': 'https://doc.mdglint.example/C',
    'file://server/share/a.png': 'https://doc.mdglint.example/UNC/server/share/a.png'
  };
  for (const [path, url] of Object.entries(cases)) {
    assert.equal(preview.localPathToUrl(path), url, path);
  }
});

test('localPathToUrl leaves everything else alone', () => {
  for (const value of ['a.png', '../a.png', '/a.png', '//cdn.example/a.png', 'https://example.com/a.png',
    '#top', 'C:a.png', '\\\\server', 'file:///docs/a.png', 'file://[bad/a.png', 'mailto:me@example.com', '']) {
    assert.equal(preview.localPathToUrl(value), null, value);
  }
});

test('rewriteLocalPaths changes only local paths, in every media attribute', () => {
  const elements = [
    element('img', { src: 'C:\\docs\\a.png' }),
    element('img', { src: 'b.png' }),
    element('a', { href: 'D:\\notes.md' }),
    element('source', { src: '\\\\s\\h\\c.mp4' }),
    element('video', { src: 'C:\\v.mp4', poster: 'C:\\p.png' }),
    element('audio', { src: 'C:\\a.mp3' })
  ];

  preview.rewriteLocalPaths(fragment(elements));

  assert.deepEqual(elements.map((e) => e.attributes), [
    { src: 'https://doc.mdglint.example/C/docs/a.png' },
    { src: 'b.png' },
    { href: 'https://doc.mdglint.example/D/notes.md' },
    { src: 'https://doc.mdglint.example/UNC/s/h/c.mp4' },
    { src: 'https://doc.mdglint.example/C/v.mp4', poster: 'https://doc.mdglint.example/C/p.png' },
    { src: 'https://doc.mdglint.example/C/a.mp3' }
  ]);
});

const doc = [
  ['H1', 'Title', 'title'],
  ['P', 'Intro'],
  ['H2', 'Setup', 'setup'],
  ['P', 'First'],
  ['P', 'Second'],
  ['P', 'Third'],
  ['H2', 'Usage', 'usage'],
  ['P', 'Run it']
];

test('captureAnchor records nothing at the top, while hidden or when empty', () => {
  assert.equal(preview.captureAnchor(layout(doc, 0), 0, false), null);
  assert.equal(preview.captureAnchor(layout(doc, 50), 50, true), null);
  assert.equal(preview.captureAnchor([], 50, false), null);
});

test('captureAnchor records the block at the viewport top by its heading', () => {
  // Blocks are 20px tall; scrolled 85px, "Second" spans -5 to 15, so it is at the viewport top.
  const anchor = preview.captureAnchor(layout(doc, 85), 85, false);

  assert.deepEqual(anchor, {
    heading: { id: 'setup', tag: 'H2', text: 'Setup', occurrence: 0, index: 1, count: 3 },
    offsetInSection: 2,
    tag: 'P',
    text: 'Second',
    top: -5
  });
});

test('captureAnchor uses the last block when all are above, and no heading before the first one', () => {
  const below = preview.captureAnchor(layout([['P', 'a'], ['P', 'b']], 500), 500, false);
  assert.equal(below.text, 'b');
  assert.equal(below.heading, null);
  assert.equal(below.offsetInSection, 1);

  const spaced = preview.captureAnchor([block('P', '  many\n  spaces  ', -5)], 5, false);
  assert.equal(spaced.text, 'many spaces');
});

test('restoreAnchor finds the same block after content above it grows', () => {
  const anchor = preview.captureAnchor(layout(doc, 85), 85, false);
  const grown = layout([doc[0], doc[1], ['P', 'Inserted'], ['P', 'Also'], ...doc.slice(2)], 85);

  // "Second" now sits 40px lower, at 35: scroll down by 40 to put it back at -5.
  assert.equal(preview.restoreAnchor(anchor, grown), 40);
  assert.deepEqual(preview.scrollAfterRender(false, anchor, grown, 85), { by: 40 });
});

test('restoreAnchor falls back to the position in the section when the block was edited', () => {
  const anchor = preview.captureAnchor(layout(doc, 85), 85, false);
  const edited = layout(doc.map((b) => (b[1] === 'Second' ? ['P', 'Second, edited'] : b)), 85);

  assert.equal(preview.restoreAnchor(anchor, edited), 0);

  // The section got shorter than the offset: its last block.
  const shorter = layout(doc.filter((b) => b[1] !== 'Second' && b[1] !== 'Third'), 85);
  assert.equal(preview.restoreAnchor(anchor, shorter), shorter[3].getBoundingClientRect().top + 5);
});

test('restoreAnchor finds the heading by id, text and occurrence, or by position', () => {
  const anchor = preview.captureAnchor(layout(doc, 85), 85, false);

  // Same id, new text.
  const renamedWithId = layout(doc.map((b) => (b[2] === 'setup' ? ['H2', 'Install', 'setup'] : b)), 85);
  assert.equal(preview.restoreAnchor(anchor, renamedWithId), 0);

  // Ids gone, same text.
  const noIds = layout(doc.map(([tag, text]) => [tag, text]), 85);
  assert.equal(preview.restoreAnchor(anchor, noIds), 0);

  // Renamed and no ids, same number of headings: by position.
  const renamed = layout(doc.map(([tag, text]) => [tag, text === 'Setup' ? 'Install' : text]), 85);
  assert.equal(preview.restoreAnchor(anchor, renamed), 0);

  // Renamed and a heading added: lost.
  const lost = layout([['H1', 'New'], ...doc.map(([tag, text]) => [tag, text === 'Setup' ? 'Install' : text])], 85);
  assert.equal(preview.restoreAnchor(anchor, lost), null);
  assert.deepEqual(preview.scrollAfterRender(false, anchor, lost, 85), { top: 85 });
});

test('restoreAnchor tells repeated headings apart', () => {
  const repeated = [['H2', 'Notes'], ['P', 'one'], ['H2', 'Notes'], ['P', 'two'], ['P', 'more']];
  const anchor = preview.captureAnchor(layout(repeated, 65), 65, false);
  assert.equal(anchor.heading.occurrence, 1);
  assert.equal(anchor.text, 'two');

  // The second "Notes" lost a paragraph above it; the first one is gone altogether.
  assert.equal(preview.restoreAnchor(anchor, layout(repeated.slice(2), 65)), -40);
});

test('restoreAnchor works without a heading and gives up on an empty page', () => {
  const plain = [['P', 'a'], ['P', 'b'], ['P', 'c']];
  const anchor = preview.captureAnchor(layout(plain, 25), 25, false);
  assert.equal(anchor.heading, null);

  assert.equal(preview.restoreAnchor(anchor, layout([['P', 'new'], ...plain], 25)), 20);
  assert.equal(preview.restoreAnchor(anchor, []), null);
});

test('scrollAfterRender goes to the top for a new file and stays put without an anchor', () => {
  assert.deepEqual(preview.scrollAfterRender(true, { any: 1 }, [], 300), { top: 0 });
  assert.deepEqual(preview.scrollAfterRender(false, null, [], 300), { top: 300 });
});

test('highlightCode highlights only languages highlight.js knows', () => {
  const blocks = [
    { className: 'language-js' },
    { className: 'hljs language-unknown' },
    { className: 'xlanguage-js' }
  ];
  const highlighted = [];
  const hljs = {
    getLanguage: (name) => (name === 'js' ? {} : undefined),
    highlightElement: (code) => highlighted.push(code)
  };
  const root = { querySelectorAll: (selector) => (selector === 'pre > code[class*="language-"]' ? blocks : []) };

  preview.highlightCode(root, hljs);
  preview.highlightCode(root, undefined); // highlight.js didn't load: plain code.

  assert.deepEqual(highlighted, [blocks[0]]);
});

test('inPageTarget handles only # links, decoding them safely', () => {
  assert.equal(preview.inPageTarget('#setup'), 'setup');
  assert.equal(preview.inPageTarget('#caf%C3%A9'), 'café');
  assert.equal(preview.inPageTarget('#100%'), '100%');
  assert.equal(preview.inPageTarget('#'), '');
  assert.equal(preview.inPageTarget('notes.md#setup'), null);
  assert.equal(preview.inPageTarget('https://example.com/'), null);
});

test('findTarget looks for an id, then a name', () => {
  const byId = { found: 'id' };
  const byName = { found: 'name' };
  const escape = (id) => id.replace(/[\\"]/g, '\\$&');
  const queries = [];
  const root = (results) => ({
    querySelector: (selector) => {
      queries.push(selector);
      return results[selector] ?? null;
    }
  });

  assert.equal(preview.findTarget(root({ '#a': byId }), 'a', escape), byId);
  assert.equal(preview.findTarget(root({ '[name="a"]': byName }), 'a', escape), byName);
  assert.equal(preview.findTarget(root({}), 'x"y', escape), null);
  assert.deepEqual(queries, ['#a', '#a', '[name="a"]', '#x\\"y', '[name="x\\"y"]']);
});
