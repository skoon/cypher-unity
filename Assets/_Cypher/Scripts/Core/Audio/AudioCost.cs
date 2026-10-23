using System.Globalization;

namespace Cypher.Core
{
    /// <summary>
    /// What the sound costs: the milliseconds the audio thread spends on each device block in each piece (all of them run one after another on the one mixer thread, so their sum against the length
    /// of a block is the share of that thread the audio takes), and the milliseconds a frame of the main thread spends on the engine's own update.
    /// </summary>
    public readonly struct AudioCost
    {
        public readonly double BedMs, ScoreMs, CarsMs, MasterMs;
        /// <summary>How long a device block lasts, which is how long the audio thread has to do all of the above.</summary>
        public readonly double BlockMs;
        public readonly double MainThreadAverageMs, MainThreadMaxMs;
        /// <summary>The longest any one piece has taken over one device block: a spike here is what would make the sound crackle.</summary>
        public readonly double WorstPieceMs;

        public AudioCost(double bedMs, double scoreMs, double carsMs, double masterMs, double blockMs, double mainAverageMs, double mainMaxMs, double worstPieceMs = 0)
        {
            BedMs = bedMs; ScoreMs = scoreMs; CarsMs = carsMs; MasterMs = masterMs; BlockMs = blockMs;
            MainThreadAverageMs = mainAverageMs; MainThreadMaxMs = mainMaxMs; WorstPieceMs = worstPieceMs;
        }

        public double AudioThreadMs => BedMs + ScoreMs + CarsMs + MasterMs;

        /// <summary>The fraction of real time the audio thread spends on this game's synthesis; at 1 it would not keep up.</summary>
        public double AudioThreadShare => BlockMs > 0 ? AudioThreadMs / BlockMs : 0;

        public override string ToString() => string.Format(CultureInfo.InvariantCulture,
            "audio thread {0:0.00} ms of every {1:0.0} ms block ({2:0.0}%: bed {3:0.00}, score {4:0.00}, cars {5:0.00}, master {6:0.00}; worst single block {9:0.00}); main thread {7:0.000} ms a frame (worst {8:0.00})",
            AudioThreadMs, BlockMs, AudioThreadShare * 100, BedMs, ScoreMs, CarsMs, MasterMs, MainThreadAverageMs, MainThreadMaxMs, WorstPieceMs);
    }
}
