using CATHODE.Scripting;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace CATHODE
{
    /// <summary>
    /// DATA/ENV/x/WORLD/SOUNDENVIRONMENTDATA.DAT
    /// </summary>
    public class SoundEnvironmentData : CathodeFile
    {
        public HashSet<string> Entries = new HashSet<string>();
        public static new Implementation Implementation = Implementation.CREATE | Implementation.LOAD | Implementation.SAVE;

        //These are all the possible reverb names. Each level only uses a subset of them, defined by SoundEnvironmentMarker entities.
        public readonly string[] PossibleEntries = new string[20]
        {
            "roomverb_corridor_padded",
            "roomverb_medium_room",
            "roomverb_detachable_lab",
            "roomverb_vent",
            "roomverb_corridor",
            "Tannoy_Verb",
            "Warehouse_Reverb",
            "roomverb_huge_room",
            "roomverb_Ladder_shaft",
            "Locker_Reverb",
            "roomverb_large_room",
            "roomverb_multistory_largeroom",
            "roomverb_long_hallway",
            "roomverb_bathroom",
            "OutSide_EVA_SUIT",
            "roomverb_LV426_MainRoom",
            "Planetview_Room_Reverb_01",
            "Planetview_Room_Reverb",
            "roomverb_staircase",
            "roomverb_small_room"
        };

        public SoundEnvironmentData(string path) : base(path) { }
        public SoundEnvironmentData(MemoryStream stream, string path = "") : base(stream, path) { }
        public SoundEnvironmentData(byte[] data, string path = "") : base(data, path) { }

        ~SoundEnvironmentData()
        {
            Entries.Clear();
        }

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            using (BinaryReader reader = new BinaryReader(stream))
            {
                reader.BaseStream.Position += 4; //version
                int entryCount = reader.ReadInt32();
                for (int i = 0; i < entryCount; i++)
                {
                    byte[] content = reader.ReadBytes(100);
                    using (BinaryReader contentReader = new BinaryReader(new MemoryStream(content)))
                    {
                        Entries.Add(Utilities.ReadString(contentReader));
                    }
                }
                reader.BaseStream.Position += 4; //zone count - always zero
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                writer.Write(2);
                writer.Write(Entries.Count);
                foreach (string reverb in Entries)
                {
                    Utilities.WriteString(reverb, writer);
                    writer.Write(new byte[100 - reverb.Length]);
                }
                writer.Write(0);
            }
            return true;
        }
        #endregion
    }
}