using CathodeLib;
using System.IO;
using System.Runtime.InteropServices;

namespace CATHODE
{
    /* Handles Cathode COLLISION.MAP files */
    public class CollisionMapDatabase : CathodeFile
    {
        //TODO: tidy how we access these
        public Header _header;
        public Entry[] _entries;

        public CollisionMapDatabase(string path) : base(path) { }

        #region FILE_IO
        /* Load the file */
        override protected bool LoadInternal()
        {
            using (BinaryReader stream = new BinaryReader(File.OpenRead(_filepath)))
            {
                _header = Utilities.Consume<Header>(stream);
                _entries = Utilities.ConsumeArray<Entry>(stream, _header.EntryCount);
            }
            return true;
        }

        /* Save the file */
        override protected bool SaveInternal()
        {
            using (BinaryWriter stream = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                stream.BaseStream.SetLength(0);
                Utilities.Write<Header>(stream, _header);
                Utilities.Write<Entry>(stream, _entries);
            }
            return true;
        }
        #endregion

        #region ACCESSORS
        /* Data accessors */
        public int EntryCount { get { return _entries.Length; } }
        public Entry[] Entries { get { return _entries; } }
        public Entry GetEntry(int i)
        {
            return _entries[i];
        }

        /* Data setters */
        public void SetEntry(int i, Entry content)
        {
            _entries[i] = content;
        }
        #endregion

        #region STRUCTURES
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct Header
        {
            public int DataSize;
            public int EntryCount;
        };

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct Entry
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public int[] Unknowns1; //12
            public int ID;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 7)]
            public int[] Unknowns2; //12
        };
        #endregion
    }
}