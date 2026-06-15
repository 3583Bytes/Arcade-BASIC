! music.bas — the audio extension: SOUND, BEEP and PLAY (MML).      @category Basics
! These are Microsoft-BASIC / GW-BASIC dialect statements, NOT ISO/ECMA Full
! BASIC — see docs/conformance.md and docs/audio-extension.md. Render the audio
! to a WAV file with:
!     arcade-basic run examples/music.bas --wav music.wav
! (Real-time audible playback is a later phase; with no --wav the program runs
! silently but still exercises every audio statement.)

PRINT "Arcade BASIC audio demo"

! A plain tone: SOUND frequency_hz, duration_ticks  (18.2 ticks = 1 second).
PRINT "SOUND - a 440 Hz tone for about half a second"
SOUND 440, 9

! The standard alert tone (~800 Hz, 1/4 second).
PRINT "BEEP"
BEEP

! PLAY speaks Music Macro Language: notes A-G (with # / + / - accidentals),
! O = octave, L = default note length, T = tempo (quarter notes per minute),
! MN/ML/MS = normal/legato/staccato, P = rest, > / < = octave up / down.
PRINT "PLAY - a C major scale up and back down"
PLAY "T140 O4 L8 CDEFGAB>C C<BAGFEDC"

PRINT "PLAY - Twinkle, Twinkle, Little Star"
PLAY "T120 O4 L4 CCGG AA G2 FF EE DD C2"

PRINT "done"
END
