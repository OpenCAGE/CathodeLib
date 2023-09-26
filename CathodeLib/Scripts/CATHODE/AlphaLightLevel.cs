using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace CATHODE.EXPERIMENTAL
{
    /* DATA/ENV/PRODUCTION/x/WORLD/ALPHALIGHT_LEVEL.BIN */
    public class AlphaLightLevel : CathodeFile
    {
        public List<Entry> Entries = new List<Entry>();
        public static new Implementation Implementation = Implementation.NONE;
        public AlphaLightLevel(string path) : base(path) { }

        // Lighting information for objects with alpha (e.g. glass). Levels can load without this file, but look worse.

        #region FILE_IO
        override protected bool LoadInternal()
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                //todo
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                Utilities.WriteString("alph", writer);
                
                //todo
            }
            return true;
        }
        #endregion

        #region STRUCTURES
        public class Entry
        {
            //todo
        };
        #endregion
    }
}