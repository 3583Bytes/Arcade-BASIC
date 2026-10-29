// The program's screen: CanvasGraphicsDevice's frames drawn on a canvas, and the
// keyboard and pointer passed on to the program. Shared by the IDE (main.js) and the
// player (src/ArcadeBasic.Player), so a program looks and plays the same in both.
//
// The canvas is drawn at the screen's real pixel density (fit), and straight lines are
// snapped to the pixel grid, so every line is crisp and the same weight.

// CanvasGraphicsDevice command codes.
const COLOR = 2, LINES = 3, AREA = 4, POINTS = 5, TEXT = 6, LINE_STYLE = 7, POINT_STYLE = 8, CELLS = 9, PALETTE = 10;
// SET LINE STYLE 1 solid, 2 dashed, 3 dotted — the SVG backend's dash patterns.
const DASHES = { 2: [12, 6], 3: [3, 5] };

/** A screen on `canvas`; `glow` says whether to draw with a CRT-style glow. */
export function createScreen(canvas, { glow = () => false } = {}) {
  const ctx = canvas.getContext('2d');
  // The colours (GfxPalette): 256 of them, which SET COLOR MIX can change. A frame
  // brings the whole palette with it whenever it has changed — always the first.
  let palette = Array(256).fill('#ffffff');   // CSS colours
  const paletteAbgr = new Uint32Array(256);   // the same, as ImageData pixels (little-endian ABGR)
  let paletteSeen = false;

  let lastFrame = null;   // [cmds, texts, cells] — redrawn when the canvas is resized or glow toggled
  let lastTexts = [];     // the last drawn frame's text: [{ x, y, text }] in unit coordinates

  /** Size the canvas's backing store to its on-screen size in device pixels. */
  function fitCanvas() {
    const dpr = window.devicePixelRatio || 1;
    const w = Math.max(1, Math.round(canvas.clientWidth * dpr)), h = Math.max(1, Math.round(canvas.clientHeight * dpr));
    if (canvas.width === w && canvas.height === h) return;
    canvas.width = w;
    canvas.height = h;
    if (lastFrame) drawFrame(...lastFrame);
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
    // so size the font to fit 80 columns.
    ctx.font = `${Math.round(w / 80 / 0.6)}px ui-monospace, Menlo, monospace`;
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
      if (op === LINE_STYLE) { lineStyle = cmds[i++]; continue; }
      if (op === POINT_STYLE) { pointStyle = cmds[i++]; continue; }
      if (op === TEXT) {
        paint(colors[2]);                             // the point is the text's baseline (ECMA-116 LEFT, BASE)
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
      for (let k = 0; k < n; k++, i += 2) {
        let x = X(cmds[i]), y = Y(cmds[i + 1]);
        if (op === LINES) { x = snap(x); y = snap(y); }
        else if (op === AREA) { x = Math.round(x); y = Math.round(y); }
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
      const c = cmds[at + k] >>> 0, r = (c >> 16) & 255, g = (c >> 8) & 255, b = c & 255;
      palette.push(`rgb(${r},${g},${b})`);
      paletteAbgr[k] = (0xff000000 | (b << 16) | (g << 8) | r) >>> 0;
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
    /** Draw the last frame again (the glow changed). */
    redraw() { if (lastFrame) drawFrame(...lastFrame); },
    /** A new program: its colours aren't known until it draws. */
    newRun() { paletteSeen = false; },
    /** The program's colours ('#rrggbb' × 256) once it has drawn, else null. */
    colours: () => paletteSeen
      ? palette.map(c => '#' + c.match(/\d+/g).map(v => (+v).toString(16).padStart(2, '0')).join(''))
      : null,
    /** The last frame's text: [{ x, y, text }] in unit coordinates. */
    get texts() { return lastTexts; },
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

  // Capturing the pointer keeps a drag's moves and release coming even if it leaves the canvas.
  canvas.addEventListener('pointermove', e => program()?.PointerMove(...screen.unitPoint(e)));
  canvas.addEventListener('pointerdown', e => {
    if (e.button !== 0 || !program()) return;
    audio.unlock();
    try { canvas.setPointerCapture(e.pointerId); } catch { /* not a live pointer (synthetic) */ }
    canvas.focus();
    program().PointerDown(...screen.unitPoint(e));
    e.preventDefault();
    onPress();
  });
  canvas.addEventListener('pointerup', e => { if (e.button === 0) program()?.PointerUp(...screen.unitPoint(e)); });
  canvas.addEventListener('pointercancel', () => program()?.PointerCancel());
  canvas.addEventListener('lostpointercapture', () => program()?.PointerCancel());
}
