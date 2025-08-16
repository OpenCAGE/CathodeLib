using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using CathodeLib.Properties;

using System.Linq;

#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using UnityEngine;
#else
using System.Numerics;
#endif

namespace CATHODE
{
    /// <summary>
    /// DATA/ENV/PRODUCTION/x/RENDERABLE/RADIOSITY_INSTANCE_MAP.TXT
    /// </summary>
    public class RadiosityInstanceMap : CathodeFile
    {
        public List<Entry> Entries = new List<Entry>();
        public static new Implementation Implementation = Implementation.LOAD | Implementation.SAVE | Implementation.CREATE;

        public RadiosityInstanceMap(string path) : base(path) { }

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            //NOTE: Loading via byte[] or MemoryStream is not currently supported. Must be loaded via disk from a filepath!
            if (_filepath == "")
                return false;

            string[] radiosityMappings = File.ReadAllLines(_filepath); 
            foreach (string entry in radiosityMappings)
            {
                string[] mapping = entry.Split(' ');
                Entries.Add(new Entry()
                {
                    lightmap_transform = Convert.ToInt32(mapping[0]),
                    resource_index = Convert.ToInt32(mapping[1])
                });
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            List<string> radiosityMappings = new List<string>();
            foreach (Entry entry in Entries)
            {
                radiosityMappings.Add(entry.lightmap_transform + " " + entry.resource_index);
            }
            File.WriteAllLines(_filepath, radiosityMappings.ToArray());
            return true;
        }
        #endregion

        #region STRUCTURES
        public class Entry
        {
            public int lightmap_transform;
            public int resource_index;
        }
        #endregion
    }
}