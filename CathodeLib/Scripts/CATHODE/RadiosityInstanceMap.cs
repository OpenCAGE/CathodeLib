using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;

using System.Linq;

#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using UnityEngine;
#elif GODOT
using Godot;
using System.Numerics;
using Matrix4x4 = System.Numerics.Matrix4x4;
using Quaternion = System.Numerics.Quaternion;
using Vector2 = Godot.Vector2;
using Vector3 = Godot.Vector3;
using Vector4 = Godot.Vector4;
using Color = Godot.Color;
#else
using System.Numerics;
#endif

namespace CATHODE
{
    /// <summary>
    /// DATA/ENV/x/RENDERABLE/RADIOSITY_INSTANCE_MAP.TXT
    /// </summary>
    public class RadiosityInstanceMap : CathodeFile
    {
        public List<Entry> Entries = new List<Entry>();
        public static new Implementation Implementation = Implementation.LOAD | Implementation.SAVE | Implementation.CREATE;

        public RadiosityInstanceMap(string path) : base(path) { }

        ~RadiosityInstanceMap()
        {
            Entries.Clear();
        }

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