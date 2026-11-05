using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text.Json;
namespace WinSqueeze.Audio;

// Reads the samples our Squeezelite build shares (-v, see vendor/squeezelite/src/output_vis_win.c) and turns them into meter data ~30 times a second:
// VU levels (RMS with ~300 ms ballistics, in dB), peak levels for the bar graph, and 8 spectrum bands. Looks back by the output latency so meters match what you hear.
public sealed class VisualiserReader : IDisposable
{
    const int HeaderBytes = 32, FftSize = 1024, BandCount = 8;
    static readonly double[] bandEdges = { 40, 100, 250, 600, 1200, 2500, 5000, 10000, 18000 }; // Hz, roughly log-spaced like the hardware's bands
    public event Action<string>? LevelsReady;
    Thread? worker;
    volatile bool running;
    string mapName = "";
    int latencyMs = 200;
    double vuLeft, vuRight, peakLeft, peakRight;
    readonly double[] bandLevels = new double[BandCount];
    bool sentSilence;

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
        var samples = new short[FftSize * 2];
        var lastTick = Environment.TickCount64;
        while (running)
        {
            Thread.Sleep(33);
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
                Analyse(samples, rate > 0 ? rate : 44100, elapsedSeconds, live);
            }
            catch (Exception) { view?.Dispose(); sharedFile?.Dispose(); view = null; sharedFile = null; Thread.Sleep(1000); }
        }
        view?.Dispose(); sharedFile?.Dispose();
    }

    void Analyse(short[] samples, int rate, double elapsedSeconds, bool live)
    {
        // VU: RMS over the newest ~50 ms, integrated with a 300 ms time constant
        var rmsFrames = Math.Min(FftSize, rate / 20);
        double sumLeft = 0, sumRight = 0, maxLeft = 0, maxRight = 0;
        for (var frame = FftSize - rmsFrames; frame < FftSize; frame++)
        {
            double left = samples[frame * 2] / 32768.0, right = samples[frame * 2 + 1] / 32768.0;
            sumLeft += left * left; sumRight += right * right; maxLeft = Math.Max(maxLeft, Math.Abs(left)); maxRight = Math.Max(maxRight, Math.Abs(right));
        }
        var vuFollow = 1 - Math.Exp(-elapsedSeconds / 0.3);
        vuLeft += (Math.Sqrt(sumLeft / rmsFrames) - vuLeft) * vuFollow; vuRight += (Math.Sqrt(sumRight / rmsFrames) - vuRight) * vuFollow;
        // Bars: instant attack, ~1.5 s fall from full scale (in dB terms)
        var peakFall = Math.Pow(10, -elapsedSeconds * 30 / 20);
        peakLeft = Math.Max(maxLeft, peakLeft * peakFall); peakRight = Math.Max(maxRight, peakRight * peakFall);
        // Spectrum: Hann-windowed FFT of the mono mix, band energy in dB mapped to 0..1 over a 60 dB range
        var real = new double[FftSize]; var imaginary = new double[FftSize];
        for (var frame = 0; frame < FftSize; frame++) real[frame] = (samples[frame * 2] + samples[frame * 2 + 1]) / 65536.0 * (0.5 - 0.5 * Math.Cos(2 * Math.PI * frame / (FftSize - 1)));
        Fft(real, imaginary);
        var bandFollow = 1 - Math.Exp(-elapsedSeconds / 0.12);
        for (var band = 0; band < BandCount; band++)
        {
            int firstBin = Math.Max(1, (int)(bandEdges[band] * FftSize / rate)), lastBin = Math.Min(FftSize / 2 - 1, Math.Max(firstBin, (int)(bandEdges[band + 1] * FftSize / rate)));
            double energy = 0;
            for (var bin = firstBin; bin <= lastBin; bin++) energy = Math.Max(energy, real[bin] * real[bin] + imaginary[bin] * imaginary[bin]);
            var level = Math.Clamp((10 * Math.Log10(energy * 4 / FftSize + 1e-12) + 66) / 60, 0, 1);
            bandLevels[band] = level > bandLevels[band] ? level : bandLevels[band] + (level - bandLevels[band]) * bandFollow;
        }
        var silent = !live && vuLeft < 1e-4 && vuRight < 1e-4 && peakLeft < 1e-3 && peakRight < 1e-3 && bandLevels.All(level => level < 0.01);
        if (silent && sentSilence) return;
        sentSilence = silent;
        LevelsReady?.Invoke(JsonSerializer.Serialize(new { type = "levels", vu = new[] { Decibels(vuLeft), Decibels(vuRight) }, peak = new[] { Math.Round(peakLeft, 3), Math.Round(peakRight, 3) }, bands = bandLevels.Select(level => Math.Round(level, 3)) }));
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
