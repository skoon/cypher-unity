using Cypher.Core;
using UnityEngine;

namespace Cypher.Game
{
    /// <summary>
    /// Flies this transform along the title route (<see cref="TitleFlight"/>) while enabled; the game parks the camera under it
    /// during Boot, Loading and Title. The path and its sways are Core's; this only converts a pose into a transform.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TitleCamera : MonoBehaviour
    {
        /// <summary>A long frame (the first one after loading) must not make the camera jump half a block.</summary>
        const float MaxStepSeconds = 0.1f;

        readonly TitleFlight _flight = TitleFlight.Default();
        double _seconds;

        /// <summary>Seconds flown so far.</summary>
        public double Seconds => _seconds;

        public TitleFlight Flight => _flight;

        void OnEnable() => Place();

        /// <summary>Jump to a point in the flight, for screenshots and tests.</summary>
        public void SeekTo(double seconds)
        {
            if (seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds)) throw new System.ArgumentOutOfRangeException(nameof(seconds), seconds, "time must be finite and not negative");
            _seconds = seconds;
            Place();
        }

        void Update()
        {
            _seconds += Mathf.Min(Time.deltaTime, MaxStepSeconds);
            Place();
        }

        /// <summary>Put the transform at the pose for the current time.</summary>
        public void Place()
        {
            FlightPose p = _flight.PoseAt(_seconds);
            transform.SetPositionAndRotation(
                new Vector3((float)p.X, (float)p.Y, (float)p.Z),
                Quaternion.Euler((float)(-p.Pitch * Mathf.Rad2Deg), (float)(p.Yaw * Mathf.Rad2Deg), 0f));
        }
    }
}
