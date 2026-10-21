using System;

namespace Cypher.Core
{
    public readonly struct Vec3
    {
        public readonly double X, Y, Z;

        public Vec3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    /// <summary>One frame of player intent, already read from whatever device produced it.</summary>
    public readonly struct PlayerCommand
    {
        /// <summary>-1..1, +1 is forward.</summary>
        public readonly double Forward;

        /// <summary>-1..1, +1 is right.</summary>
        public readonly double Strafe;

        public readonly bool Sprint;
        public readonly bool Crouch;

        /// <summary>True only on the frame the jump button went down.</summary>
        public readonly bool JumpPressed;

        /// <summary>Look input in device units (mouse pixels). +X turns right, +Y looks up.</summary>
        public readonly double LookX, LookY;

        public PlayerCommand(double forward, double strafe, bool sprint, bool crouch, bool jumpPressed, double lookX = 0, double lookY = 0)
        {
            Forward = forward;
            Strafe = strafe;
            Sprint = sprint;
            Crouch = crouch;
            JumpPressed = jumpPressed;
            LookX = lookX;
            LookY = lookY;
        }
    }

    /// <summary>
    /// Kinematic first-person movement model, ported from the web prototype's Player.fixedUpdate:
    /// horizontal velocity chases the wish vector, vertical is ballistic, jumps are forgiving
    /// (coyote time plus an input buffer), and standing up needs headroom. It does not touch
    /// physics: the caller moves a character controller by the returned displacement and then
    /// reports what happened with <see cref="ReportResult"/>.
    ///
    /// Frame: +Y up, yaw 0 faces +Z, positive yaw turns toward +X (Unity's convention).
    /// The origin of the character is its feet.
    /// </summary>
    public sealed class PlayerMotor
    {
        public const double EyeStand = 1.68;
        public const double EyeCrouch = 1.02;
        public const double Radius = 0.34;
        public const double HeightStand = 1.8;
        public const double HeightCrouch = 1.15;

        public const double WalkSpeed = 4.1;
        public const double SprintSpeed = 7.4;
        public const double CrouchSpeed = 1.9;

        public const double AccelGround = 52;
        public const double AccelAir = 9;
        public const double JumpVelocity = 6.4;
        public const double Gravity = -22;
        public const double TerminalVelocity = -55;
        public const double CoyoteTime = 0.12;
        public const double JumpBuffer = 0.14;

        /// <summary>Sprinting needs the wish direction to be at least this far forward (cosine).</summary>
        public const double SprintForwardCosine = 0.35;

        /// <summary>Eye height eases toward its target at this rate (1/s).</summary>
        public const double EyeEaseRate = 12;

        /// <summary>Landing slower than this (m/s, downward) is silent.</summary>
        public const double LandingSpeedThreshold = 4;

        /// <summary>Downward speed that counts as a full-strength landing.</summary>
        public const double LandingFullImpactSpeed = 18;

        const double MinWishLength = 0.01;

        double _coyote;
        double _jumpBuffer;
        double _lastVerticalVelocity;

        public double VelocityX { get; private set; }
        public double VelocityY { get; private set; }
        public double VelocityZ { get; private set; }

        public bool Grounded { get; private set; }
        public bool Crouching { get; private set; }
        public bool Sprinting { get; private set; }

        /// <summary>True for the step in which a jump started.</summary>
        public bool Jumped { get; private set; }

        /// <summary>0 when the last <see cref="ReportResult"/> was not a landing; otherwise 0..1.</summary>
        public double LandImpact { get; private set; }

        public double EyeHeight { get; private set; } = EyeStand;

        public double ColliderHeight => Crouching ? HeightCrouch : HeightStand;

        public double PlanarSpeed => Math.Sqrt(VelocityX * VelocityX + VelocityZ * VelocityZ);

        /// <summary>
        /// Advance the model by dt and return the displacement the character should attempt.
        /// headBlocked is true when there is no room above to stand up.
        /// </summary>
        public Vec3 Step(PlayerCommand cmd, double yaw, double dt, bool headBlocked)
        {
            if (!(dt > 0) || double.IsInfinity(dt)) throw new ArgumentOutOfRangeException(nameof(dt), dt, "dt must be positive and finite");

            Jumped = false;
            LandImpact = 0;

            if (cmd.JumpPressed) _jumpBuffer = JumpBuffer;
            _jumpBuffer = Math.Max(0, _jumpBuffer - dt);
            _coyote = Grounded ? CoyoteTime : Math.Max(0, _coyote - dt);

            UpdateStance(cmd.Crouch, headBlocked);

            // Wish direction. Digital input is normalised so diagonals are not faster;
            // analog input keeps its magnitude so a half-pushed stick walks slower.
            double fx = Math.Sin(yaw), fz = Math.Cos(yaw);
            double rx = Math.Cos(yaw), rz = -Math.Sin(yaw);
            double wx = fx * cmd.Forward + rx * cmd.Strafe;
            double wz = fz * cmd.Forward + rz * cmd.Strafe;
            double wishLength = Math.Sqrt(wx * wx + wz * wz);
            bool moving = wishLength > MinWishLength;
            double scale = 0;
            if (moving)
            {
                scale = Math.Min(1, wishLength);
                wx /= wishLength;
                wz /= wishLength;
            }

            Sprinting = cmd.Sprint && !Crouching && moving && (wx * fx + wz * fz) > SprintForwardCosine;

            double target = Crouching ? CrouchSpeed : Sprinting ? SprintSpeed : WalkSpeed;
            double blend = Math.Min(1, (Grounded ? AccelGround : AccelAir) * dt);
            VelocityX += (wx * scale * target - VelocityX) * blend;
            VelocityZ += (wz * scale * target - VelocityZ) * blend;

            if (_jumpBuffer > 0 && _coyote > 0 && !Crouching)
            {
                VelocityY = JumpVelocity;
                _jumpBuffer = 0;
                _coyote = 0;
                Grounded = false;
                Jumped = true;
            }
            VelocityY = Math.Max(TerminalVelocity, VelocityY + Gravity * dt);
            _lastVerticalVelocity = VelocityY;

            EyeHeight += ((Crouching ? EyeCrouch : EyeStand) - EyeHeight) * Math.Min(1, EyeEaseRate * dt);

            return new Vec3(VelocityX * dt, VelocityY * dt, VelocityZ * dt);
        }

        /// <summary>
        /// Feed back what the physics move did: whether the character ended up on the ground and
        /// whether its head hit something while rising.
        /// </summary>
        public void ReportResult(bool grounded, bool hitCeiling)
        {
            bool wasGrounded = Grounded;
            Grounded = grounded;
            LandImpact = 0;

            if (Grounded && VelocityY < 0) VelocityY = 0;
            if (!Grounded && VelocityY > 0 && hitCeiling) VelocityY = 0;

            double fall = -_lastVerticalVelocity;
            if (Grounded && !wasGrounded && fall > LandingSpeedThreshold)
            {
                LandImpact = Math.Min(1, fall / LandingFullImpactSpeed);
            }
        }

        /// <summary>Put the character somewhere new at rest, e.g. after a respawn.</summary>
        public void ResetMotion()
        {
            VelocityX = VelocityY = VelocityZ = 0;
            _coyote = _jumpBuffer = _lastVerticalVelocity = 0;
            Jumped = false;
            LandImpact = 0;
        }

        void UpdateStance(bool wantCrouch, bool headBlocked)
        {
            if (wantCrouch == Crouching) return;
            // Only stand back up if there is headroom.
            if (!wantCrouch && headBlocked) return;
            Crouching = wantCrouch;
        }
    }
}
