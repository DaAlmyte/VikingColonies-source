namespace VikingColonies.Core
{
    /// <summary>
    /// The size of VikingColonies' windows (1.5.1, review 8: Hearthfolk's UI scale is a kindness on big and small screens):
    /// the setting kept between three quarters and one and a half, and a window too tall for the screen made to fit.
    /// </summary>
    public static class WindowScale
    {
        public const float Min = 0.75f, Max = 1.5f, Default = 1f;

        /// <summary>The share of the screen's height a window may take.</summary>
        public const float MostOfScreen = 0.95f;

        public static float Clamp(float setting) =>
            float.IsNaN(setting) || float.IsInfinity(setting) ? Default : setting < Min ? Min : setting > Max ? Max : setting;

        /// <summary>The clamped setting, made smaller when the window would be taller than most of the screen.</summary>
        public static float Fit(float setting, float windowHeight, float screenHeight)
        {
            float scale = Clamp(setting);
            if (windowHeight <= 0f || screenHeight <= 0f) return scale;
            float most = screenHeight * MostOfScreen / windowHeight;
            return scale > most ? most : scale;
        }
    }
}
