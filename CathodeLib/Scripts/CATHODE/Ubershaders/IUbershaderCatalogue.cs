using CATHODE;
using CATHODE.ShaderTypes;
using System.Collections.Generic;

#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
namespace CathodeLib.Ubershaders
{
    public interface IUbershaderCatalogue
    {
        /// <summary>Every feature mask this catalogue holds for a family.</summary>
        HashSet<long> FamilyMasks(SHADER_LIST family);

        /// <summary>
        /// A shader for one exact permutation. The returned entry must be a FRESH instance - the
        /// caller rewrites its sampler remaps and adds it to a level.
        /// </summary>
        bool TryGet(SHADER_LIST family, long mask, out Shaders.Shader shader);

        /// <summary>
        /// Families held, with how many distinct permutations each has.
        /// </summary>
        IEnumerable<KeyValuePair<SHADER_LIST, int>> Families();

        /// <summary>
        /// Every entry of a family, as (mask, fresh shader) - used to pick a metadata donor.
        /// </summary>
        IEnumerable<KeyValuePair<long, Shaders.Shader>> Entries(SHADER_LIST family);
    }
}
#endif
