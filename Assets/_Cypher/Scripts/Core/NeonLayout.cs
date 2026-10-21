using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>The look of one emissive quad. The shader draws the profile procedurally.</summary>
    public enum NeonShape
    {
        /// <summary>Glass tube: a bright core with a tight halo across the quad's height.</summary>
        Tube = 0,

        /// <summary>The same with a wider, softer halo.</summary>
        TubeSoft = 1,

        /// <summary>Backlit sign face with procedural lettering bars, a frame, and a bright core.</summary>
        Panel = 2,

        /// <summary>A row of marquee bulbs along the quad's width.</summary>
        Bulbs = 3,

        /// <summary>Soft radial glow: street pools (facing up) and washes on walls.</summary>
        Radial = 4,
    }

    /// <summary>
    /// One additive emissive quad, ported from Neon._quad. Faces along (Nx, 0, Nz), or straight up when
    /// FacingUp is set (street pools). Colour is the linear tint; Gain scales the whole quad's output.
    /// </summary>
    public struct NeonQuad
    {
        public int Gx, Gz;
        public double X, Y, Z;
        public double Nx, Nz;
        public bool FacingUp;
        public double Width, Height;

        /// <summary>Radians about the facing axis; a quarter turn makes a vertical tube.</summary>
        public double Roll;

        public NeonShape Shape;
        public Rgb Colour;
        public double Gain;

        /// <summary>0 steady hum, 1 dying tube, 2 slow breathe, 3 advert blink, 4 sputter.</summary>
        public int FlickerMode;

        public double Seed;

        /// <summary>Tubes: chase segments along the length (0 = none). Panels and bulbs: ignored by the planner (the mesher fills in aspect or bulb count).</summary>
        public double Chase;
    }

    /// <summary>
    /// A dark housing box behind a sign or fitting (the prototype's BoxBatch). The local Z axis points
    /// along (DirX, 0, DirZ); local X is that direction turned a quarter turn; local Y is up.
    /// </summary>
    public struct NeonBox
    {
        public int Gx, Gz;
        public double X, Y, Z;
        public double DirX, DirZ;
        public double SizeX, SizeY, SizeZ;
    }

    /// <summary>A facade plane the neon planner can hang things on (the prototype's city.walls entries).</summary>
    public struct NeonWall
    {
        public int Gx, Gz;
        public double Ox, Oz;
        public double Tx, Tz;
        public double Nx, Nz;
        public double Length;
        public double Mx, Mz;
        public double Y0, Y1;

        /// <summary>Faces open street.</summary>
        public bool Street;

        /// <summary>Faces a gap between lots (an alley) rather than a street.</summary>
        public bool Alley;

        /// <summary>Belongs to a volume that stands on the ground.</summary>
        public bool Ground;
    }

    public sealed class NeonLayout
    {
        public readonly List<NeonQuad> Quads = new List<NeonQuad>();
        public readonly List<NeonBox> Boxes = new List<NeonBox>();
    }
}
