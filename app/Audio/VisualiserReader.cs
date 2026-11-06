using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text.Json;
using WinSqueeze.Settings;
namespace WinSqueeze.Audio;

// Reads the samples our Squeezelite build shares (-v, see vendor/squeezelite/src/output_vis_win.c) and turns them into meter data ~30 times a second:
// VU levels (RMS with ~300 ms ballistics, in dB), peak levels for the bar graph, and the 10 octave bands (the same analysis the EQ window shows), ~60 times a second. Looks back by the output latency so meters match what you hear.
public sealed class VisualiserReader : IDisposable
{
    const int HeaderBytes = 32, FftSize = 1024, AnalyserSize = 4096; // a long window resolves the low octaves
    public event Action<string>? LevelsReady;
    public event Action<string>? AnalyserReady; // raw octave dBFS per channel for the EQ window, while it's open
    public volatile bool AnalyserWanted;
    public int SampleRate { get; private set; }
    int analyserFrame;
    Thread? worker;
    volatile bool running;
    string mapName = "";
    int latencyMs = 200;
    double vuLeft, vuRight, peakLeft, peakRight;
    readonly double[] bandLevels = new double[10];
    bool sentSilence;
    double peakHeldLeft, peakHeldRight; // seconds each bar has held its peak
    public MeterSettings Tuning { get; set; } = new(); // Settings › Meters, read every frame so changes show live

    public void Start(string playerMac, int outputLatencyMs)
    {
        mapName = "Local\\squeezelite-vis-" + playerMac.Replace(":", "").ToLowerInvariant();
        latencyMs = outputLatencyMs;
        if (worker != null) return;
        running = true;
        worker = new Thread(Run) { IsBackground = true, Name = "Visualiser", Priority = ThreadPriority.BelowNormal };
        worker.Start();
    }

    void Run()
    {
        MemoryMappedFile? sharedFile = null; MemoryMappedViewAccessor? view = null;
        var samples = new short[AnalyserSize * 2];
        var lastTick = Environment.TickCount64;
        while (running)
        {
            Thread.Sleep(10); // the Windows timer rounds this up to one ~15.6 ms tick: about 60 updates a second
            try
            {
                if (view == null) { try { sharedFile = MemoryMappedFile.OpenExisting(mapName, MemoryMappedFileRights.Read); view = sharedFile.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read); } catch (FileNotFoundException) { Thread.Sleep(1000); continue; } }
                var now = Environment.TickCount64; var elapsedSeconds = (now - lastTick) / 1000.0; lastTick = now;
                // Seqlock read: retry if the writer was mid-update
                int bufferSize = 0, rate = 0; bool live = false;
                for (var attempt = 0; attempt < 4; attempt++)
                {
                    var sequenceBefore = view.ReadInt32(0);
                    if ((sequenceBefore & 1) != 0) continue;
                    bufferSize = view.ReadInt32(4); var writeIndex = view.ReadInt32(8); live = view.ReadInt32(12) != 0; rate = view.ReadInt32(16); var updatedMs = view.ReadInt64(24);
                    if (bufferSize <= 0 || rate <= 0) break;
                    live = live && now - updatedMs < 1000;
                    // The window ends where the speaker is now: behind the write position by the output latency
                    var latencySamples = (int)((long)latencyMs * rate / 1000) * 2;
                    var endIndex = ((writeIndex - Math.Min(latencySamples, bufferSize - samples.Length)) % bufferSize + bufferSize) % bufferSize & ~1;
                    var startIndex = ((endIndex - samples.Length) % bufferSize + bufferSize) % bufferSize;
                    var firstPart = Math.Min(samples.Length, bufferSize - startIndex);
                    view.ReadArray(HeaderBytes + startIndex * 2L, samples, 0, firstPart);
                    if (firstPart < samples.Length) view.ReadArray(HeaderBytes, samples, firstPart, samples.Length - firstPart);
                    if (view.ReadInt32(0) == sequenceBefore) break;
                }
                if (!live) Array.Clear(samples);
                if (rate > 0) SampleRate = rate;
                var octaves = AnalyseOctaves(samples, rate > 0 ? rate : 44100);
                Analyse(samples, rate > 0 ? rate : 44100, octaves, elapsedSeconds, live);
                if (AnalyserWanted && ++analyserFrame % 2 == 0) AnalyserReady?.Invoke(JsonSerializer.Serialize(new { type = "eqLevels", left = octaves[0], right = octaves[1] })); // ~30 a second is plenty
            }
            catch (Exception) { view?.Dispose(); sharedFile?.Dispose(); view = null; sharedFile = null; Thread.Sleep(1000); }
        }
        view?.Dispose(); sharedFile?.Dispose();
    }

    void Analyse(short[] samples, int rate, double[][] octaves, double elapsedSeconds, bool live)
    {
        // VU: RMS over the newest ~50 ms, integrated with a 300 ms time constant
        const int newest = AnalyserSize - FftSize; // frame where the newest FftSize frames start
        var rmsFrames = Math.Min(FftSize, rate / 20);
        double sumLeft = 0, sumRight = 0, maxLeft = 0, maxRight = 0;
        for (var frame = FftSize - rmsFrames; frame < FftSize; frame++)
        {
            double left = samples[(newest + frame) * 2] / 32768.0, right = samples[(newest + frame) * 2 + 1] / 32768.0;
            sumLeft += left * left; sumRight += right * right; maxLeft = Math.Max(maxLeft, Math.Abs(left)); maxRight = Math.Max(maxRight, Math.Abs(right));
        }
        var tuning = Tuning;
        var vuFollow = 1 - Math.Exp(-elapsedSeconds / Math.Max(0.02, tuning.VuIntegrationMs / 1000));
        vuLeft += (Math.Sqrt(sumLeft / rmsFrames) - vuLeft) * vuFollow; vuRight += (Math.Sqrt(sumRight / rmsFrames) - vuRight) * vuFollow;
        // Bars: instant attack, optional hold, then a steady fall in dB
        var peakFall = Math.Pow(10, -elapsedSeconds * 30 / 20);
        (peakLeft, peakHeldLeft) = FollowPeak(maxLeft, peakLeft, peakHeldLeft, peakFall, elapsedSeconds, 0);
        (peakRight, peakHeldRight) = FollowPeak(maxRight, peakRight, peakHeldRight, peakFall, elapsedSeconds, 0);
        // Spectrum: the octaves of both channels power-averaged, on the EQ window's scale (+3 dB/octave tilt about 1 kHz, 3..30 over -33..-6 dBFS)
        var bandFollow = 1 - Math.Exp(-elapsedSeconds / Math.Max(0.02, tuning.SpectrumFallMs / 1000)); // instant rise, then the fall from Settings › Meters
        for (var band = 0; band < bandLevels.Length; band++)
        {
            var decibels = 10 * Math.Log10((Math.Pow(10, octaves[0][band] / 10) + Math.Pow(10, octaves[1][band] / 10)) / 2 + 1e-12);
            var level = Math.Clamp((decibels + 3 * Math.Log2(octaveCentres[band] / 1000) + 36) / 30, 0, 1);
            bandLevels[band] = level > bandLevels[band] ? level : bandLevels[band] + (level - bandLevels[band]) * bandFollow;
        }
        var silent = !live && vuLeft < 1e-4 && vuRight < 1e-4 && peakLeft < 1e-3 && peakRight < 1e-3 && bandLevels.All(level => level < 0.01);
        if (silent && sentSilence) return;
        sentSilence = silent;
        LevelsReady?.Invoke(JsonSerializer.Serialize(new { type = "levels", vu = new[] { Decibels(vuLeft), Decibels(vuRight) }, peak = new[] { Math.Round(peakLeft, 3), Math.Round(peakRight, 3) }, bands = bandLevels.Select(level => Math.Round(level, 3)) }));
    }

    // Per channel, the energy in each octave band 31.5 Hz–16 kHz as dBFS (raw; the EQ window applies its own decay)
    static readonly double[] octaveCentres = { 31.5, 63, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };
    static double[][] AnalyseOctaves(short[] samples, int rate)
    {
        var levels = new double[2][];
        for (var channel = 0; channel < 2; channel++)
        {
            var real = new double[AnalyserSize]; var imaginary = new double[AnalyserSize];
            for (var frame = 0; frame < AnalyserSize; frame++) real[frame] = samples[frame * 2 + channel] / 32768.0 * (0.5 - 0.5 * Math.Cos(2 * Math.PI * frame / (AnalyserSize - 1)));
            Fft(real, imaginary);
            levels[channel] = octaveCentres.Select(centre =>
            {
                int firstBin = Math.Max(1, (int)Math.Round(centre / Math.Sqrt(2) * AnalyserSize / rate)), lastBin = Math.Min(AnalyserSize / 2 - 1, Math.Max(firstBin, (int)Math.Round(centre * Math.Sqrt(2) * AnalyserSize / rate)));
                double energy = 0;
                for (var bin = firstBin; bin <= lastBin; bin++) energy += real[bin] * real[bin] + imaginary[bin] * imaginary[bin];
                // A full-scale sine sums to (N/4)² × 1.5 (Hann) across its bins
                return Math.Round(10 * Math.Log10(energy * 16.0 / (1.5 * AnalyserSize * AnalyserSize) + 1e-12), 1);
            }).ToArray();
        }
        return levels;
    }

    static (double level, double held) FollowPeak(double current, double level, double held, double fall, double elapsedSeconds, double holdSeconds)
    {
        if (current >= level) return (current, 0);
        held += elapsedSeconds;
        return held < holdSeconds ? (level, held) : (Math.Max(current, level * fall), held);
    }

    static double Decibels(double amplitude) => Math.Round(20 * Math.Log10(amplitude + 1e-9), 1);

    // In-place radix-2 FFT
    static void Fft(double[] real, double[] imaginary)
    {
        var count = real.Length;
        for (int index = 1, reversed = 0; index < count; index++)
        {
            var bit = count >> 1;
            for (; (reversed & bit) != 0; bit >>= 1) reversed ^= bit;
            reversed ^= bit;
            if (index < reversed) { (real[index], real[reversed]) = (real[reversed], real[index]); (imaginary[index], imaginary[reversed]) = (imaginary[reversed], imaginary[index]); }
        }
        for (var length = 2; length <= count; length <<= 1)
        {
            var angle = -2 * Math.PI / length;
            for (var start = 0; start < count; start += length)
                for (var offset = 0; offset < length / 2; offset++)
                {
                    double twiddleReal = Math.Cos(angle * offset), twiddleImaginary = Math.Sin(angle * offset);
                    int even = start + offset, odd = even + length / 2;
                    double oddReal = real[odd] * twiddleReal - imaginary[odd] * twiddleImaginary, oddImaginary = real[odd] * twiddleImaginary + imaginary[odd] * twiddleReal;
                    real[odd] = real[even] - oddReal; imaginary[odd] = imaginary[even] - oddImaginary; real[even] += oddReal; imaginary[even] += oddImaginary;
                }
        }
    }

    public void Dispose() { running = false; }
}
