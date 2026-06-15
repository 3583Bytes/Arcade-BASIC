# Design proposal: audio extension (`SOUND` / `BEEP` / `PLAY`)

> **Status: PROPOSAL — not yet implemented.** This document is the spec we would
> build to, and the source of the text that will land in `docs/conformance.md` and
> the READMEs once implemented. Reviewing this doc is the gate before any code.
>
> **Target: the full GW-BASIC/QuickBASIC audio feature set** — `SOUND`, `BEEP`,
> and the complete `PLAY` MML including background music (`MB`) and substring
> execution (`X`). It's delivered in the reviewable phases in §6 so each step
> ships usable, tested value; nothing is permanently cut.

## 1. Scope and source dialect

ISO/IEC 10279:1991 and ECMA-116 (Full BASIC) define **no** audio facility — this
was confirmed against the spec text (`specs/ECMA-116.txt` has no `SOUND`/`PLAY`/
`BEEP`/`TONE`). The §14 real-time module is process control, not sound, and ECMA
omits it.

Audio is therefore an **extension**, and per project policy it must track a real,
documented dialect faithfully and cite it — the same treatment `INKEY$`/`SLEEP`
already get. The canonical source is **Microsoft BASIC — GW-BASIC / QuickBASIC**
(IBM-PC lineage), the same dialect `INKEY$`/`SLEEP` cite.

Three statements are in scope, all reserved-or-extendable in the lexer today
(`SOUND` is already a reserved keyword at `Keywords.cs:168` but unimplemented):

| Statement | Source | One-liner |
|---|---|---|
| `SOUND freq, duration` | GW-BASIC / QuickBASIC | Emit a tone at `freq` Hz for `duration` clock ticks |
| `BEEP` | GW-BASIC / QuickBASIC | Fixed ~800 Hz tone for ~0.25 s (≡ `PRINT CHR$(7)`) |
| `PLAY "<MML>"` | GW-BASIC / QuickBASIC | Play a Music Macro Language string |

## 2. `SOUND` — exact semantics (GW-BASIC)

```
SOUND frequency, duration
```

- **frequency**: integer Hz, range **37–32767** (per the GW-BASIC User's Guide).
  Out-of-range raises a catchable runtime exception (consistent with our other
  range errors).
- **duration**: numeric, range **0–65535**, measured in **clock ticks of 18.2
  ticks/second** — so `seconds = duration / 18.2`.
- **Boundary cases** (GW-BASIC documents these; we pin the ambiguous ones as
  IMPLEMENTATION-DEFINED): `duration = 0` turns off any currently-sounding tone;
  a very small duration (< ~0.022 ticks) in GW-BASIC sounds continuously until
  the next `SOUND`/`PLAY`. **Decision needed** — see §6.

`SOUND` is **synchronous/foreground** by default in GW-BASIC (it blocks for the
duration unless background music is queued). It also acts as a natural frame
boundary, like `SLEEP`.

## 3. `BEEP` — exact semantics

`BEEP` (no arguments) produces the standard console alert: **~800 Hz for ~0.25 s**,
equivalent to `PRINT CHR$(7)`. Faithful to GW-BASIC/QuickBASIC.

## 4. `PLAY` — Music Macro Language

```
PLAY stringexpression
```

The string is a sequence of single-letter commands (case-insensitive, spaces
ignored). The full GW-BASIC/QuickBASIC command set, with the exact ranges and
defaults from the manual:

| Command | Meaning | Range | Default |
|---|---|---|---|
| `A`–`G` | Play that note in the current octave/length | — | — |
| `#` or `+` | (suffix) sharpen the preceding note | — | — |
| `-` | (suffix) flatten the preceding note | — | — |
| `n` after a note | per-note length override (`A8` = eighth-note A) | 1–64 | current `L` |
| `.` | (suffix) dotted note — ×3/2 play time; multiple dots stack | — | — |
| `O n` | set octave | 0–6 | **4** |
| `>` | shift up one octave (prefix or standalone) | — | — |
| `<` | shift down one octave | — | — |
| `N n` | play note by absolute number (0 = rest) | 0–84 | — |
| `L n` | set default note length (`L1` whole … `L64` 64th) | 1–64 | **4** |
| `P n` | pause/rest of the given length | 1–64 | — |
| `T n` | tempo — number of quarter notes (`L4`) per minute | 32–255 | **120** |
| `MN` | "music normal" — each note sounds **7/8** of its slot, rest silent | — | default |
| `ML` | "music legato" — each note sounds the **full** slot | — | — |
| `MS` | "music staccato" — each note sounds **3/4** of its slot, rest silent | — | — |
| `MF` | foreground mode — `PLAY`/`SOUND` block until done | — | **default** |
| `MB` | background mode — queue up to 32 notes, program continues | — | — |
| `X str;` | execute the MML in a string variable (substring) | — | — |

### Timing model (how the above turns into seconds)

- A quarter note (`L4`) lasts `60 / T` seconds (T = quarter notes per minute).
- A note of length `n` lasts `240 / (n * T)` seconds before dots.
- Each dot multiplies by 3/2; multiple dots use the standard cumulative dotted
  rule (2 dots = ×7/4, …).
- Articulation (`MN`/`ML`/`MS`) splits each note's slot into **sounded** vs
  **silent** time (7/8, full, or 3/4 sounded) — total slot time is unchanged, so
  tempo is preserved.
- Pitch: equal-tempered semitones. **Decision needed**: tuning reference (we'd
  pin A4 = 440 Hz; GW-BASIC's 8253-divisor frequencies differ by a few cents —
  IMPLEMENTATION-DEFINED, see §6).

## 5. Architecture (mirror the graphics module)

The graphics module's design is the proven template and audio should copy it
exactly, which keeps it embeddable and byte-identical across engines:

- **Device-independent core in `ArcadeBasic.Runtime`.** `SOUND`/`BEEP`/`PLAY`
  lower to a flat sequence of **tone events** `(frequencyHz, soundedSeconds,
  silentSeconds)` in shared Runtime code — exactly as `GRAPH`/`SET` lower to
  clipped vector primitives via `GraphicsState`. Both the interpreter and the VM
  call the same lowering, so `run` / `vm` / `build` produce **identical** audio
  (the same guarantee graphics has, asserted in `ArcadeBasic.Conformance.Tests`).
- **`IAudioDevice`** (parallel to `IGraphicsDevice`) consumes tone events.
  Proposed shipped backends:
  - **WAV synth** — square-wave PCM → `--wav out.wav` (mirrors `--svg`). This is
    the **portable, dependency-free, deterministic** backend: works headless, and
    parity tests assert byte-identical WAV across engines (just like SVG today).
    No new runtime dependency, so AOT/standalone builds are unaffected.
  - **Console/OS** — audible playback on a live terminal. `Console.Beep(freq,ms)`
    covers Windows; cross-platform real-time playback is limited without a native
    audio dependency, so this backend may start Windows-only or be deferred (the
    WAV backend is the universal one). **Decision needed** — see §6.
  - **Unity** — a procedural `AudioClip`/`AudioSource` fed by the same tone
    events, alongside the existing `BasicScreen` graphics backend.
- **Testability**: because the WAV backend is deterministic and the lowering is
  shared, audio gets the same parity-test coverage as graphics — a strong fit
  with the existing differential-testing story.

## 6. Implementation phases

The full GW-BASIC/QuickBASIC feature set is the target. It's sequenced into
reviewable phases ordered by dependency and risk — each phase ships something
usable and testable on its own, and earlier phases don't bake in assumptions that
the later ones have to undo.

**Phase 1 — Core + offline output (foundation). ✅ IMPLEMENTED.**
Lexer/parser/sema for `SOUND`/`BEEP`/`PLAY`; the shared tone-event lowering in
`Runtime` (`AudioState` + `ToneEvent` + `IAudioDevice`); the full `PLAY` MML
parser (all of §4 except the `MB`/`X`/`=` items below, which are parsed and, for
`X`/`=`, rejected as not-yet-implemented); and the **`--wav` backend**
(`WavAudioDevice`, square wave 44.1 kHz/16-bit mono). Byte-identical across
`run`/`vm`, covered by `AudioTests` (interpreter-vs-VM parity via
`RecordingAudioDevice`, plus MML semantics). `arcade-basic run file.bas --wav out.wav`
renders a correct WAV with no audio hardware. Example: `examples/music.bas`.

**Phase 2 — Live audible playback (cross-platform).**
A real-time `IAudioDevice` that actually makes sound from `arcade-basic run`/`vm`
and standalone binaries. This is the phase that needs the **audio-output decision**
(below) because it touches the no-dependency / single-file AOT story. Foreground
(`MF`) playback only at this stage; it blocks for the tone duration and doubles as
a frame boundary like `SLEEP`.

**Phase 3 — Background music (`MB`) + async.**
True `MB`: a buffered audio thread (32-note queue, `PLAY` blocks when full, per
GW-BASIC) so music plays while the program runs; `MF`/`MB` switch foreground vs
background. For the offline `--wav` backend, model a **virtual clock** that
advances on `SLEEP` and on foreground tones, and mix background notes onto that
timeline so offline output stays deterministic and meaningful.

**Phase 4 — Advanced MML.**
`X str;` (execute a substring variable) and `=var;` (numeric substitution into a
command) — recursive MML evaluation with variable lookup. Small, self-contained.

**Phase 5 — Unity audio backend.**
Procedural `AudioClip`/`AudioSource` fed by the same tone events, alongside the
existing `BasicScreen` graphics backend.

Docs (`conformance.md` extensions rows, README, `keywords.md`) and a `music.bas`
example are updated incrementally as each phase lands — so the documentation
requirement is satisfied per phase, not deferred to the end.

## 7. Open design decisions (to pin down as IMPLEMENTATION-DEFINED)

Independent of phasing, these get an explicit recorded choice:

1. **Audio-output mechanism (Phase 2) — the one real architectural call.**
   Cross-platform real-time PCM playback needs *something*: `Console.Beep` is
   Windows-only and crude (mono, no PCM); true cross-platform means either
   P/Invoking platform audio APIs or bundling a small native lib (e.g. miniaudio)
   per-RID. That tension with the **self-contained single-file AOT binary** is the
   crux — bundling per-RID native audio affects the standalone-build story. This
   decision deserves its own short design pass when we reach Phase 2; Phase 1
   doesn't depend on it.
2. **`SOUND` boundary cases.** `duration = 0` = silence/stop; reject or clamp
   negative; define the sub-tick "continuous" case explicitly.
3. **Tuning.** A4 = 440 Hz equal temperament; note GW-BASIC's 8253-divisor
   frequencies differ by a few cents.
4. **WAV format.** Sample rate (e.g. 44.1 kHz), 16-bit mono, square waveform
   (most faithful to the PC speaker; sine optional later).

## 7. Documentation plan (the policy requirement)

On implementation:
- Add three rows to the **"Extensions beyond ISO/ECMA Full BASIC"** table in
  `docs/conformance.md`, tagged *Source: Microsoft BASIC (GW-BASIC/QuickBASIC)*,
  with the §2–§4 semantics and the §6 implementation-defined choices spelled out.
  This also retires the current gap where `SOUND`/`WAIT` are reserved but
  undocumented.
- Note the feature in the root `README.md` (and the Unity sample README, since
  the Unity backend gains audio).
- Add the keywords to `docs/keywords.md` with commented examples.
- Ship a `sound.bas` / `music.bas` example (a recognizable tune via `PLAY`),
  registered the usual way (`@category` tag → auto-menu).

## 8. Out of scope

- ISO §14 real-time / process I/O (unchanged — out of scope).
- `PLAY` graphics-cursor or non-Microsoft MML dialects (MSX extensions, etc.).
- Sampled/wavetable audio, multiple simultaneous voices/channels (the PC speaker
  is monophonic; faithful v1 is single-voice).

## 9. Sources

- [GW-BASIC User's Guide — SOUND](https://hwiegman.home.xs4all.nl/gw-man/SOUND.html) ([mirror](http://www.ojodepez-fanzine.net/network/qbdl/GW-MAN/SOUND.html))
- [GW-BASIC User's Guide — PLAY](https://hwiegman.home.xs4all.nl/gw-man/PLAY.html)
- [Microsoft BASIC MML — Video Game Music Preservation Foundation](https://www.vgmpf.com/Wiki/index.php?title=Microsoft_BASIC_MML)
- [GW-BASIC User's Manual (archive.org full text)](https://archive.org/stream/GWBASICUsersManual/GWBASIC%20User's%20Manual_djvu.txt)
- [QuickBASIC SOUND statement reference](https://qbasic.net/en/qb-manual/Statement/SOUND.htm)
