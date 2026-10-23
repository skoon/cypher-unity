using System;

namespace Cypher.Core
{
    /// <summary>
    /// Web Audio's dynamics compressor as Chromium makes it: a feed-forward, peak-detecting compressor with a soft knee, an attack and a release that adapts to how hard it is working, a
    /// six millisecond look-ahead, and a make-up gain worked out from the curve. The prototype's master chain is two of these (a gentle glue stage and a fast limiter), and the mix was
    /// balanced through them, so the game's has to behave the same: including the make-up gain, which lifts everything quiet by several decibels. Channels are linked: the loudest of them
    /// drives the gain for all.
    /// </summary>
    public sealed class DynamicsCompressor
    {
        const int MaxPreDelayFrames = 1024;
        const int PreDelayMask = MaxPreDelayFrames - 1;
        const int DivisionFrames = 32;

        // the four points the adaptive release curve passes through (fractions of the release time at 15, 10, 5 and 0 dB of compression)
        const double Zone1 = 0.09, Zone2 = 0.16, Zone3 = 0.42, Zone4 = 0.98;
        const double PreDelaySeconds = 0.006;
        const double SaturationReleaseSeconds = 0.0025;

        readonly int _rate;
        readonly int _channels;
        readonly float[][] _preDelay;
        int _preDelayRead, _preDelayWrite;
        double _detectorAverage;
        double _compressorGain = 1;
        double _maxAttenuationDb;
        int _stretchLeft;
        double _scaledDesiredGain, _envelopeRate;

        double _thresholdDb, _kneeDb, _ratio;
        double _linearThreshold, _kneeThreshold, _kneeThresholdDb, _yKneeThresholdDb, _slope, _k;

        public DynamicsCompressor(int sampleRate, int channels = 2)
        {
            if (sampleRate < 8000) throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "not a sample rate");
            if (channels < 1 || channels > 2) throw new ArgumentOutOfRangeException(nameof(channels), channels, "one or two channels");
            _rate = sampleRate;
            _channels = channels;
            _preDelay = new float[channels][];
            for (int c = 0; c < channels; c++) _preDelay[c] = new float[MaxPreDelayFrames];
            _preDelayWrite = 0;
            _preDelayRead = (int)(MaxPreDelayFrames - Math.Round(PreDelaySeconds * sampleRate)) & PreDelayMask;
            Threshold = -24;
            Knee = 30;
            Ratio = 12;
            Attack = 0.003;
            Release = 0.25;
        }

        public double Threshold { get; set; }
        public double Knee { get; set; }
        public double Ratio { get; set; }
        public double Attack { get; set; }
        public double Release { get; set; }

        /// <summary>The delay the look-ahead puts on the signal.</summary>
        public int LatencyFrames => (int)Math.Round(PreDelaySeconds * _rate);

        static double DbToLinear(double db) => Math.Pow(10, 0.05 * db);

        static double LinearToDb(double x) => 20 * Math.Log10(x);

        double KneeCurve(double x, double k)
        {
            // linear up to the threshold, then an exponential curve
            if (x < _linearThreshold) return x;
            return _linearThreshold + (1 - Math.Exp(-k * (x - _linearThreshold))) / k;
        }

        double SlopeAt(double x, double k)
        {
            if (x < _linearThreshold) return 1;
            double x2 = x * 1.001;
            double xDb = LinearToDb(x), x2Db = LinearToDb(x2);
            double yDb = LinearToDb(KneeCurve(x, k)), y2Db = LinearToDb(KneeCurve(x2, k));
            return (y2Db - yDb) / (x2Db - xDb);
        }

        double KAtSlope(double desiredSlope)
        {
            double x = DbToLinear(_thresholdDb + _kneeDb);
            double minK = 0.1, maxK = 10000, k = 5;
            for (int i = 0; i < 15; i++)
            {
                // a high k approaches a slope of zero faster
                if (SlopeAt(x, k) < desiredSlope) maxK = k;
                else minK = k;
                k = Math.Sqrt(minK * maxK);
            }
            return k;
        }

        void UpdateStaticCurve()
        {
            _thresholdDb = Threshold;
            _kneeDb = Knee;
            _ratio = Ratio;
            _slope = 1 / _ratio;
            _linearThreshold = DbToLinear(_thresholdDb);
            _k = KAtSlope(1 / _ratio);
            _kneeThresholdDb = _thresholdDb + _kneeDb;
            _kneeThreshold = DbToLinear(_kneeThresholdDb);
            _yKneeThresholdDb = LinearToDb(KneeCurve(_kneeThreshold, _k));
        }

        /// <summary>The curve: a signal's level in, the level the compressor would give it at steady state.</summary>
        double Saturate(double x)
        {
            if (x < _linearThreshold) return x;
            if (x < _kneeThreshold) return KneeCurve(x, _k);
            double yDb = _yKneeThresholdDb + _slope * (LinearToDb(x) - _kneeThresholdDb);
            return DbToLinear(yDb);
        }

        /// <summary>The make-up gain Web Audio applies: the loss at full scale, restored by its 0.6 power.</summary>
        public double MakeupGain()
        {
            UpdateStaticCurve();
            double fullRangeGain = Saturate(1);
            return Math.Pow(1 / fullRangeGain, 0.6);
        }

        /// <summary>Compress a block in place. A mono compressor takes one array; a stereo one two (the same length).</summary>
        public void Process(float[] left, float[] right, int frames)
        {
            if (_channels == 2 && right == null) throw new ArgumentNullException(nameof(right));
            UpdateStaticCurve();
            double fullRangeGain = Saturate(1);
            double masterLinearGain = Math.Pow(1 / fullRangeGain, 0.6);

            double attackFrames = Math.Max(0.001, Attack) * _rate;
            double releaseFrames = _rate * Release;
            double satReleaseFrames = SaturationReleaseSeconds * _rate;
            // a signal the curve leaves alone has no attenuation, which the detector takes as the least it ever counts (2 dB): that rate is the same for every such sample
            double quietReleaseRate = DbToLinear(2 / satReleaseFrames) - 1;

            double y1 = releaseFrames * Zone1, y2 = releaseFrames * Zone2, y3 = releaseFrames * Zone3, y4 = releaseFrames * Zone4;
            double kA = 0.9999999999999998 * y1 + 1.8432219684323923e-16 * y2 - 1.9373394351676423e-16 * y3 + 8.824516011816245e-18 * y4;
            double kB = -1.5788320352845888 * y1 + 2.3305837032074286 * y2 - 0.9141194204840429 * y3 + 0.1623677525612032 * y4;
            double kC = 0.5334142869106424 * y1 - 1.272736789213631 * y2 + 0.9258856042207512 * y3 - 0.18656310191776226 * y4;
            double kD = 0.08783463138207234 * y1 - 0.1694162967925622 * y2 + 0.08588057951595272 * y3 - 0.00429891410546283 * y4;
            double kE = -0.042416883008123074 * y1 + 0.1115693827987602 * y2 - 0.09764676325265872 * y3 + 0.028494263462021576 * y4;

            int frameIndex = 0;
            while (frameIndex < frames)
            {
                // the gain is worked out for a stretch of 32 frames at a time; a stretch is carried over from one block to the next, so the sound does not depend on how it is cut into blocks
                if (_stretchLeft == 0)
                {
                    if (double.IsNaN(_detectorAverage) || double.IsInfinity(_detectorAverage)) _detectorAverage = 1;
                    double desiredGain = _detectorAverage;
                    // pre-warp so the sine warp below gives the desired gain
                    _scaledDesiredGain = Math.Asin(desiredGain) / (0.5 * Math.PI);

                    bool isReleasing = _scaledDesiredGain > _compressorGain;
                    double compressionDiffDb = LinearToDb(_compressorGain / _scaledDesiredGain);
                    if (isReleasing)
                    {
                        _maxAttenuationDb = 0;
                        if (double.IsNaN(compressionDiffDb) || double.IsInfinity(compressionDiffDb)) compressionDiffDb = -1;
                        // adaptive release: the harder it is compressing, the faster it lets go
                        double x = Math.Min(0, Math.Max(-12, compressionDiffDb));
                        x = 0.25 * (x + 12);
                        double x2 = x * x, x3 = x2 * x, x4 = x2 * x2;
                        double release = kA + kB * x + kC * x2 + kD * x3 + kE * x4;
                        double dbPerFrame = 5 / release;
                        _envelopeRate = DbToLinear(dbPerFrame);
                    }
                    else
                    {
                        if (double.IsNaN(compressionDiffDb) || double.IsInfinity(compressionDiffDb)) compressionDiffDb = 1;
                        // while attacking, the rate follows the largest attenuation met so far
                        if (_maxAttenuationDb < compressionDiffDb) _maxAttenuationDb = compressionDiffDb;
                        double effAttenuationDb = Math.Max(0.5, _maxAttenuationDb);
                        double x = 0.25 / effAttenuationDb;
                        _envelopeRate = 1 - Math.Pow(x, 1 / attackFrames);
                    }
                    _stretchLeft = DivisionFrames;
                }
                int stretch = Math.Min(_stretchLeft, frames - frameIndex);
                double scaledDesiredGain = _scaledDesiredGain;
                double envelopeRate = _envelopeRate;

                int readIndex = _preDelayRead, writeIndex = _preDelayWrite;
                double detectorAverage = _detectorAverage;
                double compressorGain = _compressorGain;
                for (int i = 0; i < stretch; i++)
                {
                    float l = left[frameIndex];
                    float r = _channels == 2 ? right[frameIndex] : 0;
                    _preDelay[0][writeIndex] = l;
                    if (_channels == 2) _preDelay[1][writeIndex] = r;
                    double compressorInput = Math.Abs(l);
                    if (_channels == 2) compressorInput = Math.Max(compressorInput, Math.Abs(r));

                    double absInput = compressorInput;
                    double shapedInput = Saturate(absInput);
                    double attenuation = absInput <= 0.0001 ? 1 : shapedInput / absInput;
                    double satReleaseRate = attenuation >= 1 ? quietReleaseRate : DbToLinear(Math.Max(2, -LinearToDb(attenuation)) / satReleaseFrames) - 1;
                    bool isRelease = attenuation > detectorAverage;
                    double rate = isRelease ? satReleaseRate : 1;
                    detectorAverage += (attenuation - detectorAverage) * rate;
                    detectorAverage = Math.Min(1, detectorAverage);
                    if (double.IsNaN(detectorAverage) || double.IsInfinity(detectorAverage)) detectorAverage = 1;

                    if (envelopeRate < 1)
                    {
                        // attack: reduce the gain to the desired
                        compressorGain += (scaledDesiredGain - compressorGain) * envelopeRate;
                    }
                    else
                    {
                        // release: climb exponentially back to 1
                        compressorGain *= envelopeRate;
                        compressorGain = Math.Min(1, compressorGain);
                    }

                    // warp the gain to smooth out the sharp exponential transition points
                    double postWarp = compressorGain >= 1 ? 1 : Math.Sin(0.5 * Math.PI * compressorGain);
                    double totalGain = masterLinearGain * postWarp;
                    left[frameIndex] = (float)(_preDelay[0][readIndex] * totalGain);
                    if (_channels == 2) right[frameIndex] = (float)(_preDelay[1][readIndex] * totalGain);

                    frameIndex++;
                    readIndex = (readIndex + 1) & PreDelayMask;
                    writeIndex = (writeIndex + 1) & PreDelayMask;
                }
                _preDelayRead = readIndex;
                _preDelayWrite = writeIndex;
                _detectorAverage = detectorAverage;
                _compressorGain = compressorGain;
                _stretchLeft -= stretch;
            }
        }

        public void Reset()
        {
            foreach (float[] b in _preDelay) Array.Clear(b, 0, b.Length);
            _preDelayWrite = 0;
            _preDelayRead = (int)(MaxPreDelayFrames - Math.Round(PreDelaySeconds * _rate)) & PreDelayMask;
            _detectorAverage = 0;
            _compressorGain = 1;
            _maxAttenuationDb = 0;
            _stretchLeft = 0;
        }
    }
}
