namespace Cypher.Core
{
    /// <summary>
    /// Grid constants and queries, ported from src/world/City.js. Values are the
    /// prototype's contract: later systems (crowd, traffic, quests) line up on them.
    /// All distances are metres. Coordinates are the prototype's, unchanged, so
    /// ported locations and layouts drop in as-is. Unity is left-handed, so the
    /// city is the mirror image of the prototype; triangle winding needs no change
    /// (front face = direction of cross(b-a, c-a) in both engines).
    /// </summary>
    public static class CityLayout
    {
        public const double Block = 46;               // buildable block footprint
        public const double Road = 16;                // road corridor width
        public const double Cell = Block + Road;      // 62
        public const int Grid = 7;                    // Grid x Grid blocks
        public const double Half = Grid * Cell / 2;

        /// <summary>Origin shift so the plaza block (Grid/2, Grid/2) is centred on 0,0.</summary>
        public const double Base = -Half - 8;

        /// <summary>Block centre offset inside a cell.</summary>
        public const double BlockOffset = Road + Block / 2;

        public const double FloorHeight = 3.55;       // storey height; everything snaps to it
        public const double WindowPitch = 2.35;       // pane pitch
        public const int WindowAtlas = 32;            // window atlas is Atlas x Atlas panes
        public const double SidewalkHeight = 0.185;   // kerb height (player autostep is 0.42)
        public const double SidewalkPad = 2.5;        // sidewalk overhang beyond the block footprint

        public const int PlazaIndex = Grid >> 1;

        public static double CellCentre(int index) => Base + index * Cell + BlockOffset;

        /// <summary>Middle of the road corridor that opens grid cell <paramref name="index"/> (each cell's road is its low edge).</summary>
        public static double RoadCentre(int index) => Base + index * Cell + Road / 2;

        /// <summary>True when a world-space XZ point is in a road corridor rather than a block.</summary>
        public static bool IsOnRoad(double x, double z)
        {
            double lx = Mod(x - Base, Cell);
            double lz = Mod(z - Base, Cell);
            return lx < Road || lz < Road;
        }

        /// <summary>Grid index of the cell containing a world coordinate; may be outside 0..Grid-1.</summary>
        public static int CellIndex(double coord) => (int)System.Math.Floor((coord - Base) / Cell);

        static double Mod(double v, double m) => ((v % m) + m) % m;
    }
}
