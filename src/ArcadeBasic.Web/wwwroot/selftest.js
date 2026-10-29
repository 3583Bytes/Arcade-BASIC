// ?selftest=ide: use the editor the way a person would and post what happened to
// /results (for the local test server). Headless Firefox has no File System Access
// API, so this covers Open and Save's fallbacks: a downloaded file, here caught
// before it reaches the disk.
import { findPictures } from './pictures.js';

export async function ideSelftest({ editor, documents, pack, openAddress, $, audio }) {
  const pause = (ms = 300) => new Promise(r => setTimeout(r, ms));
  const view = editor.view;
  const result = {};
  const step = async (name, fn) => {
    document.querySelector('#views [data-view="code"]').click();   // (each step starts in the code, as a person would)
    try { result[name] = await fn(); } catch (e) { result[name] = { error: String(e), stack: e.stack }; }
  };
  const setText = text => view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: text }, selection: { anchor: text.length } });
  const type = text => view.dispatch({ ...view.state.replaceSelection(text), userEvent: 'input.type' });
  const lineAtCursor = () => view.state.doc.lineAt(view.state.selection.main.head).text;
  const dialog = $('dialog');
  async function answer(value, text) {               // answer the next question asked
    for (let i = 0; i < 100 && !dialog.open; i++) await pause(20);
    if (!dialog.open) throw new Error('no question was asked');
    const asked = dialog.querySelector('h2').textContent;
    if (text != null) dialog.querySelector('input').value = text;
    dialog.close(value);
    await pause(50);
    return asked;
  }
  const downloads = [];
  const click = HTMLAnchorElement.prototype.click;
  HTMLAnchorElement.prototype.click = function () {
    if (this.download) downloads.push({ name: this.download, href: this.href }); else click.call(this);
  };
  const downloaded = async d => (await fetch(d.href)).text();

  await step('startup', () => ({ tabs: documents.all.map(d => d.name), active: documents.active?.name, title: document.title, hash: location.hash }));

  await step('highlighting', async () => {
    documents.newProgram();
    setText('10 LET X = INT(RND * 6) ! a die\n20 IF X > 3 THEN 10\n30 PRINT "X is"; X\nREM done\n');
    await pause(300);
    const spans = [...view.contentDOM.querySelectorAll('.cm-line span')];
    const colourOf = word => { const s = spans.find(x => x.textContent === word); return s && getComputedStyle(s).color; };
    const css = name => getComputedStyle(document.documentElement).getPropertyValue(name).trim();
    return {
      spans: spans.length, colours: new Set(spans.map(s => getComputedStyle(s).color)).size,
      LET: colourOf('LET'), keyword: css('--syn-kw'), INT: colourOf('INT'), builtin: css('--syn-fn'),
      jumpTo: colourOf('10'), label: css('--syn-label'), comment: colourOf('! a die'), commentColour: css('--syn-com'),
      string: colourOf('"X is"'), stringColour: css('--syn-str'),
    };
  });

  await step('themes', () => {
    const select = $('theme'), out = {};
    for (const option of select.options) {
      select.value = option.value;
      select.dispatchEvent(new Event('change'));
      out[option.value] = { editor: getComputedStyle(view.dom).backgroundColor, page: getComputedStyle(document.body).backgroundColor };
    }
    out.saved = localStorage.getItem('arcade-basic.theme');
    select.value = 'midnight';
    select.dispatchEvent(new Event('change'));
    return out;
  });

  await step('errorAsYouType', async () => {
    documents.newProgram();
    setText('PRINT (1\n');
    await pause(800);
    return { underlined: [...document.querySelectorAll('.cm-lintRange-error')].map(e => e.textContent), status: $('problems').textContent };
  });

  await step('warningAsYouType', async () => {
    setText('PRINT SCORE\nEND\n');
    await pause(800);
    return { underlined: document.querySelectorAll('.cm-lintRange-warning').length, status: $('problems').textContent };
  });

  await step('problemsPanel', async () => {
    $('problems').click();                             // the problems, in the Console
    await pause(100);
    return { view: document.documentElement.dataset.view, badge: $('consoleBadge').textContent,
             items: [...document.querySelectorAll('#problemList li')].map(li => li.textContent) };
  });

  await step('runtimeError', async () => {
    setText('10 PRINT "hi"\n20 LET A = 1 / 0\n30 END\n');
    $('run').click();
    await pause(600);
    return { marked: document.querySelector('.cm-runtimeError')?.textContent, cursorLine: lineAtCursor(), output: $('output').textContent.trim(),
             view: document.documentElement.dataset.view, logged: document.querySelector('#log li:last-child .text')?.textContent };
  });

  await step('compileErrorOnRun', async () => {
    setText('PRINT "fine"\nPRINT (1\n');
    $('run').click();
    await pause(400);
    return { logged: document.querySelector('#log li:last-child .text')?.textContent.split('\n').slice(0, 2).join(' / '),
             view: document.documentElement.dataset.view, cursorLine: lineAtCursor() };
  });

  await step('completion', async () => {
    setText('LET SCORE = 1\n');
    type('PRI');
    await pause(400);
    const keywords = [...document.querySelectorAll('.cm-tooltip-autocomplete li')].map(li => li.textContent).slice(0, 4);
    setText('LET SCORE = 1\nPRINT S');
    type('C');
    await pause(400);
    const names = [...document.querySelectorAll('.cm-tooltip-autocomplete li')].map(li => li.textContent).slice(0, 4);
    setText('PRINT "PRI');
    type('N');
    await pause(400);
    return { keywords, names, insideString: document.querySelectorAll('.cm-tooltip-autocomplete li').length };
  });

  await step('goToDefinition', async () => {
    documents.openExample('maze3d.bas');
    const text = view.state.doc.toString(), at = text.indexOf('GOSUB 2000') + 7;
    view.dispatch({ selection: { anchor: at } });
    view.contentDOM.dispatchEvent(new KeyboardEvent('keydown', { key: 'F12', code: 'F12', bubbles: true, cancelable: true }));
    const lineNumber = lineAtCursor();
    documents.openExample('kanban.bas');
    const call = view.state.doc.toString().indexOf('& LANENAME$(') + 4;
    view.dispatch({ selection: { anchor: call } });
    view.contentDOM.dispatchEvent(new KeyboardEvent('keydown', { key: 'F12', code: 'F12', bubbles: true, cancelable: true }));
    return { lineNumber, func: lineAtCursor() };
  });

  await step('examplesMenu', async () => {
    const select = $('examples');
    select.value = 'snake.bas';
    select.dispatchEvent(new Event('change'));
    await pause(50);
    return { active: documents.active.name, hash: location.hash, groups: [...select.querySelectorAll('optgroup')].map(g => `${g.label} ${g.children.length}`) };
  });

  await step('closeWithChanges', async () => {
    const doc = documents.newProgram();
    type('PRINT 2\n');
    const dirty = doc.dirty;
    const closing = documents.close(doc);
    const asked = await answer('');                 // Cancel
    await closing;
    const keptAfterCancel = documents.all.includes(doc);
    const again = documents.close(doc);
    await answer('close');
    await again;
    return { dirty, asked, keptAfterCancel, closedAfterClose: !documents.all.includes(doc) };
  });

  await step('saveDownloads', async () => {
    const doc = documents.newProgram();
    setText('PRINT "mine"\n');
    const saving = documents.save();
    const asked = await answer('save', 'mine');
    const saved = await saving;
    const file = downloads.at(-1);
    return { asked, saved, name: file?.name, text: file && await downloaded(file), tab: doc.name, kind: doc.kind, dirty: doc.dirty, title: document.title };
  });

  await step('crlfKept', async () => {
    const doc = await documents.openFile(new File(['10 PRINT 1\r\n20 END\r\n'], 'windows.bas'));
    const inEditor = view.state.doc.toString();
    view.dispatch({ changes: { from: view.state.doc.length, insert: '30 REM edited\n' } });
    const dirty = doc.dirty;
    const before = downloads.length;
    await documents.save();
    const file = downloads[before];
    return { inEditorHasCR: inEditor.includes('\r'), dirty, name: file?.name, saved: JSON.stringify(file && await downloaded(file)), dirtyAfter: doc.dirty };
  });

  await step('sharedLink', async () => {
    const text = 'PRINT "from a link"\nEND\n';
    const code = await pack(text);
    await openAddress('#code=' + code);
    return { codeLength: code.length, active: documents.active.name, same: view.state.doc.toString() === text };
  });

  await step('views', async () => {
    // Running shows the Output view (its screen once it draws, what it prints); when it
    // ends the Console says so; Alt+1, 2, 3 choose a view.
    const root = document.documentElement;
    const alt = digit => window.dispatchEvent(new KeyboardEvent('keydown', { key: String(digit), code: `Digit${digit}`, altKey: true, bubbles: true, cancelable: true }));
    documents.newProgram();
    setText('SET WINDOW 0, 1, 0, 1\nGRAPH AREA: 0.2, 0.2; 0.8, 0.2; 0.5, 0.8\nPRINT "drawn"\nSLEEP 0.5\n');
    const before = { view: root.dataset.view, hint: $('hint').offsetParent !== null };
    $('run').click();
    await pause(200);
    const running = { view: root.dataset.view, running: root.classList.contains('running'), screen: $('screen').offsetParent !== null,
                      output: $('output').textContent.trim(), focus: document.activeElement?.id };
    await pause(700);
    const ended = { running: root.classList.contains('running'), view: root.dataset.view,
                    log: [...document.querySelectorAll('#log li .text')].slice(-2).map(e => e.textContent) };
    alt(3); const consoleView = root.dataset.view;
    alt(1); const codeView = root.dataset.view;
    alt(2); const outputView = root.dataset.view;
    return { before, running, ended, keys: [consoleView, codeView, outputView] };
  });

  await step('playLink', async () => {
    // A play link shows a Play button and no editor; playing draws and prints, and opens no tab;
    // "Open in the editor" goes back, with the program in a tab.
    const root = document.documentElement, tabs = documents.all.length;
    const text = 'SET WINDOW 0, 1, 0, 1\nGRAPH AREA: 0.1, 0.1; 0.9, 0.1; 0.5, 0.9\nPRINT "played"\n';
    await openAddress('#play=' + await pack(JSON.stringify({ main: ['triangle.bas', text], libraries: [] })));
    const before = { player: root.classList.contains('player'), button: $('playButton').textContent,
                     editorHidden: $('editorPane').offsetParent === null, title: document.title };
    $('playButton').click();
    for (let i = 0; i < 200 && !$('output').textContent.includes('played'); i++) await pause(10);
    await pause(50);
    const played = { drawn: root.classList.contains('drawn'), screenShown: $('screen').offsetParent !== null,
                     output: $('output').textContent.trim(), status: $('playerStatus').textContent,
                     tabsAdded: documents.all.length - tabs };
    $('playerEdit').click();
    await pause(50);
    return { ...before, ...played, backInEditor: !root.classList.contains('player'), opened: documents.active.name,
             same: view.state.doc.toString() === text };
  });

  await step('soundTiming', async () => {
    // How long until the program PRINTs "after": a foreground tune holds it up, background music doesn't.
    const timeTo = async program => {
      documents.newProgram();
      setText(program);
      const t0 = performance.now();
      $('run').click();
      for (let i = 0; i < 400 && !$('output').textContent.includes('after'); i++) await pause(10);
      return Math.round(performance.now() - t0);
    };
    return {
      foregroundPlay500: await timeTo('PLAY "T240 L8 CDEC"\nPRINT "after"\n'),             // 4 eighths at T240
      backgroundPlay0: await timeTo('PLAY "MB T240 L8 CDEC"\nPRINT "after"\n'),
      sound500: await timeTo('SOUND 440, 9.1\nPRINT "after"\n'),                          // 9.1 ticks of 18.2/s
      fullQueue470: await timeTo('FOR I = 1 TO 40\nPLAY "MB T255 L16 C"\nNEXT I\nPRINT "after"\n'),   // 8 of 40 notes must play first
      twoPlaysInARow1000: await timeTo('PLAY "T240 L8 CDEC"\nPLAY "T240 L8 CDEC"\nPRINT "after"\n'),
    };
  });

  await step('webAudio', async () => {
    // With autoplay allowed (the test profile's setting), Run's click starts audio:
    // count the square waves actually started for four notes and a rest.
    let started = 0;
    const start = OscillatorNode.prototype.start;
    OscillatorNode.prototype.start = function (...args) { started++; return start.apply(this, args); };
    documents.newProgram();
    setText('PLAY "T240 L8 C D P8 E C"\nPRINT "after"\n');
    $('run').click();
    for (let i = 0; i < 200 && !$('output').textContent.includes('after'); i++) await pause(10);
    OscillatorNode.prototype.start = start;
    return { state: audio.state, oscillators: started };
  });

  await step('debugger', async () => {
    documents.newProgram();
    setText(['LET T = 0', '! add them up', 'FOR I = 1 TO 3', '  LET T = T + I', 'NEXT I', 'CALL SHOW(T)', 'END',
             'SUB SHOW(X)', '  PRINT X', 'END SUB'].join('\n') + '\n');
    await pause(700);                                  // checked: it knows where statements are
    const at = n => view.state.doc.line(n).from;
    const press = (key, shiftKey = false) =>
      window.dispatchEvent(new KeyboardEvent('keydown', { key, code: key, shiftKey, bubbles: true, cancelable: true }));
    const pausedAt = async () => {                     // the next pause: its line and variables
      for (let i = 0; i < 200 && !document.querySelector('.cm-pausedLine') && !$('stop').disabled; i++) await pause(10);
      const line = document.querySelector('.cm-pausedLine');
      if (!line) return 'ended';
      const frames = [...document.querySelectorAll('#variables .frame')].map(f => {
        const vars = [...f.querySelectorAll(':scope > table tr')].map(tr =>
          `${tr.cells[0].textContent}=${tr.cells[1]?.textContent}${tr.className === 'changed' ? '*' : ''}`);
        return `${f.querySelector('summary').textContent}: ${vars.join(' ')}`;
      });
      return { line: line.textContent.trim(), frames };
    };
    const result = {};

    // F9 on the comment: the breakpoint goes on the next statement (line 3).
    view.dispatch({ selection: { anchor: at(2) } });
    view.contentDOM.dispatchEvent(new KeyboardEvent('keydown', { key: 'F9', code: 'F9', bubbles: true, cancelable: true }));
    // A click beside line 9 (in the SUB).
    const gutter = document.querySelector('.cm-breakpoints').getBoundingClientRect();
    const y = view.coordsAtPos(at(9)).top + 4;
    document.elementFromPoint(gutter.left + gutter.width / 2, y)
      ?.dispatchEvent(new MouseEvent('mousedown', { clientX: gutter.left + gutter.width / 2, clientY: y, button: 0, bubbles: true }));
    result.breakpoints = editor.breakpointLines();
    result.dots = document.querySelectorAll('.cm-bp').length;

    press('F5');
    result.first = await pausedAt();
    result.panel = $('variables').hidden ? 'output' : 'variables';
    result.view = document.documentElement.dataset.view;
    result.runButton = $('run').textContent;
    press('F10');
    result.stepOver = await pausedAt();
    press('F10');
    result.stepOverAgain = await pausedAt();
    press('F5');
    result.continued = await pausedAt();
    // Hold on to the breakpoints until the SUB.
    for (let i = 0; i < 6 && result.continued?.line?.startsWith?.('FOR'); i++) { press('F5'); result.continued = await pausedAt(); }
    while (result.continued !== 'ended' && !result.continued.line.startsWith('PRINT X') && Object.keys(result).length < 40) {
      press('F5');
      result.continued = await pausedAt();
    }
    // Point at X in the SUB.
    const x = view.state.doc.line(9).from + view.state.doc.line(9).text.indexOf('X');
    const where = view.coordsAtPos(x);
    view.contentDOM.dispatchEvent(new MouseEvent('mousemove', { clientX: where.left + 2, clientY: where.top + 4, bubbles: true }));
    await pause(500);
    result.hover = document.querySelector('.cm-valueTip')?.textContent;
    press('F11', true);                                 // step out
    result.stepOut = await pausedAt();
    press('F5');
    result.end = await pausedAt();
    result.output = $('output').textContent;
    press('F11');                                       // nothing running: start, stopped at the first line
    result.startPaused = await pausedAt();
    press('F5', true);                                  // stop
    await pause(50);
    result.stoppedClean = !document.querySelector('.cm-pausedLine') && $('run').textContent === '▶ Run';
    return result;
  });

  await step('pictureEditor', async () => {
    documents.openExample('maze3d-textures.bas');
    const result = { pictures: findPictures(view.state.doc).map(p => `${p.name || '(unnamed)'} ${p.width}x${p.height} @${p.firstLine}`) };
    const stone = findPictures(view.state.doc).find(p => p.name === 'stone');
    const colourLine = view.state.doc.lineAt(view.state.doc.toString().indexOf('DATA 0.10, 0.10, 0.16')).number;
    result.before = { row: view.state.doc.line(stone.firstLine).text, colours: view.state.doc.line(colourLine).text };
    editor.goToLine(stone.firstLine);
    await pause(150);
    const lens = [...document.querySelectorAll('.cm-pictureLens')].find(l => l.textContent.includes('stone'));
    lens?.querySelector('button').dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true }));
    await pause(150);
    const dialog = $('pictureDialog');
    result.open = dialog.open;
    result.title = $('pictureTitle').textContent;
    result.source = $('pictureSource').textContent;
    result.swatches = [...document.querySelectorAll('#pictureColours .swatch')].map(s =>
      `${s.textContent}=${s.querySelector('.chip').style.background || 'clear'}`);
    // Paint the top-left pixel with "3", then fill the 0 at (4, 0)'s area... with "3" too.
    [...document.querySelectorAll('#pictureColours .swatch')].find(s => s.textContent === '3').click();
    const canvas = $('pictureCanvas'), r = canvas.getBoundingClientRect(), z = r.width / stone.width;
    const at = (x, y) => ({ clientX: r.left + (x + 0.5) * z, clientY: r.top + (y + 0.5) * z, button: 0, buttons: 1, pointerId: 1, bubbles: true });
    canvas.dispatchEvent(new PointerEvent('pointerdown', at(0, 0)));
    canvas.dispatchEvent(new PointerEvent('pointermove', at(2, 0)));     // a stroke across three pixels
    canvas.dispatchEvent(new PointerEvent('pointerup', at(2, 0)));
    // Colour 0 becomes red.
    [...document.querySelectorAll('#pictureColours .swatch')].find(s => s.textContent === '0').click();
    $('pictureInk').click();
    $('pictureInk').value = '#ff0000';
    $('pictureInk').dispatchEvent(new Event('input'));
    $('pictureUndo').click();                                            // undo the colour ...
    $('pictureRedo').click();                                            // ... and redo it
    $('pictureApply').click();
    await pause(50);
    result.closed = !dialog.open;
    result.after = { row: view.state.doc.line(stone.firstLine).text, colours: view.state.doc.line(colourLine).text };
    // One undo in the editor takes it all back.
    const mac = /Mac/.test(navigator.platform);
    view.focus();
    view.contentDOM.dispatchEvent(new KeyboardEvent('keydown', { key: 'z', code: 'KeyZ', metaKey: mac, ctrlKey: !mac, bubbles: true, cancelable: true }));
    result.undone = view.state.doc.line(stone.firstLine).text === result.before.row && view.state.doc.line(colourLine).text === result.before.colours;

    // A new picture from the File menu: 8 x 4, draw a row of 1s with Fill, apply.
    documents.newProgram();
    setText('PRINT "pictures"\n');
    view.dispatch({ selection: { anchor: 0 } });
    document.querySelector('[data-action="picture"]').click();
    await answer('insert', 'ship 8 x 4');
    await pause(100);
    result.newTitle = $('pictureTitle').textContent;
    result.newSwatches = [...document.querySelectorAll('#pictureColours .swatch')].map(s => s.textContent);
    [...document.querySelectorAll('#pictureColours .swatch')].find(s => s.textContent === '1').click();
    $('pictureFill').click();
    const c2 = $('pictureCanvas').getBoundingClientRect(), z2 = c2.width / 8;
    $('pictureCanvas').dispatchEvent(new PointerEvent('pointerdown', { clientX: c2.left + z2 / 2, clientY: c2.top + z2 / 2, button: 0, buttons: 1, pointerId: 2, bubbles: true }));
    $('pictureApply').click();
    await pause(50);
    result.newText = view.state.doc.toString();
    return result;
  });

  await step('colours', async () => {
    documents.newProgram();
    setText('SET LINE COLOR 2\nSET COLOR MIX (40) 0.10, 0.50, 0.90\nSET AREA COLOR 40\n');
    await pause(150);
    const swatches = () => [...view.contentDOM.querySelectorAll('.cm-swatch')];
    const result = { swatches: swatches().map(s => getComputedStyle(s).backgroundColor) };
    // Colour 2 → pick 9 from the palette.
    swatches()[0].dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true }));
    await pause(50);
    result.paletteOpen = $('paletteDialog').open;
    result.paletteCells = $('paletteGrid').children.length;
    result.paletteSelected = $('paletteGrid').querySelector('.selected')?.dataset.index;
    $('paletteGrid').children[9].click();
    $('paletteUse').click();
    await pause(50);
    result.line1 = view.state.doc.line(1).text;
    // The mix's swatch → the colour picker → red.
    swatches()[1].dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true }));
    const picker = document.querySelector('.swatchPicker');
    picker.value = '#ff0000';
    picker.dispatchEvent(new Event('input'));
    await pause(50);
    result.line2 = view.state.doc.line(2).text;
    result.mixSwatchNow = getComputedStyle(swatches()[2]).backgroundColor;   // colour 40 follows the mix
    // Tools → Colours…, then insert a SET COLOR MIX for colour 200.
    view.dispatch({ selection: { anchor: view.state.doc.length } });
    document.querySelector('[data-action="palette"]').click();
    await pause(50);
    result.toolsSource = $('paletteSource').textContent.slice(0, 40);
    $('paletteGrid').children[200].click();
    $('paletteInsert').click();
    result.inserted = view.state.doc.toString().split('\n').find(l => l.includes('(200)'));
    // maze3d's colour DATA (in its textures library): a swatch per colour.
    documents.openExample('maze3d-textures.bas');
    editor.goToLine(view.state.doc.lineAt(view.state.doc.toString().indexOf('DATA 0.10, 0.10, 0.16')).number);
    await pause(150);
    const line92 = [...view.contentDOM.querySelectorAll('.cm-line')].find(l => l.textContent.trim().startsWith('DATA 0.10, 0.10, 0.16'));
    result.dataSwatches = line92 ? line92.querySelectorAll('.cm-swatch').length : 'no line';
    return result;
  });

  await step('tunes', async () => {
    documents.newProgram();
    setText('PLAY "T240 L8 CDEC"\nPLAY "MB T255 L32 N" & STR$(5)\nEND ! PLAY "C" here is in a comment\n');
    await pause(150);
    const result = { buttons: view.contentDOM.querySelectorAll('.cm-tune').length };
    let started = 0;
    const start = OscillatorNode.prototype.start;
    OscillatorNode.prototype.start = function (...args) { started++; return start.apply(this, args); };
    view.contentDOM.querySelector('.cm-tune button').dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true }));
    await pause(200);
    result.previewOscillators = started;
    OscillatorNode.prototype.start = start;
    // ♫: the tune editor.
    view.contentDOM.querySelectorAll('.cm-tune button')[1].dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true }));
    await pause(100);
    result.open = $('tuneDialog').open;
    result.text = $('tuneText').value;
    result.summary = $('tuneLength').textContent;
    const keys = [...$('tunePiano').children];
    keys.find(k => k.title === 'G4').click();                     // L8 in force, new notes quarters: G4
    $('tuneDialog').focus();
    $('tuneDialog').dispatchEvent(new KeyboardEvent('keydown', { key: 'k', bubbles: true, cancelable: true }));   // C5
    $('tuneRest').click();
    $('tuneTempo').value = '120';
    $('tuneTempo').dispatchEvent(new Event('change'));
    result.edited = $('tuneText').value;
    result.editedSummary = $('tuneLength').textContent;
    $('tuneText').value = 'T240 X';
    $('tuneText').dispatchEvent(new Event('input'));
    result.error = $('tuneError').textContent;
    $('tuneText').value = result.edited;
    $('tuneText').dispatchEvent(new Event('input'));
    $('tuneApply').click();
    await pause(50);
    result.line1 = view.state.doc.line(1).text;
    // Tools → Insert a tune.
    view.dispatch({ selection: { anchor: view.state.doc.line(3).from } });
    document.querySelector('[data-action="tune"]').click();
    await pause(100);
    result.newTune = $('tuneDialog').open && $('tuneText').value;
    $('tuneApply').click();
    result.line3 = view.state.doc.line(3).text;
    return result;
  });

  await step('outline', async () => {
    documents.openExample('maze3d.bas');
    if ($('outline').hidden) $('outlineToggle').click();
    await pause(100);
    const items = [...document.querySelectorAll('#outline .item')];
    const result = {
      count: items.length,
      first: items.slice(0, 8).map(b => `${b.querySelector('i').textContent} ${b.textContent.slice(1)}`),
      kinds: [...new Set(items.map(b => b.className.split(' ')[1]))],
    };
    items.find(b => b.textContent.includes('RENDER'))?.click();
    await pause(50);
    result.clicked = view.state.doc.lineAt(view.state.selection.main.head).text;
    result.current = document.querySelector('#outline .item.current')?.textContent;
    editor.foldAll();
    await pause(50);
    result.foldedPlaceholders = view.dom.querySelectorAll('.cm-foldPlaceholder').length;
    result.visibleLinesFolded = view.dom.querySelectorAll('.cm-line').length;
    editor.unfoldAll();
    await pause(50);
    result.unfolded = view.dom.querySelectorAll('.cm-foldPlaceholder').length;
    // Go to (Mod+Shift+O): by name, then by line.
    const mac = /Mac/.test(navigator.platform);
    const goTo = text => {
      window.dispatchEvent(new KeyboardEvent('keydown', { key: 'O', code: 'KeyO', shiftKey: true, metaKey: mac, ctrlKey: !mac, bubbles: true, cancelable: true }));
      const input = $('goToDialog').querySelector('input');
      input.value = text;
      input.dispatchEvent(new Event('input'));
      input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
      return view.state.doc.lineAt(view.state.selection.main.head);
    };
    result.goToName = goTo('door').text;
    result.goToLine = goTo('400').number;
    // A classic program: its GOSUB targets are its outline.
    documents.newProgram();
    setText('10 GOSUB 100\n20 GOSUB 200\n30 END\n100 REM draw the board\n110 RETURN\n200 PRINT "score"\n210 RETURN\n');
    await pause(400);
    result.classic = [...document.querySelectorAll('#outline .item')].map(b => b.textContent.slice(1));
    return result;
  });

  await step('projects', async () => {
    const result = {};
    const press = (key, shiftKey = false) =>
      window.dispatchEvent(new KeyboardEvent('keydown', { key, code: key, shiftKey, bubbles: true, cancelable: true }));
    const waitFor = async test => { for (let i = 0; i < 200 && !test(); i++) await pause(10); return test(); };
    // A breakpoint in maze3d's textures library, hit as the module starts up — F5 from its tab runs the game.
    const textures = documents.openExample('maze3d-textures.bas');
    const mixLine = view.state.doc.lineAt(view.state.doc.toString().search(/^\s*SET COLOR MIX/m)).number;
    view.dispatch({ selection: { anchor: view.state.doc.line(mixLine).from } });
    view.contentDOM.dispatchEvent(new KeyboardEvent('keydown', { key: 'F9', code: 'F9', bubbles: true, cancelable: true }));
    press('F5');
    await waitFor(() => document.querySelector('.cm-pausedLine'));
    result.ranProgram = documents.all.find(d => d.name === 'maze3d.bas') && document.querySelector('.tab .mark')?.textContent;
    result.pausedIn = documents.active.name;
    result.pausedLine = document.querySelector('.cm-pausedLine')?.textContent.trim();
    result.frame = document.querySelector('#variables .frame summary')?.textContent;
    result.status = $('stats').textContent.split(' · ')[0];
    press('F5', true);                                   // stop
    view.dispatch({ selection: { anchor: view.state.doc.line(mixLine).from } });
    view.contentDOM.dispatchEvent(new KeyboardEvent('keydown', { key: 'F9', code: 'F9', bubbles: true, cancelable: true }));
    // A program and a library of its own, in two tabs.
    documents.openShared('SUB HI\n  PRINT "hi from the helper"\nEND SUB\nSUB OOPS\n  PRINT 1 / 0\nEND SUB\n', 'helper.bas', false);
    documents.openShared('LIBRARY "helper.bas"\nCALL HI\n', 'proj.bas');
    $('run').click();
    await waitFor(() => $('stop').disabled && $('output').textContent);
    result.output = $('output').textContent.trim();
    // A mistake in the library shows on the LIBRARY line.
    const helper = documents.all.find(d => d.name === 'helper.bas');
    helper.state = helper.state.update({ changes: { from: 0, insert: 'PRINT (\n' } }).state;
    setText('LIBRARY "helper.bas"\nCALL HI\n ');
    await pause(700);
    result.libraryProblem = document.querySelector('.cm-lintRange-error')?.textContent;
    result.libraryProblemText = [...document.querySelectorAll('#problemList li')].map(li => li.textContent)[0];
    helper.state = helper.state.update({ changes: { from: 0, to: 8 } }).state;
    // A runtime error in the library: its tab, its line.
    setText('LIBRARY "helper.bas"\nCALL OOPS\n');
    $('run').click();
    await waitFor(() => $('stop').disabled && document.querySelector('.cm-runtimeError'));
    result.errorTab = documents.active.name;
    result.errorLine = document.querySelector('.cm-runtimeError')?.textContent.trim();
    result.errorText = document.querySelector('#log li:last-child .text')?.textContent;   // (errors go to the Console)
    // A shared link carries a program's own libraries.
    const code = await pack(JSON.stringify({ main: ['linked.bas', 'LIBRARY "lib2.bas"\nCALL TWO\n'], libraries: [['lib2.bas', 'SUB TWO\n  PRINT "two"\nEND SUB\n']] }));
    await openAddress('#code=' + code);
    result.linkTabs = documents.all.filter(d => ['linked.bas', 'lib2.bas'].includes(d.name)).map(d => d.name);
    $('run').click();
    await waitFor(() => $('stop').disabled && $('output').textContent);
    result.linkOutput = $('output').textContent.trim();
    return result;
  });

  await step('keptInBrowser', async () => {
    documents.persist();
    const session = JSON.parse(localStorage.getItem('arcade-basic.session'));
    return { tabs: session.docs.map(d => `${d.name}${d.saved != null ? ' (changed)' : ''}`), active: session.docs.find(d => d.id === session.active)?.name };
  });

  HTMLAnchorElement.prototype.click = click;
  await fetch('/results', { method: 'POST', body: JSON.stringify(result, null, 2) });
}
