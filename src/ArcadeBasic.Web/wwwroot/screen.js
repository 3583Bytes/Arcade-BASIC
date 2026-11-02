// The program's screen: CanvasGraphicsDevice's frames drawn on a canvas, and the
// keyboard and pointer passed on to the program. Shared by the IDE (main.js) and the
// player (src/ArcadeBasic.Player), so a program looks and plays the same in both.
//
// The canvas is drawn at the screen's real pixel density (fit), and straight lines are
// snapped to the pixel grid, so every line is crisp and the same weight.

// CanvasGraphicsDevice command codes.
const COLOR = 2, LINES = 3, AREA = 4, POINTS = 5, TEXT = 6, LINE_STYLE = 7, POINT_STYLE = 8, CELLS = 9, PALETTE = 10,
  TEXT_STYLE = 11;
// SET LINE STYLE 1 solid, 2 dashed, 3 dotted — the SVG backend's dash patterns.
const DASHES = { 2: [12, 6], 3: [3, 5] };

// SET TEXT FONT's fonts: a TEXT_STYLE's font is 1 for sans-serif (else monospace), + 2 for bold.
const MONO = 'ui-monospace, Menlo, Consolas, monospace';
const SANS = 'system-ui, -apple-system, "Segoe UI", Roboto, Helvetica, Arial, sans-serif';
const fontOf = (font, px) => `${font & 2 ? '600 ' : ''}${px}px ${font & 1 ? SANS : MONO}`;

/** A screen on `canvas`; `glow` says whether to draw with a CRT-style glow. */
export function createScreen(canvas, { glow = () => false } = {}) {
  const ctx = canvas.getContext('2d');
  // The colours (GfxPalette): 256 of them, which SET COLOR MIX can change. A frame
  // brings the whole palette with it whenever it has changed — always the first.
  let palette = Array(256).fill('#ffffff');   // CSS colours
  const paletteAbgr = new Uint32Array(256);   // the same, as ImageData pixels (little-endian ABGR)
  let paletteSeen = false;

  let lastFrame = null;   // [cmds, texts, cells] — redrawn when the canvas is resized or glow toggled
  const measuring = document.createElement('canvas').getContext('2d');
  let measured = new Map();   // TEXTWIDTH's answers at this size: font and text → width
  let lastTexts = [];     // the last drawn frame's text: [{ x, y, text }] in unit coordinates

  /** Size the canvas's backing store to its on-screen size in device pixels. */
  function fitCanvas() {
    const dpr = window.devicePixelRatio || 1;
    const w = Math.max(1, Math.round(canvas.clientWidth * dpr)), h = Math.max(1, Math.round(canvas.clientHeight * dpr));
    if (canvas.width === w && canvas.height === h) return;
    canvas.width = w;
    canvas.height = h;
    measured = new Map();
    if (lastFrame) drawFrame(...lastFrame);
  }

  /** The pixel size drawFrame gives text of `height` (a fraction of the screen's height;
   *  0, the usual size) — exactly: TEXTWIDTH measures it so, and a rounded one draws wider. */
  function textPx(height, w, h) {
    return height > 0 ? Math.max(1, height * h).toFixed(2) : Math.round(w / 80 / 0.6);
  }

  /** How wide `text` is drawn in `font` (TEXT_STYLE's) at `height`, as a fraction of the
   *  screen's width: measured at the size it's drawn, since a font can be shaped
   *  differently small (macOS's is wider). Before the screen shows, as if it were 960 across. */
  function measureText(text, font, height) {
    let { w, h } = geometry();
    if (w < 16 || h < 16) [w, h] = [960, 576];
    const px = textPx(height, w, h), key = `${font} ${px} ${text}`;
    let width = measured.get(key);
    if (width === undefined) {
      if (measured.size > 8192) measured = new Map();
      measuring.font = fontOf(font, px);
      measured.set(key, width = measuring.measureText(text).width);
    }
    return width / w;
  }

  /** Canvas geometry for the current backing store: the unit square, inset by half a
   *  line width so lines on its edges aren't clipped. */
  function geometry() {
    const dpr = window.devicePixelRatio || 1;
    const W = canvas.width, H = canvas.height;
    const lineWidth = Math.max(1, Math.round(1.5 * dpr));
    const inset = Math.ceil(lineWidth / 2);
    return { W, H, dpr, lineWidth, inset, w: W - 2 * inset, h: H - 2 * inset };
  }

  /** Draw a frame; whether it drew anything (more than colours and styles). */
  function drawFrame(cmds, texts, cells = []) {
    lastFrame = [cmds, texts, cells];
    let i = 0, drew = false;
    if (cmds[0] === PALETTE) { setPalette(cmds, 1); i = 1 + 256; }
    const { W, H, dpr, lineWidth, inset, w, h } = geometry();
    const X = x => inset + x * w, Y = y => inset + (1 - y) * h;   // unit square, origin bottom-left
    // Snap to the pixel grid: an odd-width line's centre on a pixel centre, an even one's on an edge.
    const half = lineWidth % 2 ? 0.5 : 0;
    const snap = v => Math.round(v - half) + half;
    const glowing = glow();
    const colors = [1, 1, 1, 1];                      // point, line, text, area
    let lineStyle = 1, pointStyle = 3;                // GraphicsState's defaults
    lastTexts = [];
    ctx.shadowBlur = 0;
    ctx.fillStyle = palette[0];                       // colour 0 is the background
    ctx.fillRect(0, 0, W, H);
    ctx.imageSmoothingEnabled = false;                // cells stay crisp squares when scaled up
    let atlas = null;                                 // this frame's cell arrays, uploaded on first use
    ctx.lineWidth = lineWidth;
    ctx.lineJoin = 'miter';
    ctx.lineCap = 'square';
    // An 80 x 24-cell screen, like the terminal's: a monospace glyph is ~0.6 em wide,
    // so the usual font fits 80 columns. SET TEXT HEIGHT gives a size of the screen's height.
    let textHeight = 0, textFont = 0, fontSet = '';
    ctx.textBaseline = 'alphabetic';
    const paint = index => {
      const c = palette[index] ?? palette[1];
      ctx.strokeStyle = ctx.fillStyle = c;
      if (glowing) { ctx.shadowColor = c; ctx.shadowBlur = 6 * dpr; }
    };
    while (i < cmds.length) {
      const op = cmds[i++];
      if (op === COLOR) { colors[cmds[i]] = cmds[i + 1]; i += 2; continue; }
      if (op === CELLS) {
        // MAT GRAPH CELLS: its block of the atlas, scaled into the rectangle (edges on
        // whole pixels, so neighbouring arrays meet without seams), cut to what's visible.
        const [l, b, r, t, cols, rows, vl, vb, vr, vt, ax, ay] = cmds.slice(i, i + 12);
        i += 12;
        if (!atlas) atlas = uploadAtlas(cells);
        drew = true;
        const x0 = Math.round(X(l)), x1 = Math.round(X(r)), y0 = Math.round(Y(t)), y1 = Math.round(Y(b));
        const cx0 = Math.round(X(vl)), cx1 = Math.round(X(vr)), cy0 = Math.round(Y(vt)), cy1 = Math.round(Y(vb));
        const cut = cx0 > x0 || cx1 < x1 || cy0 > y0 || cy1 < y1;
        ctx.shadowBlur = 0;
        if (cut) { ctx.save(); ctx.beginPath(); ctx.rect(cx0, cy0, cx1 - cx0, cy1 - cy0); ctx.clip(); }
        ctx.drawImage(atlas, ax, ay, cols, rows, x0, y0, x1 - x0, y1 - y0);
        if (cut) ctx.restore();
        continue;
      }
      if (op === TEXT_STYLE) { textHeight = cmds[i]; textFont = cmds[i + 1]; i += 2; continue; }
      if (op === LINE_STYLE) { lineStyle = cmds[i++]; continue; }
      if (op === POINT_STYLE) { pointStyle = cmds[i++]; continue; }
      if (op === TEXT) {
        paint(colors[2]);                             // the point is the text's baseline (GraphicsState justifies it)
        const font = fontOf(textFont, textPx(textHeight, w, h));
        if (font !== fontSet) ctx.font = fontSet = font;
        ctx.fillText(texts[cmds[i + 2]], X(cmds[i]), Y(cmds[i + 1]));
        lastTexts.push({ x: cmds[i], y: cmds[i + 1], text: texts[cmds[i + 2]] });
        i += 3;
        drew = true;
        continue;
      }
      const n = cmds[i++];
      drew = true;
      paint(colors[op === POINTS ? 0 : op === LINES ? 1 : 3]);
      ctx.setLineDash(op === LINES ? (DASHES[lineStyle] ?? []).map(d => d * dpr) : []);
      ctx.beginPath();
      const at = i;
      const near = (k, d) => {                        // is the edge from point k to its neighbour (d = -1, +1) across or down?
        const j = op === AREA ? (k + d + n) % n : k + d;
        return j >= 0 && j < n && j !== k
          && (Math.abs(cmds[at + 2 * j] - cmds[at + 2 * k]) < 1e-9 || Math.abs(cmds[at + 2 * j + 1] - cmds[at + 2 * k + 1]) < 1e-9);
      };
      for (let k = 0; k < n; k++, i += 2) {
        let x = X(cmds[i]), y = Y(cmds[i + 1]);
        // A straight line, a box: on the pixel grid, crisp. A curve (GRAPH AREA, ROUND's corners)
        // is left where it is, so it's smooth.
        if (op !== POINTS && (n <= 4 || near(k, -1) || near(k, 1))) {
          if (op === LINES) { x = snap(x); y = snap(y); }
          else { x = Math.round(x); y = Math.round(y); }
        }
        if (op === POINTS) marker(x, y, pointStyle, dpr);
        else if (k === 0) ctx.moveTo(x, y);
        else ctx.lineTo(x, y);
      }
      if (op === LINES || (op === POINTS && pointStyle !== 1)) ctx.stroke();
      else { if (op === AREA) ctx.closePath(); ctx.fill(); }
    }
    return drew;
  }

  /** The colours a PALETTE command carries: 256 ARGB values from cmds[at]. */
  function setPalette(cmds, at) {
    paletteSeen = true;
    palette = [];
    for (let k = 0; k < 256; k++) {
      const c = cmds[at + k] >>> 0, a = c >>> 24, r = (c >> 16) & 255, g = (c >> 8) & 255, b = c & 255;
      palette.push(a === 255 ? `rgb(${r},${g},${b})` : `rgba(${r},${g},${b},${(a / 255).toFixed(3)})`);   // (SET COLOR MIX's opacity)
      paletteAbgr[k] = ((a << 24) | (b << 16) | (g << 8) | r) >>> 0;
    }
  }

  /** Put a frame's cell atlas (width, height, then colour indices row by row) into an
   *  image, in one upload, for drawImage to scale pieces of. Colour -1 is see-through
   *  (a transparent pixel), so a cell array can be a sprite. */
  const atlasCanvas = document.createElement('canvas'), atlasCtx = atlasCanvas.getContext('2d');
  function uploadAtlas(cells) {
    const w = cells[0], h = cells[1];
    if (atlasCanvas.width !== w || atlasCanvas.height !== h) { atlasCanvas.width = w; atlasCanvas.height = h; }
    const image = atlasCtx.createImageData(w, h), pixels = new Uint32Array(image.data.buffer);
    for (let k = 0; k < w * h; k++) {
      const c = cells[2 + k];
      pixels[k] = c < 0 ? 0 : paletteAbgr[c] ?? paletteAbgr[1];
    }
    atlasCtx.putImageData(image, 0, 0);
    return atlasCanvas;
  }

  // SET POINT STYLE 1 dot, 2 plus, 3 asterisk (adds to the current path).
  function marker(x, y, style, dpr) {
    const r = 4 * dpr;
    if (style === 1) { const d = 2.5 * dpr; ctx.moveTo(x + d, y); ctx.arc(x, y, d, 0, 2 * Math.PI); return; }
    ctx.moveTo(x - r, y); ctx.lineTo(x + r, y);
    ctx.moveTo(x, y - r); ctx.lineTo(x, y + r);
    if (style === 3) {
      const d = r * 0.7;
      ctx.moveTo(x - d, y - d); ctx.lineTo(x + d, y + d);
      ctx.moveTo(x - d, y + d); ctx.lineTo(x + d, y - d);
    }
  }

  new ResizeObserver(fitCanvas).observe(canvas);
  fitCanvas();

  return {
    /** Draw a frame (TakeFrame, FrameText, FrameCells); whether it drew anything. */
    draw: drawFrame,
    /** TEXTWIDTH, for CanvasGraphicsDevice (the page gives it to .NET with setModuleImports). */
    measureText,
    /** Draw the last frame again (the glow changed). */
    redraw() { if (lastFrame) drawFrame(...lastFrame); },
    /** A new program: its colours aren't known until it draws. */
    newRun() { paletteSeen = false; },
    /** The program's colours ('#rrggbb' × 256) once it has drawn, else null. */
    colours: () => paletteSeen
      ? palette.map(c => '#' + c.match(/\d+/g).slice(0, 3).map(v => (+v).toString(16).padStart(2, '0')).join(''))
      : null,
    /** The last frame's text: [{ x, y, text }] in unit coordinates. */
    get texts() { return lastTexts; },
    /** The last frame drawn at width × height on a canvas of its own (File → Export's icon,
     *  even while the screen is hidden); null before one. */
    picture(width, height) {
      if (!lastFrame) return null;
      const [w, h] = [canvas.width, canvas.height];
      canvas.width = width;
      canvas.height = height;
      drawFrame(...lastFrame);
      const copy = document.createElement('canvas');
      copy.width = width;
      copy.height = height;
      copy.getContext('2d').drawImage(canvas, 0, 0);
      canvas.width = w;
      canvas.height = h;
      drawFrame(...lastFrame);
      return copy;
    },
    geometry,
    /** A pointer event's position in the unit square (origin bottom-left). */
    unitPoint(e) {
      const r = canvas.getBoundingClientRect(), { inset, w, h } = geometry();
      const px = (e.clientX - r.left - canvas.clientLeft) * canvas.width / canvas.clientWidth;
      const py = (e.clientY - r.top - canvas.clientTop) * canvas.height / canvas.clientHeight;
      return [(px - inset) / w, 1 - (py - inset) / h];
    },
  };
}

/** Pass keys (from `keys`: the canvas, or the whole window) and the pointer over the
 *  canvas on to the program (`program()`: the .NET side, once loaded). `wanted(e)` can
 *  keep a key from it; `onPress` hears each press of the pointer. */
export function connectInput(screen, canvas, { keys = canvas, program, audio, wanted = () => true, onPress = () => {} }) {
  keys.addEventListener('keydown', e => {
    audio.unlock();                       // (a key press: the browser now allows sound)
    if (e.ctrlKey || e.metaKey || e.altKey || !wanted(e) || !program()) return;
    program().KeyDown(e.code, e.key, e.repeat);
    e.preventDefault();
  });
  keys.addEventListener('keyup', e => program()?.KeyUp(e.code));
  keys.addEventListener('blur', () => program()?.ReleaseKeys());

  // The buttons go to the program as e.buttons has them: 1 the primary, 2 the secondary, 4
  // the middle (e.button numbers them 0, 2, 1). The first press is a pointerdown, another
  // while it's held a pointermove with e.button set (and a button still down: letting go of
  // the last is a pointerup). A Mac's Ctrl+click is a secondary click; the canvas has no
  // menu of its own. Capturing the pointer keeps a drag's moves and release coming even if
  // it leaves the canvas.
  const bit = [1, 4, 2];
  const mac = /Mac/.test(navigator.platform);
  canvas.addEventListener('pointermove', e => {
    if (!program()) return;
    program().PointerMove(...screen.unitPoint(e));
    if (e.button >= 0 && e.buttons) program().PointerButtons(...screen.unitPoint(e), e.buttons & 7);
  });
  canvas.addEventListener('pointerdown', e => {
    if (!program() || bit[e.button] === undefined) return;
    audio.unlock();
    try { canvas.setPointerCapture(e.pointerId); } catch { /* not a live pointer (synthetic) */ }
    canvas.focus();
    const buttons = mac && e.ctrlKey && e.button === 0 ? 2 : (e.buttons & 7) | bit[e.button];
    program().PointerButtons(...screen.unitPoint(e), buttons);
    e.preventDefault();
    if (buttons & 1) onPress();
  });
  canvas.addEventListener('pointerup', e => program()?.PointerButtons(...screen.unitPoint(e), 0));
  canvas.addEventListener('pointercancel', () => program()?.PointerCancel());
  canvas.addEventListener('lostpointercapture', () => program()?.PointerCancel());
  canvas.addEventListener('contextmenu', e => { if (program()) e.preventDefault(); });
}
