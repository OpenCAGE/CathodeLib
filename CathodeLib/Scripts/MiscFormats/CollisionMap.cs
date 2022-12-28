using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace CATHODE.Misc
{
    /* Handles Cathode COLLISION.MAP files */
    public class CollisionMap : CathodeFile
    {
        public CollisionMapHeader header;
        public CollisionMapEntry[] entries;

        public CollisionMap(string path) : base(path) { }

        /* Load the file */
        protected override void Load()
        {
            BinaryReader stream = new BinaryReader(File.OpenRead(_filepath));
            header = Utilities.Consume<CollisionMapHeader>(stream);
            entries = Utilities.ConsumeArray<CollisionMapEntry>(stream, header.EntryCount);
            stream.Close();
        }

        /* Save the file */
        override public void Save()
        {
            BinaryWriter stream = new BinaryWriter(File.OpenWrite(_filepath));
            stream.BaseStream.SetLength(0);
            Utilities.Write<CollisionMapHeader>(stream, header);
            Utilities.Write<CollisionMapEntry>(stream, entries);
            stream.Close();
        }

        /* Data accessors */
        public int EntryCount { get { return entries.Length; } }
        public CollisionMapEntry[] Entries { get { return entries; } }
        public CollisionMapEntry GetEntry(int i)
        {
            return entries[i];
        }

        /* Data setters */
        public void SetEntry(int i, CollisionMapEntry content)
        {
            entries[i] = content;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct CollisionMapHeader
        {
            public int DataSize;
            public int EntryCount;
        };

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct CollisionMapEntry
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public int[] Unknowns1; //12
            public int ID;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 7)]
            public int[] Unknowns2; //12
        };
    }
}