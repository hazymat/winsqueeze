using System.IO;
using System.IO.MemoryMappedFiles;
using WinSqueeze.Settings;
using WinSqueeze.Shell;
namespace WinSqueeze.Audio;

// Graphic equaliser (SPEC.md › Equaliser): designs the biquads for the current settings and sample rate and hands them to our
// Squeezelite through "Local\squeezelite-eq-<mac>" (see vendor/squeezelite/src/output_eq_win.c). Off = Squeezelite skips it (bit-perfect).
public sealed class Equaliser : IDisposable
{
    const int MaxStages = 12, CoefficientsOffset = 24, MapBytes = CoefficientsOffset + 2 * MaxStages * 5 * 4;
    public static readonly Dictionary<int, double[]> Frequencies = new()
    {
        [10] = new double[] { 31.5, 63, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 }, // octaves, as on classic hi-fi graphic EQs
        [7] = new double[] { 63, 160, 400, 1000, 2500, 6300, 16000 },                   // 1⅓-octave spacing
        [3] = new double[] { 100, 1000, 8000 },                                          // bass shelf, mid, treble shelf
    };
    // Q of a band at full boost: about an octave wide for 10 bands, broader as the bands spread out
    static readonly Dictionary<int, double> fullBoostQ = new() { [10] = 1.41, [7] = 1.0, [3] = 0.7 };

    MemoryMappedFile? sharedFile; MemoryMappedViewAccessor? view;
    string mapName = "";
    int writtenSequence = -1, writtenRate;
    bool dirty = true;
    readonly System.Threading.Timer timer;
    readonly object writeLock = new();
    public Func<int> CurrentRate { get; set; } = () => 0; // what Squeezelite is playing at (from the visualiser export)
    public EqualiserSettings Tuning { get; set; } = new();
    public double AutoPreampDb { get; private set; } // what AUTO would use, shown in the panel

    public Equaliser() { timer = new System.Threading.Timer(_ => Tick(), null, 500, 500); }

    public void Start(string playerMac) { mapName = "Local\\squeezelite-eq-" + playerMac.Replace(":", "").ToLowerInvariant(); Changed(); }
    public void Changed() { dirty = true; Tick(); }

    // Rewrite on changes, a new sample rate, or when Squeezelite restarted and cleared the block
    void Tick()
    {
        lock (writeLock)
        {
            try
            {
                if (mapName == "") return;
                if (view == null) { try { sharedFile = MemoryMappedFile.OpenExisting(mapName, MemoryMappedFileRights.ReadWrite); view = sharedFile.CreateViewAccessor(0, MapBytes, MemoryMappedFileAccess.ReadWrite); dirty = true; } catch (FileNotFoundException) { return; } }
                var rate = CurrentRate(); if (rate <= 0) rate = 44100;
                if (!dirty && rate == writtenRate && view.ReadInt32(0) == writtenSequence) return;
                Write(rate);
            }
            catch (Exception error) { CrashReporter.Write("Equaliser update failed", error, false); view?.Dispose(); sharedFile?.Dispose(); view = null; sharedFile = null; }
        }
    }

    void Write(int rate)
    {
        var tuning = Tuning;
        var (stages, preamp) = Design(tuning, rate);
        var sequence = view!.ReadInt32(0); if ((sequence & 1) != 0) sequence++;
        view.Write(0, sequence + 1); // odd: Squeezelite ignores the block until it's even again
        view.Write(4, tuning.Enabled ? 1 : 0); view.Write(8, rate); view.Write(12, stages[0].Length); view.Write(16, (float)preamp);
        for (var channel = 0; channel < 2; channel++)
            for (var stage = 0; stage < stages[channel].Length; stage++)
                for (var index = 0; index < 5; index++) view.Write(CoefficientsOffset + ((channel * MaxStages + stage) * 5 + index) * 4, (float)stages[channel][stage][index]);
        view.Write(0, sequence + 2);
        writtenSequence = sequence + 2; writtenRate = rate; dirty = false;
    }

    public static double[] GainsFor(EqualiserSettings tuning, char channel)
    {
        var count = Frequencies.ContainsKey(tuning.BandCount) ? tuning.BandCount : 10;
        var key = $"{count}{(tuning.Linked ? 'L' : channel)}";
        return tuning.Gains.TryGetValue(key, out var gains) && gains.Length == count ? gains : new double[count];
    }

    // Per channel: one biquad per band. Preamp: manual (PreampDb, default -6) or auto (keeps the loudest point of the curves at 0 dBFS); Squeezelite's limiter catches what still overshoots
    public (double[][][] stages, double preamp) Design(EqualiserSettings tuning, int rate)
    {
        var count = Frequencies.ContainsKey(tuning.BandCount) ? tuning.BandCount : 10;
        var stages = new double[2][][];
        var loudest = 1.0;
        for (var channel = 0; channel < 2; channel++)
        {
            var gains = GainsFor(tuning, channel == 0 ? 'L' : 'R');
            stages[channel] = new double[count][];
            for (var band = 0; band < count; band++) stages[channel][band] = Band(count, band, Math.Clamp(gains[band], -12, 12), tuning.Character == "precise", rate);
            loudest = Math.Max(loudest, PeakMagnitude(stages[channel], rate));
        }
        AutoPreampDb = Math.Round(-20 * Math.Log10(loudest), 1);
        if (tuning.AutoPreamp) return (stages, 1 / loudest);
        return (stages, Math.Pow(10, Math.Clamp(tuning.PreampDb, -18, 0) / 20));
    }

    static double[] Band(int count, int band, double gainDb, bool precise, int rate)
    {
        var frequency = Frequencies[count][band];
        if (Math.Abs(gainDb) < 0.05 || frequency >= rate * 0.45) return new double[] { 1, 0, 0, 0, 0 };
        if (count == 3 && band != 1) return Shelf(band == 2, frequency, gainDb, rate);
        // Vintage: like an analogue gyrator EQ, a band is broad at gentle settings and narrows towards full boost or cut
        var quality = precise ? fullBoostQ[count] : fullBoostQ[count] * (0.45 + 0.55 * Math.Min(1, Math.Abs(gainDb) / 10));
        return Peaking(frequency, gainDb, quality, rate);
    }

    // RBJ audio EQ cookbook filters, normalised to a0 = 1: b0 b1 b2 a1 a2
    static double[] Peaking(double frequency, double gainDb, double quality, int rate)
    {
        double amplitude = Math.Pow(10, gainDb / 40), omega = 2 * Math.PI * frequency / rate, alpha = Math.Sin(omega) / (2 * quality), cosine = Math.Cos(omega);
        var a0 = 1 + alpha / amplitude;
        return new[] { (1 + alpha * amplitude) / a0, -2 * cosine / a0, (1 - alpha * amplitude) / a0, -2 * cosine / a0, (1 - alpha / amplitude) / a0 };
    }

    static double[] Shelf(bool high, double frequency, double gainDb, int rate)
    {
        double amplitude = Math.Pow(10, gainDb / 40), omega = 2 * Math.PI * frequency / rate, cosine = Math.Cos(omega), alpha = Math.Sin(omega) / 2 * Math.Sqrt(2), root = 2 * Math.Sqrt(amplitude) * alpha;
        double sign = high ? -1 : 1, plus = amplitude + 1, minus = amplitude - 1;
        var a0 = plus + sign * minus * cosine + root;
        return high
            ? new[] { amplitude * (plus + minus * cosine + root) / a0, -2 * amplitude * (minus + plus * cosine) / a0, amplitude * (plus + minus * cosine - root) / a0, 2 * (minus - plus * cosine) / a0, (plus - minus * cosine - root) / a0 }
            : new[] { amplitude * (plus - minus * cosine + root) / a0, 2 * amplitude * (minus - plus * cosine) / a0, amplitude * (plus - minus * cosine - root) / a0, -2 * (minus + plus * cosine) / a0, (plus + minus * cosine - root) / a0 };
    }

    // Loudest point of the cascade's response, swept 20 Hz–20 kHz
    static double PeakMagnitude(double[][] stages, int rate)
    {
        var loudest = 0.0;
        for (var step = 0; step <= 240; step++)
        {
            var frequency = 20 * Math.Pow(1000, step / 240.0); if (frequency >= rate / 2.0) break;
            double omega = 2 * Math.PI * frequency / rate, magnitude = 1;
            foreach (var coefficients in stages)
            {
                double cos1 = Math.Cos(omega), sin1 = Math.Sin(omega), cos2 = Math.Cos(2 * omega), sin2 = Math.Sin(2 * omega);
                double numeratorReal = coefficients[0] + coefficients[1] * cos1 + coefficients[2] * cos2, numeratorImaginary = -(coefficients[1] * sin1 + coefficients[2] * sin2);
                double denominatorReal = 1 + coefficients[3] * cos1 + coefficients[4] * cos2, denominatorImaginary = -(coefficients[3] * sin1 + coefficients[4] * sin2);
                magnitude *= Math.Sqrt((numeratorReal * numeratorReal + numeratorImaginary * numeratorImaginary) / (denominatorReal * denominatorReal + denominatorImaginary * denominatorImaginary));
            }
            loudest = Math.Max(loudest, magnitude);
        }
        return loudest;
    }

    // Peak gain reduction from Squeezelite's safety limiter since the last read (dB, 0 = untouched); drives the CLIP lamp
    public double TakeLimiterReductionDb()
    {
        lock (writeLock)
        {
            try { if (view == null) return 0; var lowestGain = view.ReadSingle(20); view.Write(20, 1f); return lowestGain > 0 && lowestGain < 0.9999f ? Math.Round(-20 * Math.Log10(lowestGain), 1) : 0; }
            catch { return 0; }
        }
    }

    public void Dispose() { timer.Dispose(); view?.Dispose(); sharedFile?.Dispose(); }
}
