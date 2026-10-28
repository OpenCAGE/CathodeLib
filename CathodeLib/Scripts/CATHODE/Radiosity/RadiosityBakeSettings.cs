#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
namespace CathodeLib.Radiosity
{
    /// <summary>
    /// What a caller passes to <see cref="RadiosityBaker.BakeLevel"/>: whether a shipped bake is patched or regenerated.
    /// Instancing adds the two pristine snapshots the patch needs. The bake's tuning is fixed: a full regeneration always
    /// bakes with the validated profile.
    /// </summary>
    public sealed class RadiosityBakeSettings
    {
        /// <summary>
        /// Delta-bake: keep the level's shipped radiosity wholesale and patch only what the edit invalidated
        /// (<see cref="RadiosityPatcher"/>): moved and newly added movers get radiosity of their own. Off regenerates the
        /// whole level on every save. Default on since 29 Aug 2026; older library versions cleared the level's radiosity
        /// on every instanced save without radiosity settings.
        /// </summary>
        public bool PatchRetailRuntime = true;

        /// <summary>
        /// Skip the bake: keep the radiosity the level already has (the game's, or an earlier bake) and only re-bind it to the
        /// movers the save rebuilt, as <see cref="RadiosityPatcher"/> does before its delta - instance-map rows of movers that are
        /// gone are dropped, the surface lights of deleted entities go dark, and every kept mover keeps its lightmap address -
        /// so the files stay valid for the game. Nothing is baked: a moved mover keeps the lighting of where it was, and an added
        /// one has no lightmap (the engine's unlit fallback). Both are remembered in
        /// <see cref="CATHODE.RadiosityRuntime.PendingDeltaMovers"/>, so the next bake that patches lights them. A level with no
        /// radiosity to keep is left without any. Takes precedence over <see cref="PatchRetailRuntime"/>.
        /// </summary>
        public bool KeepExisting = false;

        /// <summary>
        /// Pristine MODEL_PARAMS lightmap transforms (first 16 bytes of RENDER_CONSTANTS), keyed by the mover's resource GUID
        /// pair packed as (composite &lt;&lt; 32 | resource). Harvested by Instancing before it rebuilds the movers and
        /// written back by <see cref="RadiosityPatcher"/>: instancing rebuilds some movers without carrying their lightmap
        /// rect, and a rect-less mover samples a wrong atlas region - ChallengeMap3's vent wall rendered its neighbouring
        /// tube-lights' yellow, a post rendered bunk-light blue, and ceiling pieces degenerated entirely.
        /// </summary>
        internal System.Collections.Generic.Dictionary<ulong, byte[]> RetailModelParams = null;

        /// <summary>
        /// Pristine mover transforms keyed like <see cref="RetailModelParams"/>, harvested with them.
        /// The patcher uses them to detect MOVED movers - which must be routed through the delta rebake, because their
        /// carried MODEL_PARAMS would light them as they stood at their old location. The patcher needs both snapshots and
        /// throws without them; Instancing fills whichever is null when <see cref="PatchRetailRuntime"/> or
        /// <see cref="KeepExisting"/> is on.
        /// </summary>
        internal System.Collections.Generic.Dictionary<ulong, System.Numerics.Matrix4x4> RetailTransforms = null;

        /// <summary>The default settings: patch shipped bakes, no snapshots yet (Instancing harvests them).</summary>
        public static RadiosityBakeSettings CreateDefault() => new RadiosityBakeSettings();
    }
}
#endif
