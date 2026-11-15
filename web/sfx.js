/* =========================================================================
   Soft mathematical interface audio.

   Every cue is synthesised from oscillators and a short envelope: no samples,
   no chiptune. The intent is quiet, warm feedback that never fatigues during a
   long batch lift, so it can stay enabled.

   Cues are short (< 260 ms), low volume, and built from a pentatonic scale so
   overlapping sounds stay consonant.
   ========================================================================= */

(function () {
    "use strict";

    var ctx = null;
    var master = null;
    var enabled = false;

    // A minor pentatonic, two octaves: any subset is consonant.
    var P = [220.00, 261.63, 293.66, 329.63, 392.00, 440.00, 523.25, 587.33];

    function ensure() {
        if (ctx) {
            if (ctx.state === "suspended") ctx.resume();
            return ctx;
        }

        var Ctor = window.AudioContext || window.webkitAudioContext;
        if (!Ctor) return null;

        try {
            ctx = new Ctor();

            master = ctx.createGain();
            master.gain.value = 0.09;

            // A gentle low-pass keeps the top end soft rather than piercing.
            var filter = ctx.createBiquadFilter();
            filter.type = "lowpass";
            filter.frequency.value = 2200;
            filter.Q.value = 0.3;

            master.connect(filter);
            filter.connect(ctx.destination);
        } catch (e) {
            ctx = null;
        }

        return ctx;
    }

    /**
     * One note with an exponential attack and release.
     *
     * Exponential ramps cannot touch zero, so the envelope clamps to a tiny
     * value instead: that is what keeps the cue click-free.
     */
    function note(freq, delay, duration, gain, type) {
        var c = ensure();
        if (!c) return;

        var t0 = c.currentTime + delay;

        var osc = c.createOscillator();
        osc.type = type || "sine";
        osc.frequency.setValueAtTime(freq, t0);

        var env = c.createGain();
        env.gain.setValueAtTime(0.0001, t0);
        env.gain.exponentialRampToValueAtTime(Math.max(0.0002, gain), t0 + 0.02);
        env.gain.exponentialRampToValueAtTime(0.0001, t0 + duration);

        osc.connect(env);
        env.connect(master);

        osc.start(t0);
        osc.stop(t0 + duration + 0.03);
    }

    window.NesSfx = {
        setEnabled: function (value) {
            enabled = !!value;
            if (enabled) ensure();
            return enabled;
        },

        isEnabled: function () {
            return enabled;
        },

        /** Barely-there tick, for navigation. */
        tick: function () {
            if (!enabled) return;
            note(P[4], 0, 0.06, 0.35, "sine");
        },

        /** Two rising notes: a choice was made. */
        select: function () {
            if (!enabled) return;
            note(P[1], 0.00, 0.09, 0.42, "sine");
            note(P[3], 0.04, 0.12, 0.34, "sine");
        },

        /** Calm rising third: work started. */
        start: function () {
            if (!enabled) return;
            note(P[0], 0.00, 0.16, 0.40, "triangle");
            note(P[2], 0.07, 0.20, 0.32, "triangle");
        },

        /** Resolving three-note figure: success. */
        success: function () {
            if (!enabled) return;
            note(P[2], 0.00, 0.13, 0.42, "sine");
            note(P[4], 0.07, 0.16, 0.38, "sine");
            note(P[5], 0.15, 0.24, 0.30, "sine");
        },

        /** Low descending pair: failure, without being harsh. */
        failure: function () {
            if (!enabled) return;
            note(P[2], 0.00, 0.16, 0.38, "triangle");
            note(P[0], 0.10, 0.26, 0.34, "triangle");
        }
    };
})();
