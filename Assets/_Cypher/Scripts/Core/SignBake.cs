using System;
using System.Diagnostics;

namespace Cypher.Core
{
    /// <summary>
    /// The pixels the signage needs: the sign lettering atlas and the board artwork atlas. Pure computation that nothing else touches, so
    /// the loading code runs it on a worker thread while the main thread builds the city.
    /// </summary>
    public sealed class SignBake
    {
        public NeonAtlasLayout Layout { get; private set; }

        /// <summary>The lettering mask, single channel, <see cref="NeonAtlas.Size"/> squared, first row at the bottom.</summary>
        public byte[] SignAtlas { get; private set; }

        /// <summary>The ad panels, RGBA, <see cref="BoardArt.Size"/> squared, first row at the bottom.</summary>
        public byte[] BoardAtlas { get; private set; }

        public double SignMilliseconds { get; private set; }
        public double BoardMilliseconds { get; private set; }

        public static SignBake Run(NeonAtlasLayout layout = null)
        {
            layout = layout ?? NeonAtlas.Layout();
            var bake = new SignBake { Layout = layout };
            var sw = Stopwatch.StartNew();
            bake.SignAtlas = NeonAtlas.Render(layout);
            bake.SignMilliseconds = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            bake.BoardAtlas = BoardArt.Render();
            bake.BoardMilliseconds = sw.Elapsed.TotalMilliseconds;
            return bake;
        }
    }
}
