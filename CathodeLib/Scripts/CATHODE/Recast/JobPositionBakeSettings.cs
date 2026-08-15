#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
namespace CathodeLib.NavMesh
{
    /// <summary>
    /// Constants for <see cref="JobPositionBaker"/>. The offsets are not guesses: every one of
    /// them was read back off retail's own files and holds to three decimal places on
    /// BSP_TORRENS, Solace, SCI_Hub and Tech_Hub. See the remarks on the baker.
    /// </summary>
    public sealed class JobPositionBakeSettings
    {
        /// <summary>
        /// How far inside the navmesh rim a sample point sits. Retail's assault positions land
        /// here (median 0.208 on every level measured) and the spotting pair straddles it.
        /// </summary>
        public float RimInset = 0.2071f;

        /// <summary>
        /// Half the spotting pair's separation. The job goes this far outside the sample along
        /// the outward normal, the task this far inside, so the pair is exactly 1 m apart -
        /// which is what retail ships for 147 of BSP_TORRENS' 169 pairs.
        /// </summary>
        public float SpottingHalfSeparation = 0.5f;

        /// <summary>
        /// Spacing along a rim edge between spotting samples. Barely matters: retail puts about
        /// one sample on each edge it accepts (313 edges carry Solace's 354 spotting positions),
        /// so sweeping this from 2 m to 6 m moves the count by 11% and the match not at all.
        /// The minimum edge length below is what actually drives how many get made.
        /// </summary>
        public float SpottingSpacing = 4.0f;

        /// <summary>Rim edges shorter than this get no spotting sample at all.</summary>
        public float SpottingMinEdgeLength = 0.75f;

        /// <summary>Two spotting samples are never placed closer together than this.</summary>
        public float SpottingMinSeparation = 0.75f;

        /// <summary>Spacing along a rim edge between assault samples.</summary>
        public float AssaultSpacing = 3.0f;

        /// <summary>Rim edges shorter than this get no assault sample.</summary>
        public float AssaultMinEdgeLength = 2.5f;

        /// <summary>Two assault samples are never placed closer together than this.</summary>
        public float AssaultMinSeparation = 0.35f;

        /// <summary>
        /// How far outside a deep-crouch region's rim the crawl-space task position is pushed,
        /// so the watcher stands on standing-height floor rather than in the vent.
        /// </summary>
        public float CrawlTaskDistance = 1.25f;

        /// <summary>Two crawl-space jobs are never placed closer together than this.</summary>
        public float CrawlMinSeparation = 0.5f;

        /// <summary>Cell size of the lookup grid the three files are binned into.</summary>
        public float GridUnitSize = 10.0f;

        public static JobPositionBakeSettings CreateDefault() => new JobPositionBakeSettings();
    }
}
#endif
