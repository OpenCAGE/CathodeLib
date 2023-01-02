using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using CathodeLib;

namespace CATHODE
{
    /* Handles Cathode PHYSICS.MAP files */
    public class PhysicsMapDatabase : CathodeFile
    {
        //TODO: tidy how we access these
        public Header _header;
        public Entry[] _entries;

        public PhysicsMapDatabase(string path) : base(path) { }

        #region FILE_IO
        /* Load the file */
        protected override bool Load()
        {
            if (!File.Exists(_filepath)) return false;

            BinaryReader stream = new BinaryReader(File.OpenRead(_filepath));
            try
            {
                _header = Utilities.Consume<Header>(stream);
                _entries = Utilities.ConsumeArray<Entry>(stream, _header.EntryCount);
            }
            catch
            {
                stream.Close();
                return false;
            }
            stream.Close();
            return true;
        }

        /* Save the file */
        override public bool Save()
        {
            BinaryWriter stream = new BinaryWriter(File.OpenWrite(_filepath));
            try
            {
                stream.BaseStream.SetLength(0);
                Utilities.Write<Header>(stream, _header);
                Utilities.Write<Entry>(stream, _entries);
            }
            catch
            {
                stream.Close();
                return false;
            }
            stream.Close();
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
            public int FileSizeExcludingThis;
            public int EntryCount;
        };

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct Entry
        {
            public int UnknownNotableValue_;
            public int UnknownZero;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public int[] IDs; //4
            public Vector4 Row0; // NOTE: This is a 3x4 matrix, seems to have rotation data on the leftmost 3x3 matrix, and position
            public Vector4 Row1; //   on the rightmost 3x1 matrix.
            public Vector4 Row2;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
            public int[] UnknownZeros_; //2
        };
        #endregion
    }
}