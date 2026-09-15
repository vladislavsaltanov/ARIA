namespace Aria.Audio;

public sealed class IntegratedLufsScan
{
    private const double AbsoluteGateLufs = -70.0;
    private const double RelativeGateLu = -10.0;
    private const double ReferenceOffsetDb = -0.691;
    private const int HopsPerBlock = 4;

    private readonly int _channels;
    private readonly int _hopFrames;
    private readonly double[] _ring;
    private readonly BiquadFilter[] _pre;
    private readonly BiquadFilter[] _post;
    private readonly List<double> _blocks = [];
    private int _slot;
    private int _inHop;
    private int _hops;

    public IntegratedLufsScan(int channels, int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        _channels = channels;
        _hopFrames = Math.Max(1, sampleRate / 10);
        _ring = new double[channels * HopsPerBlock];
        _pre = new BiquadFilter[channels];
        _post = new BiquadFilter[channels];
        for (var channel = 0; channel < channels; channel++)
        {
            _pre[channel] = BiquadFilter.KWeightingPreFilter(sampleRate);
            _post[channel] = BiquadFilter.KWeightingHighPass(sampleRate);
        }
    }

    public void Feed(ReadOnlySpan<float> samples, int channels)
    {
        if (channels != _channels)
        {
            throw new ArgumentException("Channel count does not match the scan.", nameof(channels));
        }
        var frames = samples.Length / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            for (var channel = 0; channel < channels; channel++)
            {
                var filtered = _post[channel].ProcessSample(_pre[channel].ProcessSample(samples[frame * channels + channel]));
                _ring[channel * HopsPerBlock + _slot] += filtered * filtered;
            }
            if (++_inHop == _hopFrames)
            {
                CloseHop();
            }
        }
    }

    public double Result
    {
        get
        {
            if (_blocks.Count == 0)
            {
                return double.NaN;
            }
            var mean = 0.0;
            foreach (var block in _blocks)
            {
                mean += block;
            }
            mean /= _blocks.Count;
            var relativeGate = ReferenceOffsetDb + 10.0 * Math.Log10(mean) + RelativeGateLu;
            var gated = 0.0;
            var kept = 0;
            foreach (var block in _blocks)
            {
                var loudness = ReferenceOffsetDb + 10.0 * Math.Log10(block);
                if (loudness >= relativeGate)
                {
                    gated += block;
                    kept++;
                }
            }
            if (kept == 0)
            {
                return double.NaN;
            }
            return ReferenceOffsetDb + 10.0 * Math.Log10(gated / kept);
        }
    }

    private void CloseHop()
    {
        _inHop = 0;
        _slot = (_slot + 1) % HopsPerBlock;
        for (var channel = 0; channel < _channels; channel++)
        {
            _ring[channel * HopsPerBlock + _slot] = 0.0;
        }
        _hops++;
        if (_hops < HopsPerBlock)
        {
            return;
        }
        var energy = 0.0;
        for (var channel = 0; channel < _channels; channel++)
        {
            for (var hop = 0; hop < HopsPerBlock; hop++)
            {
                energy += _ring[channel * HopsPerBlock + hop];
            }
        }
        energy /= HopsPerBlock * _hopFrames;
        if (energy > 0.0 && ReferenceOffsetDb + 10.0 * Math.Log10(energy) >= AbsoluteGateLufs)
        {
            _blocks.Add(energy);
        }
    }
}
