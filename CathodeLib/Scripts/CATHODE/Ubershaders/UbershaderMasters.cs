using CATHODE.Scripting.Internal;
using CATHODE.ShaderTypes;
using System;
using System.Collections.Generic;

#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
namespace CathodeLib.Ubershaders
{
    public static class UbershaderMasters
    {
        private static UbershaderPatchTable _table;
        private static bool _loaded;
        private static readonly object _lock = new object();

        public static PatchManager.Platform Platform = PatchManager.Platform.STEAM;

        public static UbershaderPatchTable Table
        {
            get
            {
                lock (_lock)
                {
                    if (!_loaded)
                    {
                        _loaded = true;
                        try { _table = CustomTable.ReadEmbeddedTable(CustomTableType.UBERSHADER_PATCHES) as UbershaderPatchTable; }
                        catch { _table = null; }
                        if (_table == null) _table = new UbershaderPatchTable();
                    }
                    return _table;
                }
            }
        }

        /// <summary>Is there an entry for this ubershader on the build we are editing?</summary>
        public static bool Has(SHADER_LIST ubershader)
        {
            return Table.Lookup(ubershader, Platform) != null;
        }

        /// <summary>
        /// Patches for the stage ("vs"/"ps"/"hs"/"ds"). False when the ubershader has no entry
        /// for this build, or the entry does not carry that stage.
        /// </summary>
        public static bool TryGet(SHADER_LIST ubershader, string stage, out string hlsl)
        {
            hlsl = null;
            UbershaderPatchTable.Patch patch = Table.Lookup(ubershader, Platform);
            return patch != null && patch.Stages.TryGetValue(stage, out hlsl) && !string.IsNullOrEmpty(hlsl);
        }

        /// <summary>Every ubershader the table can deal with on this build.</summary>
        public static IEnumerable<SHADER_LIST> Available()
        {
            foreach (UbershaderPatchTable.Patch patch in Table.patches)
                if (patch.SupportsPlatform(Platform))
                    yield return patch.Ubershader;
        }
    }
}
#endif
