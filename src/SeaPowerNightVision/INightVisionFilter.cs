namespace SeaPowerNightVision
{
    /// <summary>
    /// A way of rendering the night vision look. Several implementations exist and the best
    /// available one is picked at runtime.
    /// </summary>
    internal interface INightVisionFilter
    {
        /// <summary>Short description shown in the log and in the settings window.</summary>
        string Name { get; }

        /// <summary>True when this filter runs inside the game's own post-processing stack.</summary>
        bool IsTruePostProcess { get; }

        /// <summary>Attempts to set the filter up. Returns false if the game doesn't support it.</summary>
        bool TryInitialize();

        /// <summary>
        /// Called every frame with the current 0..1 fade weight and a transient gain multiplier
        /// (used for the warm-up surge when the tubes are switched on).
        /// </summary>
        void Apply(float weight, float gainScale);

        /// <summary>Tears everything down and leaves the game exactly as it was.</summary>
        void Dispose();
    }
}
