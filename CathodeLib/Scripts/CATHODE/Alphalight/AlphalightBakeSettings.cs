#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
namespace CathodeLib.Alphalight
{
    /// <summary>
    /// Constants for <see cref="AlphalightBaker"/>.
    /// </summary>
    public sealed class AlphalightBakeSettings
    {
        /// <summary>
        /// Reuse the probe grid size already recorded on a ModelReference's
        /// <c>alpha_light_scale_*</c> parameters instead of deriving one. Retail's choice is
        /// deterministic but not yet reproduced (see the remarks on <see cref="AlphalightBaker"/>),
        /// so this is the only way to match it exactly; entities with no existing parameters fall
        /// back to <see cref="TargetTexelSize"/> either way.
        /// </summary>
        public bool PreserveExistingResolution = true;

        /// <summary>
        /// World-space spacing aimed for between probes when a grid size has to be derived.
        /// Retail's implied spacing over 2818 axes runs p10 0.18 to p90 0.38 with a median of
        /// 0.2496; 0.24 is where <c>round(length / texel) + 1</c> best fits the whole set.
        /// </summary>
        public float TargetTexelSize = 0.24f;

        /// <summary>Smallest probe grid, per axis. Retail never ships fewer than two.</summary>
        public int MinGridSize = 2;

        /// <summary>
        /// Largest probe grid, per axis, when a size has to be derived. Sizes already recorded on an
        /// entity are trusted regardless - retail ships up to 54 on TECH_HUB - so this only bounds
        /// new content.
        /// </summary>
        public int MaxGridSize = 48;

        /// <summary>
        /// How far from the surface, in texels of its own grid, a probe node may be and still take
        /// the closest point on the mesh. Nodes further out are filled from their neighbours
        /// instead. Retail holds a node that is exactly one texel out, so the test is strict.
        /// </summary>
        public float CoverageTexels = 1.0f;

        /// <summary>
        /// Smallest atlas edge to try. The bake takes the first power of two the boxes fit in, so
        /// this is where the search starts - retail ships 64 on the smaller levels and 128 on the
        /// rest, and the search reproduces that split.
        /// </summary>
        public int MinResolution = 64;

        /// <summary>Atlas edge used when a level has nothing to bake at all.</summary>
        public int PreferredResolution = 128;

        /// <summary>Atlas edge past which the bake gives up rather than growing further.</summary>
        public int MaxResolution = 512;

        /// <summary>
        /// Write the generated <c>alpha_light_*</c> parameters back onto the ModelReference
        /// entities. Off, the atlas is still built but COMMANDS is left alone.
        /// </summary>
        public bool WriteEntityParameters = true;
    }
}
#endif
