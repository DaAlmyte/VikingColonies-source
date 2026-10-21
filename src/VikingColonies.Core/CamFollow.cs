using System;

namespace VikingColonies.Core
{
    /// <summary>
    /// vc_cam follow (1.4.2, a test tool for the store pictures): the camera at a distance and height from a settler, at an
    /// angle from where they face (0 in front, looking at their face; 90 to their right; 180 behind), turning with them.
    /// </summary>
    public static class CamFollow
    {
        /// <summary>Where the camera sits, from the settler's feet; <paramref name="facingYaw"/> in degrees (0 = north, 90 = east).</summary>
        public static Vec3 Offset(float facingYaw, float distance, float height, float angle)
        {
            double yaw = (facingYaw + angle) * Math.PI / 180.0;
            return new Vec3((float)(Math.Sin(yaw) * distance), height, (float)(Math.Cos(yaw) * distance));
        }

        /// <summary>
        /// The command's last word "hud" (or "tags") keeps the screen as it is, with the settlers' name tags and their speech,
        /// for pictures of them talking (the hub's store pictures); otherwise the HUD is hidden.
        /// </summary>
        public static bool KeepsHud(string[] args)
        {
            if (args == null || args.Length == 0) return false;
            string last = args[args.Length - 1] ?? "";
            return string.Equals(last, "hud", StringComparison.OrdinalIgnoreCase) || string.Equals(last, "tags", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>"talking": the settler whose line of small talk was shown last, wherever they are (the hub's store pictures).</summary>
        public static bool FollowsLastSpeaker(string word) => string.Equals(word, "talking", StringComparison.OrdinalIgnoreCase);

        /// <summary>A settler is found by their name or their job (any case): "Ingrid", "lumberjack"; "jobless", or "any" for anyone.</summary>
        public static bool Matches(string word, string name, string job)
        {
            if (string.IsNullOrEmpty(word)) return false;
            if (string.Equals(word, "any", StringComparison.OrdinalIgnoreCase)) return true;
            if (FollowsLastSpeaker(word)) return false;   // not a name: the last speaker, picked apart
            if (string.Equals(word, "jobless", StringComparison.OrdinalIgnoreCase)) return string.IsNullOrEmpty(job);
            return string.Equals(word, name, StringComparison.OrdinalIgnoreCase) || string.Equals(word, job, StringComparison.OrdinalIgnoreCase);
        }
    }
}
