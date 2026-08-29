using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Sightline;

// ─────────────────────────────────────────────────────────────────────────────
// PROGRAM RESONANCE — WAVE A1 "THE EAR"
//
// Nobody has ever HEARD this game. There is no audio device in the sandbox and the
// owner has never tuned the mix, so every audio decision so far has been made blind.
// This file is the ear: a device-free measurement rig over the exact float samples the
// synth hands to Raylib, plus a committed budget the mix has to keep passing.
//
//   SIGHTLINE_AUDIODUMP=1   render every cue + both music beds to audio_dump/*.wav and
//                           print a full measurement table (level / spectrum / tails /
//                           loop seams / concurrent-stack headroom).
//   SIGHTLINE_AUDIOGATE=1   turn those measurements into per-check PASS/FAIL lines and a
//                           final "AUDIOGATE: PASS|FAIL".
//
// Both are windowless and device-free. Neither is wired to anything automatic — house
// style is env-gated hooks run by hand (NO CI, ever).
// ─────────────────────────────────────────────────────────────────────────────
public static partial class Audio
{
    // ── measurement primitives ────────────────────────────────────────────────

    const float Eps = 1e-9f;
    static float Db(float amp) => amp <= Eps ? -200f : 20f * MathF.Log10(amp);

    /// Everything the gate and the dump table need to know about one buffer.
    internal struct CueStats
    {
        public string Id;
        public float DurMs;
        public int N;            // real sample count (excludes the 8-sample BuildBuffer pad)
        public float PeakDb, RmsDb, CrestDb, Dc, TailDb, Zcr, CentroidHz;
        public int Clipped;
        public float BLo, BMid, BHi, BAir;   // energy fractions: <200 / 200-1k / 1k-5k / >5k
    }

    /// Measure the FIRST `n` samples of `buf` (n = the real, un-padded length).
    /// BuildBuffer allocates (int)(dur*SR) + 8 samples, so the last 8 are ALWAYS zero:
    /// any "does the tail reach silence" test that reads buf[^1] passes vacuously. The
    /// tail here is deliberately sampled at index n-1 — the last sample that is real.
    internal static CueStats Measure(string id, float[] buf, int n, float durMs)
    {
        n = Math.Min(n, buf.Length);
        var st = new CueStats { Id = id, DurMs = durMs, N = n };
        double sum = 0, sq = 0;
        float peak = 0;
        int clip = 0, zc = 0;
        for (int i = 0; i < n; i++)
        {
            float v = buf[i];
            float a = MathF.Abs(v);
            if (a > peak) peak = a;
            if (a >= 0.99997f) clip++;                    // 32767/32768 rounds to full-scale
            sum += v; sq += (double)v * v;
            if (i > 0 && ((buf[i - 1] < 0f && v >= 0f) || (buf[i - 1] >= 0f && v < 0f))) zc++;
        }
        float rms = (float)Math.Sqrt(sq / Math.Max(1, n));
        st.PeakDb = Db(peak);
        st.RmsDb = Db(rms);
        st.CrestDb = st.PeakDb - st.RmsDb;
        st.Dc = (float)(sum / Math.Max(1, n));
        st.Clipped = clip;
        st.Zcr = zc / (float)Math.Max(1, n);
        st.TailDb = Db(MathF.Abs(buf[Math.Max(0, n - 1)]));
        Spectrum(buf, n, out st.CentroidHz, out st.BLo, out st.BMid, out st.BHi, out st.BAir);
        return st;
    }

    // Welch-style averaged power spectrum (Hann, 4096-pt, 50% overlap; short cues are
    // zero-padded into one window). 10.8 Hz bins — plenty for the 200 Hz / 1k / 5k splits.
    const int FftN = 4096;

    static void Spectrum(float[] buf, int n, out float centroid,
                         out float bLo, out float bMid, out float bHi, out float bAir)
    {
        var acc = new double[FftN / 2 + 1];
        var re = new double[FftN];
        var im = new double[FftN];
        int hop = FftN / 2, windows = 0;
        for (int off = 0; off == 0 || off + FftN <= n; off += hop)
        {
            for (int i = 0; i < FftN; i++)
            {
                int idx = off + i;
                double x = idx < n ? buf[idx] : 0.0;
                double w = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / (FftN - 1));   // Hann
                re[i] = x * w; im[i] = 0.0;
            }
            Fft(re, im);
            for (int k = 0; k <= FftN / 2; k++) acc[k] += re[k] * re[k] + im[k] * im[k];
            windows++;
            if (off + FftN > n) break;
        }
        double tot = 0, wsum = 0, lo = 0, mid = 0, hi = 0, air = 0;
        for (int k = 1; k <= FftN / 2; k++)          // skip DC bin (handled by the DC check)
        {
            double f = k * (double)SR / FftN, p = acc[k];
            tot += p; wsum += f * p;
            if (f < 200) lo += p;
            else if (f < 1000) mid += p;
            else if (f < 5000) hi += p;
            else air += p;
        }
        if (tot <= 0) { centroid = 0; bLo = bMid = bHi = bAir = 0; return; }
        centroid = (float)(wsum / tot);
        bLo = (float)(lo / tot); bMid = (float)(mid / tot);
        bHi = (float)(hi / tot); bAir = (float)(air / tot);
    }

    // in-place iterative radix-2 Cooley-Tukey
    static void Fft(double[] re, double[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2.0 * Math.PI / len;
            double wr = Math.Cos(ang), wi = Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                double cr = 1.0, ci = 0.0;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = i + k + len / 2;
                    double xr = re[b] * cr - im[b] * ci;
                    double xi = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - xr; im[b] = im[a] - xi;
                    re[a] += xr; im[a] += xi;
                    double nr = cr * wr - ci * wi;
                    ci = cr * wi + ci * wr; cr = nr;
                }
            }
        }
    }

    // ── the cue table (ordered; the dump/gate iterate this, not dictionary order) ──

    /// Ordered list of every SFX cue id (matches SfxCueIds / BuildRecipes).
    internal static string[] OrderedCues => SfxCueIds;

    /// Render one cue exactly as the device would get it, plus its real sample count.
    internal static float[] RenderCueById(string id, out int realN, out float durMs)
    {
        BuildRecipes();
        var (dur, target, fill) = _recipes[id];
        realN = (int)(dur * SR);
        durMs = dur * 1000f;
        return RenderCue(id, dur, target, fill);
    }

    // ── loop-seam analysis ────────────────────────────────────────────────────

    internal struct SeamStats
    {
        public string Id;
        public float First, Last;
        public float ValueDelta;    // |b[0] - b[n-1]| : the STEP across the wrap
        public float SlopeDelta;    // |(b[0]-b[n-1]) - (b[n-1]-b[n-2])| : slope break at the wrap
        public float StepMax;       // largest step the waveform takes ANYWHERE inside the loop
        public float StepP999;      // 99.9th percentile of those interior steps
        public float Ratio;         // ValueDelta / StepMax  — the scale-free click test
        public float CurvMax;       // largest 2nd difference ANYWHERE inside the loop
        public float CurvRatio;     // SlopeDelta / CurvMax  — the scale-free KINK test
    }

    /// Three views of the loop point, because one of them is a trap.
    ///
    /// |b[0]-b[n-1]| (ValueDelta) is NOT a discontinuity: a perfectly seamless loop still
    /// steps by one sample across the wrap, and that step grows linearly with the highest
    /// frequency in the bed. It cannot be driven toward zero at the same time as the
    /// music-brightness floor — the pre-A1 beds already measured 0.023 / 0.009 there while
    /// being provably seamless by construction (integer Hz over an integer-second buffer).
    ///
    /// So the primary gate is RATIO: the wrap step measured against the LARGEST step the
    /// waveform takes anywhere inside the loop. A seamless wrap is an ordinary step of the
    /// same waveform (ratio ~1 — and it lands near the top of the range here precisely
    /// because every partial is phase-aligned at zero at the loop point, which is where a
    /// sine's slope is steepest). A real click is a step the waveform never otherwise takes,
    /// and shows up as a ratio in the tens.
    ///
    /// A2 EXTENDS THE SAME ARGUMENT TO THE SLOPE. A1 kept |1st-diff delta| as a secondary
    /// ABSOLUTE bound (<= 0.005), but that constant was calibrated against beds whose largest
    /// interior sample step was 0.076. The A2 beds are broadband (interior step max 0.20), and
    /// a wider-band waveform simply BENDS harder everywhere — the rebuilt beds measured 0.016
    /// / 0.035 at the wrap while being seamless by exactly the same construction (integer Hz,
    /// double-accumulated phase, pulse envelopes that are functions of the loop phase only).
    /// An absolute bound on a scale-dependent quantity is the same trap the value delta was.
    /// So the slope check becomes CurvRatio: the wrap's second difference against the LARGEST
    /// second difference the waveform takes anywhere inside the loop. A seamless wrap bends no
    /// harder than the waveform's own worst interior bend.
    ///
    /// BUT BE HONEST ABOUT WHAT THESE RATIOS CAN AND CANNOT SEE. Both of them are ratios
    /// against the waveform's own worst-case interior behaviour, and a BROADBAND bed's worst
    /// case is large — so a small discontinuity hides inside it. Measured: adding a single
    /// 333.37 Hz partial (deliberately off integer Hz, so it does NOT divide the loop) at
    /// amplitude 0.05 to the ambient bed leaves ratio 0.03 and curv ratio 0.38, i.e. the gate
    /// still says PASS on a bed that genuinely clicks. These two checks are therefore a
    /// BACKSTOP for a gross break, not the guarantee. The guarantee is the PERIODICITY check
    /// in GateReport, which compares the loop against its own continuation sample-for-sample
    /// and catches that same de-tuned partial at 0.0250 against a 0.0001 tolerance.
    internal static SeamStats Seam(string id, float[] b)
    {
        int n = b.Length;
        float dSeam = b[0] - b[n - 1];
        float dInt = b[n - 1] - b[n - 2];
        var steps = new float[n - 1];
        for (int i = 1; i < n; i++) steps[i - 1] = MathF.Abs(b[i] - b[i - 1]);
        float curvMx = 0f;
        for (int i = 1; i < n - 1; i++)
        {
            float c = MathF.Abs(b[i + 1] - 2f * b[i] + b[i - 1]);
            if (c > curvMx) curvMx = c;
        }
        Array.Sort(steps);
        float p999 = steps[(int)((steps.Length - 1) * 0.999f)];
        float mx = steps[steps.Length - 1];
        float slope = MathF.Abs(dSeam - dInt);
        return new SeamStats
        {
            Id = id, First = b[0], Last = b[n - 1],
            ValueDelta = MathF.Abs(dSeam), SlopeDelta = slope,
            StepMax = mx, StepP999 = p999, Ratio = mx > 1e-9f ? MathF.Abs(dSeam) / mx : 0f,
            CurvMax = curvMx, CurvRatio = curvMx > 1e-9f ? slope / curvMx : 0f,
        };
    }

    // ── concurrent-stack analysis ─────────────────────────────────────────────

    // The realistic worst cases the engine actually fires together. Offsets in seconds.
    // The two kill stacks are pinned at coincident onsets on purpose: that IS the worst
    // alignment the mixer can hand a speaker, and headroom has to survive it.
    internal static readonly (string name, (string cue, float at)[] parts)[] Stacks =
    {
        ("w_lmg+crit",                 new[] { ("w_lmg", 0f), ("crit", 0f) }),
        ("w_lmg+crit+death+st_kill",   new[] { ("w_lmg", 0f), ("crit", 0f), ("death", 0f), ("st_kill", 0f) }),
        ("3x(w_rifle+hit) overwatch",  new[] { ("w_rifle", 0f), ("hit", 0f),
                                               ("w_rifle", 0.12f), ("hit", 0.12f),
                                               ("w_rifle", 0.24f), ("hit", 0.24f) }),
        ("w_shotgun+crit+st_lastkill", new[] { ("w_shotgun", 0f), ("crit", 0f), ("st_lastkill", 0f) }),
    };

    /// Mix a stack at the REAL master volume and report its peak + clipped-sample count.
    internal static (float peakDb, int clipped) MixStack((string cue, float at)[] parts)
    {
        int len = 0;
        var bufs = new List<(float[] b, int at)>();
        foreach (var (cue, at) in parts)
        {
            var b = RenderCueById(cue, out int rn, out _);
            int off = (int)(at * SR);
            bufs.Add((b, off));
            len = Math.Max(len, off + rn);
        }
        var mix = new float[len];
        foreach (var (b, off) in bufs)
            for (int i = 0; i < b.Length && off + i < len; i++) mix[off + i] += b[i];
        float peak = 0; int clip = 0;
        for (int i = 0; i < len; i++)
        {
            float v = mix[i] * MasterVol;
            float a = MathF.Abs(v);
            if (a > peak) peak = a;
            if (a >= 0.99997f) clip++;
        }
        return (Db(peak), clip);
    }

    // ── SIGHTLINE_AUDIODUMP ───────────────────────────────────────────────────

    static string F(float v, string fmt) => v.ToString(fmt, CultureInfo.InvariantCulture);

    /// Render every cue + both beds to `dir`/*.wav and print the full measurement table.
    /// Device-free; safe with no audio hardware.
    public static string DumpReport(string dir)
    {
        var sb = new StringBuilder();
        Directory.CreateDirectory(dir);
        sb.AppendLine($"AUDIODUMP -> {dir}/  (SR={SR}, MasterVol={F(MasterVol, "0.00")})");
        sb.AppendLine();
        sb.AppendLine("cue             dur_ms   peak    rms  crest       dc  clip  centroid   <200  200-1k  1k-5k    >5k     tail    zcr");
        sb.AppendLine("──────────────────────────────────────────────────────────────────────────────────────────────────────────────────");

        void Row(CueStats s)
        {
            sb.AppendLine($"{s.Id,-14} {F(s.DurMs, "0"),6} {F(s.PeakDb, "0.0"),6} {F(s.RmsDb, "0.0"),6} " +
                          $"{F(s.CrestDb, "0.0"),6} {F(s.Dc, "+0.00000;-0.00000"),8} {s.Clipped,5} " +
                          $"{F(s.CentroidHz, "0"),9} {F(s.BLo * 100, "0.0"),6} {F(s.BMid * 100, "0.0"),7} " +
                          $"{F(s.BHi * 100, "0.0"),6} {F(s.BAir * 100, "0.0"),6} {F(s.TailDb, "0.0"),8} {F(s.Zcr, "0.000"),6}");
        }

        var all = new List<CueStats>();
        foreach (var id in OrderedCues)
        {
            var buf = RenderCueById(id, out int n, out float ms);
            File.WriteAllBytes(Path.Combine(dir, id + ".wav"), EncodeWav(buf, 1f));
            var s = Measure(id, buf, n, ms);
            all.Add(s); Row(s);
        }
        sb.AppendLine();
        foreach (var m in new[] { "ambient", "combat" })
        {
            var buf = RenderMusic(m);
            File.WriteAllBytes(Path.Combine(dir, "music_" + m + ".wav"), EncodeWav(buf, 1f));
            Row(Measure("music:" + m, buf, buf.Length, MusicSecs * 1000f));
        }

        // level spread across the SFX set — the "nobody designed this" number
        float lo = 999, hi = -999; string loId = "", hiId = "";
        foreach (var s in all) { if (s.RmsDb < lo) { lo = s.RmsDb; loId = s.Id; } if (s.RmsDb > hi) { hi = s.RmsDb; hiId = s.Id; } }
        sb.AppendLine();
        sb.AppendLine($"RMS SPREAD: {F(hi - lo, "0.0")} dB   (quietest {loId} {F(lo, "0.0")} dBFS -> loudest {hiId} {F(hi, "0.0")} dBFS)");
        var hitS = all.Find(x => x.Id == "hit"); var critS = all.Find(x => x.Id == "crit");
        sb.AppendLine($"CRIT vs HIT: {F(critS.RmsDb - hitS.RmsDb, "0.0")} dB RMS separation " +
                      $"(crit {F(critS.RmsDb, "0.0")} / hit {F(hitS.RmsDb, "0.0")})");

        sb.AppendLine();
        sb.AppendLine("LOOP SEAMS (a seamless loop still steps one sample; the CLICK is a slope break)");
        foreach (var m in new[] { "ambient", "combat" })
        {
            var sm = Seam(m, RenderMusic(m));
            sb.AppendLine($"  music:{m,-8} first={F(sm.First, "+0.00000;-0.00000")} last={F(sm.Last, "+0.00000;-0.00000")}" +
                          $"  |value delta|={F(sm.ValueDelta, "0.00000")}  |1st-diff delta|={F(sm.SlopeDelta, "0.00000")}" +
                          $"  interior step max={F(sm.StepMax, "0.00000")} p99.9={F(sm.StepP999, "0.00000")}" +
                          $"  ratio={F(sm.Ratio, "0.00")}  curv max={F(sm.CurvMax, "0.00000")} curv ratio={F(sm.CurvRatio, "0.00")}");
        }

        sb.AppendLine();
        sb.AppendLine($"SUM-STACK HEADROOM (mixed at MasterVol={F(MasterVol, "0.00")})");
        foreach (var (name, parts) in Stacks)
        {
            var (pk, cl) = MixStack(parts);
            sb.AppendLine($"  {name,-30} peak {F(pk, "+0.0;-0.0"),6} dBFS   clipped {cl}");
        }
        return sb.ToString();
    }

    // ── SIGHTLINE_AUDIOGATE ───────────────────────────────────────────────────

    // ── THE BUDGET ────────────────────────────────────────────────────────────
    // RATIONALE (why these numbers and not others):
    //  • peak <= -1.0 dBFS   True-peak headroom. A cue normalised to exactly 0 dBFS has no
    //    room for the resampler Raylib runs when SetSoundPitch() detunes it (every weapon
    //    call site passes pitchVar), and inter-sample peaks in a reconstructed waveform
    //    routinely exceed the sample peak. 1 dB is the cheapest insurance there is.
    //  • RMS bands (per category, not per cue) encode the MIX DESIGN, i.e. what should sit
    //    on top of what. They are bands, not points, so a cue can have character inside its
    //    role. The categories are ordered: crit > weapons ~ stingers > impacts > UI > move.
    //  • spread <= 12 dB   The pre-mastering layer spanned 16.6 dB by accident. 12 dB is a
    //    deliberate dynamic range: the footfall you hear a hundred times a mission genuinely
    //    should be far under the run-ending gut punch, but not 17 dB under.
    //  • crit - hit >= 4 dB RMS   The heaviest hit in the game must READ as heaviest. 1.9 dB
    //    (the pre-mastering value) is inside the just-noticeable range for a transient.
    //  • |DC| <= 0.002   DC offset steals headroom and thumps on cue start/stop. Saw-heavy
    //    cues were +0.0037 biased; a 20 Hz one-pole DC blocker takes them to ~0.
    //  • zero clipped samples, cues AND stacks   The synth path clamps in EncodeWav, so
    //    clipping here is silent distortion nobody would ever be told about.
    //  • tail <= -60 dBFS at index (int)(dur*SR)-1   Measured BEFORE the 8 zero pad samples
    //    BuildBuffer always appends — reading buf[^1] is the vacuous-test trap. -60 dBFS is
    //    ~1 LSB at 16-bit; anything above it is an audible truncation click.
    //  • music >= 15% of energy above 1 kHz   A1 set this floor at 5% and cleared it at 5.5 /
    //    6.4% — a threshold picked by the same wave that had to pass it, and low enough that
    //    the beds stayed two thin lines on a black spectrogram (a chord of pure tones with
    //    literal silence between them, which reads to the ear as "the audio is off"). A2
    //    raises it to 15% and rebuilds the beds to clear it honestly: harmonic pad stacks with
    //    independent LFOs plus a loop-seamless band-limited air bed.
    //  • loop seam: wrap step <= the waveform's OWN 99.9th-percentile interior step, AND
    //    |1st-diff delta| <= 0.005. The naive "|b[0]-b[n-1]| <= 0.005" test is a trap: a
    //    provably seamless loop (integer Hz over an integer-second buffer — the design these
    //    beds already used) still steps one sample across the wrap, and that step scales with
    //    the top frequency present. The pre-A1 beds measured 0.023 / 0.009 on it while being
    //    click-free, and it is in direct tension with the brightness floor. The scale-free
    //    question is "does the wrap take a step this waveform never otherwise takes", so the
    //    ratio (against the LARGEST interior step, +5% tolerance) is a backstop. A2 makes the
    //    SLOPE check scale-free the same way (CurvRatio) — see Seam() for why the old absolute
    //    0.005 bound could not survive a broadband bed.
    //  • music PERIODICITY: |bed[N+i] - bed[i]| <= 1e-4 over a 2048-sample probe. THIS is the
    //    real seam guarantee, and it replaces trusting the two ratios above. Every generator
    //    is a pure function of the sample index, so rendering N+2048 samples yields the loop
    //    plus its own true continuation; a seamless loop repeats itself exactly there. Unlike
    //    the ratios it is sensitive at any amplitude — see the measured de-tune probe in Seam().

    const float PeakCeilDb = -1.0f;
    const float MaxSpreadDb = 12.0f;
    const float MinCritOverHitDb = 4.0f;
    const float MaxDc = 0.002f;
    const float TailCeilDb = -60.0f;
    const float MinMusicHiFrac = 0.15f;
    const float MaxSeamRatio = 1.05f;
    const float MaxSeamCurvRatio = 1.05f;
    const int SeamProbe = 2048;
    const float MaxPeriodErr = 1e-4f;

    // category -> (rms floor dBFS, rms ceiling dBFS)
    // Centres come from the mastered mix; the +-3 dB or so of slack is deliberate room for a
    // cue to have character inside its role, tight enough that an accidental level shift trips.
    static readonly (string cat, float lo, float hi)[] RmsBands =
    {
        ("crit",    -23f, -17f),   // the heaviest thing a shot can do
        ("stinger", -25f, -18f),   // event punctuation, over the weapons
        ("weapon",  -27f, -20f),   // the constant voice of the game
        ("impact",  -28f, -19f),   // hit / miss / death
        ("ui",      -31f, -23f),   // present, never competing with a gunshot
        ("move",    -34f, -28f),   // fires a hundred times a mission
    };

    static string CatOf(string id) => id switch
    {
        "crit" => "crit",
        "move" => "move",
        "shoot" or "w_rifle" or "w_shotgun" or "w_sniper" or "w_smg" or "w_lmg" => "weapon",
        "hit" or "miss" or "death" => "impact",
        "st_kill" or "st_lastkill" or "st_victory" or "st_lose" or "st_squadwipe"
            or "win" or "lose" => "stinger",
        _ => "ui",                                  // select/reload/hunker/over/turn
    };

    static IEnumerable<CueStats> Concat(IEnumerable<CueStats> a, IEnumerable<CueStats> b)
    { foreach (var x in a) yield return x; foreach (var x in b) yield return x; }

    /// The committed audio budget as a PASS/FAIL contract. Prints one line per check group
    /// and a final "AUDIOGATE: PASS" / "AUDIOGATE: FAIL (...)". Device-free, no window.
    public static string GateReport()
    {
        var sb = new StringBuilder();
        var fails = new List<string>();
        void Chk(bool ok, string name, string detail)
        {
            sb.AppendLine($"  [{(ok ? "ok  " : "FAIL")}] {name,-26} {detail}");
            if (!ok) fails.Add(name);
        }

        sb.AppendLine($"AUDIOGATE — budget contract over {OrderedCues.Length} SFX cues + 2 music beds " +
                      $"(MasterVol={F(MasterVol, "0.00")})");

        var stats = new Dictionary<string, CueStats>();
        foreach (var id in OrderedCues)
        {
            var buf = RenderCueById(id, out int n, out float ms);
            stats[id] = Measure(id, buf, n, ms);
        }

        var musicStats = new Dictionary<string, CueStats>();
        foreach (var m in new[] { "ambient", "combat" })
        {
            var mb = RenderMusic(m);
            musicStats["music:" + m] = Measure("music:" + m, mb, mb.Length, MusicSecs * 1000f);
        }

        // 1. peak ceiling (SFX cues AND both music beds)
        {
            var bad = new List<string>();
            float worst = -200f; string worstId = "";
            foreach (var s in Concat(stats.Values, musicStats.Values))
            {
                if (s.PeakDb > worst) { worst = s.PeakDb; worstId = s.Id; }
                if (s.PeakDb > PeakCeilDb) bad.Add($"{s.Id} {F(s.PeakDb, "0.0")}");
            }
            Chk(bad.Count == 0, "peak <= -1.0 dBFS",
                bad.Count == 0 ? $"hottest {worstId} {F(worst, "0.0")} dBFS (23 cues + 2 beds)"
                               : $"{bad.Count} over: {string.Join(", ", bad)}");
        }

        // 2. per-category RMS bands
        {
            var bad = new List<string>();
            foreach (var s in stats.Values)
            {
                string c = CatOf(s.Id);
                var band = Array.Find(RmsBands, b => b.cat == c);
                if (s.RmsDb < band.lo || s.RmsDb > band.hi)
                    bad.Add($"{s.Id}({c}) {F(s.RmsDb, "0.0")} not in [{F(band.lo, "0")},{F(band.hi, "0")}]");
            }
            Chk(bad.Count == 0, "rms in category band",
                bad.Count == 0 ? "all 23 cues inside their role band"
                               : $"{bad.Count} outside: {string.Join("; ", bad)}");
        }

        // 3. overall RMS spread
        {
            float lo = 999, hi = -999; string loId = "", hiId = "";
            foreach (var s in stats.Values)
            { if (s.RmsDb < lo) { lo = s.RmsDb; loId = s.Id; } if (s.RmsDb > hi) { hi = s.RmsDb; hiId = s.Id; } }
            Chk(hi - lo <= MaxSpreadDb, "rms spread <= 12 dB",
                $"{F(hi - lo, "0.0")} dB ({loId} {F(lo, "0.0")} -> {hiId} {F(hi, "0.0")})");
        }

        // 4. crit reads heavier than hit
        {
            float d = stats["crit"].RmsDb - stats["hit"].RmsDb;
            Chk(d >= MinCritOverHitDb, "crit - hit >= 4 dB rms", $"{F(d, "0.0")} dB");
        }

        // 5. DC offset
        {
            var bad = new List<string>();
            float worst = 0; string worstId = "";
            foreach (var s in stats.Values)
            {
                if (MathF.Abs(s.Dc) > MathF.Abs(worst)) { worst = s.Dc; worstId = s.Id; }
                if (MathF.Abs(s.Dc) > MaxDc) bad.Add($"{s.Id} {F(s.Dc, "+0.00000;-0.00000")}");
            }
            Chk(bad.Count == 0, "|dc| <= 0.002",
                bad.Count == 0 ? $"worst {worstId} {F(worst, "+0.00000;-0.00000")}"
                               : $"{bad.Count} biased: {string.Join(", ", bad)}");
        }

        // 6. no clipped samples in any single cue
        {
            var bad = new List<string>();
            foreach (var s in stats.Values) if (s.Clipped > 0) bad.Add($"{s.Id} x{s.Clipped}");
            Chk(bad.Count == 0, "cue clipping == 0",
                bad.Count == 0 ? "no cue reaches full scale" : string.Join(", ", bad));
        }

        // 7. no clipped samples in any realistic concurrent stack
        {
            var bad = new List<string>(); var seen = new List<string>();
            foreach (var (name, parts) in Stacks)
            {
                var (pk, cl) = MixStack(parts);
                seen.Add($"{name} {F(pk, "+0.0;-0.0")}dBFS/{cl}");
                if (cl > 0 || pk > 0f) bad.Add($"{name} {F(pk, "+0.0;-0.0")} dBFS x{cl}");
            }
            Chk(bad.Count == 0, "stack clipping == 0",
                bad.Count == 0 ? string.Join(" | ", seen) : string.Join("; ", bad));
        }

        // 8. tails reach silence BEFORE the 8-sample pad
        {
            var bad = new List<string>();
            float worst = -200f; string worstId = "";
            foreach (var s in stats.Values)
            {
                if (s.TailDb > worst) { worst = s.TailDb; worstId = s.Id; }
                if (s.TailDb > TailCeilDb) bad.Add($"{s.Id} {F(s.TailDb, "0.0")} dBFS @ {s.N - 1}");
            }
            Chk(bad.Count == 0, "tail <= -60 dBFS",
                bad.Count == 0 ? $"loudest last real sample {worstId} {F(worst, "0.0")} dBFS"
                               : $"{bad.Count} truncated: {string.Join(", ", bad)}");
        }

        // 9-10. music beds: brightness + loop seam
        foreach (var m in new[] { "ambient", "combat" })
        {
            var buf = RenderMusic(m);
            var s = Measure("music:" + m, buf, buf.Length, MusicSecs * 1000f);
            float above1k = s.BHi + s.BAir;
            Chk(above1k >= MinMusicHiFrac, $"music:{m} >1kHz >= 15%",
                $"{F(above1k * 100, "0.000")}% of energy above 1 kHz (centroid {F(s.CentroidHz, "0")} Hz)");
            var sm = Seam(m, buf);
            Chk(sm.Ratio <= MaxSeamRatio && sm.CurvRatio <= MaxSeamCurvRatio, $"music:{m} loop seam",
                $"wrap step {F(sm.ValueDelta, "0.00000")} vs largest interior step {F(sm.StepMax, "0.00000")} " +
                $"= ratio {F(sm.Ratio, "0.00")} (<= {F(MaxSeamRatio, "0.0")}); wrap bend " +
                $"{F(sm.SlopeDelta, "0.00000")} vs largest interior bend {F(sm.CurvMax, "0.00000")} " +
                $"= curv ratio {F(sm.CurvRatio, "0.00")} (<= {F(MaxSeamCurvRatio, "0.0")})");
        }

        // 11. THE REAL SEAM GUARANTEE — the bed must equal its own continuation.
        foreach (var m in new[] { "ambient", "combat" })
        {
            int n = MusicSecs * SR;
            var ext = BedRaw(m, n + SeamProbe);
            float worst = 0f; int worstI = 0;
            for (int i = 0; i < SeamProbe; i++)
            {
                float d = MathF.Abs(ext[n + i] - ext[i]);
                if (d > worst) { worst = d; worstI = i; }
            }
            Chk(worst <= MaxPeriodErr, $"music:{m} periodicity",
                $"worst |bed[N+i]-bed[i]| = {F(worst, "0.0000000")} at i={worstI} over a " +
                $"{SeamProbe}-sample probe (<= {F(MaxPeriodErr, "0.0000")}) — the loop equals its own continuation");
        }

        sb.Append(fails.Count == 0
            ? "AUDIOGATE: PASS"
            : $"AUDIOGATE: FAIL ({string.Join(", ", fails)})");
        return sb.ToString();
    }

    // ── RESONANCE A3: the numbers, one cue at a time, for the AUDIO CHECK screen ──────────
    //
    // DumpReport/GateReport measure everything at once and print a table. The audition screen
    // needs the SAME numbers per row, on a 60 fps draw, so it can put "that sounds thin" next
    // to "-26.4 dB RMS, 8% above 1 kHz" and turn a vague impression into a filed report.
    // Rendering + a 4096-pt Welch spectrum per cue is far too much to do per frame, so the
    // result is memoised; the screen warms the cache a few cues at a time (see Game.Audition).

    static readonly Dictionary<string, CueStats> _cueStatCache = new();

    /// The cue's MIX ROLE and the RMS window the budget holds it inside — the same category map
    /// and the same band table AUDIOGATE checks against. The audition screen prints the band next
    /// to the measured value so a level that has drifted out of its role is visible, not just
    /// audible. (Reuses CatOf, so "what counts as UI" stays one decision in one place.)
    public static (string role, float lo, float hi) CueBand(string id)
    {
        string cat = CatOf(id);
        foreach (var (c, lo, hi) in RmsBands) if (c == cat) return (cat, lo, hi);
        return (cat, -200f, 0f);
    }

    /// TRUE if `id`'s measurement is already computed (the screen shows a placeholder until it is).
    public static bool CueMeasured(string id) => _cueStatCache.ContainsKey(id);

    /// Measure one cue exactly as the gate does (render through the real mastering stage, then
    /// analyse the first `realN` samples). Memoised — repeated calls are a dictionary hit.
    /// Returns peak/rms in dBFS, the >1 kHz energy share as a 0..1 fraction, and the duration.
    public static (float peakDb, float rmsDb, float hiFrac, float durMs) CueMeasure(string id)
    {
        if (!_cueStatCache.TryGetValue(id, out var st))
        {
            BuildRecipes();
            if (!_recipes.ContainsKey(id)) return (-200f, -200f, 0f, 0f);
            var buf = RenderCueById(id, out int realN, out float durMs);
            st = Measure(id, buf, realN, durMs);
            _cueStatCache[id] = st;
        }
        // BHi is 1-5 kHz and BAir is >5 kHz; their sum is the ">1 kHz share" the A2 rebuild
        // was steered by (raw white noise sat at 94%, a real band-limited report near 50-70%).
        return (st.PeakDb, st.RmsDb, Util.Clamp(st.BHi + st.BAir, 0f, 1f), st.DurMs);
    }
}
