// Stepping a program from the page's animation frames, for the IDE (main.js) and the
// player (src/ArcadeBasic.Player): run the VM for a slice of each frame, and wait out
// SLEEPs and foreground sound without blocking the page.

import { QUEUE } from './audio.js';

// StepStatus values (src/ArcadeBasic.Vm/StepResult.cs)
export const RUNNING = 0, SLEEPING = 1, NEEDS_INPUT = 2, AWAITING_AUDIO = 3, HALTED = 4, NEEDS_POINT = 5, PAUSED = 6;

const SLICE = 20_000;          // instructions per Step call
const FRAME_BUDGET_MS = 10;    // VM time allowed per animation frame
// A foreground SOUND/PLAY resumes this soon before its tones end, so the next
// statement's tones follow on without a gap (the page only steps on animation frames).
const AUDIO_LEAD_MS = 20;

/** A program's loop state, for stepFrame (the page adds its own). */
export const newRun = () => ({ sleepingUntil: 0, audioUntil: 0, began: performance.now(), ticks: 0, frames: 0, vmMs: 0 });

/** This animation frame's share of the program (`P`, the .NET side), once it isn't
 *  sleeping or waiting for its sound: steps until it uses the frame's time or stops.
 *  The status it stopped on — RUNNING if it ran out of time, SLEEPING or
 *  AWAITING_AUDIO if it's waiting (handled here); NEEDS_INPUT, NEEDS_POINT, PAUSED or
 *  HALTED are the caller's to handle — or null if it wasn't its turn. */
export function stepFrame(P, r, audio) {
  const playing = audio.queued();
  if (performance.now() < Math.max(r.sleepingUntil, r.audioUntil) || playing > QUEUE) return null;
  const t0 = performance.now();
  P.SetTonesPlaying(playing);
  let s;
  for (;;) {
    s = P.Step(SLICE);
    if (s === RUNNING && performance.now() - t0 < FRAME_BUDGET_MS) continue;
    if (s === AWAITING_AUDIO) {     // a foreground SOUND, BEEP or PLAY; or a full background queue
      audio.play(P.TakeTones());
      if (P.MaxQueuedTones() === 0) r.audioUntil = performance.now() + audio.remaining() - AUDIO_LEAD_MS;
      // (with a full queue, the wait is the `playing > QUEUE` above)
    } else if (s === SLEEPING) {
      // Wake-ups land on animation frames, so time each SLEEP from the previous
      // deadline (unless we've fallen well behind) to keep the program's frame rate
      // on average: SLEEP 0.04 alternates 2- and 3-frame waits = 25 fps, not 20.
      const now = performance.now();
      const base = now - r.sleepingUntil < 100 ? r.sleepingUntil : now;
      r.sleepingUntil = base + P.SleepSeconds() * 1000;
    }
    break;
  }
  r.vmMs += performance.now() - t0;
  return s;
}
