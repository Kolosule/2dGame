using System;

namespace Game.Settings.Core
{
    /// <summary>
    /// Converts a linear 0-1 slider value into the decibel value an AudioMixer exposed parameter
    /// expects. Fixed here so the (not yet written) audio system and this menu cannot disagree.
    /// </summary>
    public static class VolumeCurve
    {
        /// <summary>Unity's AudioMixer treats -80 dB as silence.</summary>
        public const float MinDecibels = -80f;

        /// <summary>The linear value that maps exactly to MinDecibels.</summary>
        public const float MinLinear = 0.0001f;

        /// <summary>
        /// Zero and anything below the floor return MinDecibels directly rather than going through
        /// the log, so "slider at zero is silent" is a stated property, not a numeric coincidence.
        /// </summary>
        public static float LinearToDecibels(float linear)
        {
            if (linear <= MinLinear) return MinDecibels;

            float decibels = (float)(Math.Log10(linear) * 20.0);
            return decibels < MinDecibels ? MinDecibels : decibels;
        }
    }
}
