using UnityEngine;

namespace Cypher.Capture
{
    /// <summary>Shared by the Editor entry point (SliceCapture) and the Play-mode driver.</summary>
    public static class CapturePoses
    {
        public const string DirKey = "Cypher.Capture.Dir";
        public const string ScreenHeightKey = "Cypher.Capture.ScreenHeight";

        public struct Pose
        {
            public string Name;
            public Vector3 Position;
            public float Yaw, Pitch;
        }

        /// <summary>Camera poses in prototype coordinates (the city is mirrored in Unity's left-handed space).</summary>
        public static readonly Pose[] All =
        {
            new Pose { Name = "spawn_north", Position = new Vector3(0f, 1.9f, 0f), Yaw = 0f, Pitch = 0f },
            new Pose { Name = "spawn_east", Position = new Vector3(0f, 1.9f, 0f), Yaw = 90f, Pitch = 0f },
            new Pose { Name = "street_level", Position = new Vector3(-8f, 1.7f, -60f), Yaw = 0f, Pitch = -2f },
            new Pose { Name = "skyline", Position = new Vector3(-8f, 1.7f, -30f), Yaw = 30f, Pitch = 22f },
            new Pose { Name = "overview", Position = new Vector3(-8f, 140f, -190f), Yaw = 0f, Pitch = 38f },
            new Pose { Name = "north_close", Position = new Vector3(0f, 1.9f, 30f), Yaw = 0f, Pitch = 10f },
            new Pose { Name = "north_mid", Position = new Vector3(0f, 1.9f, 12f), Yaw = 0f, Pitch = 12f },
            new Pose { Name = "road_down", Position = new Vector3(0f, 1.7f, 31f), Yaw = 90f, Pitch = -12f },

            // The whole 7x7 city: the edge looking out (apron, then the skyline ring), a corner, the outer road, the longest avenue,
            // a wet street far from the plaza (its own reflection probe), and a high view of everything.
            new Pose { Name = "edge_out", Position = new Vector3(217f, 1.7f, 0f), Yaw = 90f, Pitch = 4f },
            new Pose { Name = "edge_corner", Position = new Vector3(214f, 1.7f, 214f), Yaw = 45f, Pitch = 4f },   // on the outer road, where the two outer roads meet
            new Pose { Name = "edge_along", Position = new Vector3(217f, 1.7f, -200f), Yaw = 0f, Pitch = 2f },
            new Pose { Name = "avenue_long", Position = new Vector3(31f, 1.7f, -217f), Yaw = 0f, Pitch = 0f },
            new Pose { Name = "far_district_wet", Position = new Vector3(-155f, 1.7f, 155f), Yaw = 200f, Pitch = -8f },
            new Pose { Name = "overview_whole", Position = new Vector3(0f, 240f, -270f), Yaw = 0f, Pitch = 48f },
        };
    }
}
