// Sound: the tones SOUND, BEEP and PLAY make (Program.cs TakeTones), played with
// the Web Audio API as a square wave, like the PC speaker GW-BASIC drove.
//
// Tones are laid end to end on a timeline kept in page time (performance.now()),
// and the program's waits follow that timeline — so a program keeps the same
// timing with the sound off, before the browser lets the page make sound (it
// needs a click or a key press first), or with no Web Audio at all. The sound
// just follows the timeline when it can.

const LEVEL = 0.27;          // the desktop renderer's level (PcmRenderer: 9000 of 32767)
const RAMP = 0.003;          // seconds of fade in and out, so tones don't click
export const QUEUE = 32;     // GW-BASIC's background-music buffer: past this, the program waits

export function createAudio() {
  let context = null;        // made on the first click or key press
  let out = null;            // this run's output: disconnecting it silences the run
  let muted = false;
  let end = 0;               // when the last tone given ends (ms, page time)
  const ends = [];           // when each tone not finished yet ends

  function tone(frequency, startMs, seconds) {
    if (!out || context.state !== 'running' || seconds <= 0) return;
    const at = context.currentTime + Math.max(0, (startMs - performance.now()) / 1000);
    const ramp = Math.min(RAMP, seconds / 2);
    const oscillator = context.createOscillator();
    oscillator.type = 'square';
    oscillator.frequency.value = frequency;
    const envelope = context.createGain();
    envelope.gain.setValueAtTime(0, at);
    envelope.gain.linearRampToValueAtTime(LEVEL, at + ramp);
    envelope.gain.setValueAtTime(LEVEL, at + seconds - ramp);
    envelope.gain.linearRampToValueAtTime(0, at + seconds);
    oscillator.connect(envelope).connect(out);
    oscillator.start(at);
    oscillator.stop(at + seconds);
  }

  function silence() {
    out?.disconnect();
    out = null;
    end = 0;
    ends.length = 0;
  }

  return {
    /** Let the page make sound. Call it from a click or key press: browsers only
     *  start audio after one. */
    unlock() {
      try {
        context ??= new AudioContext();
        if (context.state === 'suspended') context.resume();
      } catch { /* no Web Audio: the timeline still runs, silently */ }
    },

    /** A new run: whatever the last one left playing stops. */
    reset() {
      silence();
      if (!context) return;
      out = context.createGain();
      out.gain.value = muted ? 0 : 1;
      out.connect(context.destination);
    },

    /** Stop: silence now. */
    stop: silence,

    /** Tones from the program, flat (frequency Hz, sounded s, silent s); frequency 0 is a rest. */
    play(tones) {
      let t = Math.max(end, performance.now());
      for (let i = 0; i + 2 < tones.length; i += 3) {
        if (tones[i] > 0) tone(tones[i], t, tones[i + 1]);
        t += (tones[i + 1] + tones[i + 2]) * 1000;
        ends.push(t);
      }
      end = t;
    },

    /** Milliseconds until everything given has played. */
    remaining() { return Math.max(0, end - performance.now()); },

    /** How many tones given haven't finished. */
    queued() {
      const now = performance.now();
      let done = 0;
      while (done < ends.length && ends[done] <= now) done++;
      ends.splice(0, done);
      return ends.length;
    },

    /** 'none' before the first click or key, then the AudioContext's state. */
    get state() { return context?.state ?? 'none'; },

    get muted() { return muted; },
    set muted(value) {
      muted = value;
      out?.gain.setValueAtTime(value ? 0 : 1, context.currentTime);
    },
  };
}
