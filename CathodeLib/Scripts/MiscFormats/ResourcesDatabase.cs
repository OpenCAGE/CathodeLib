using System.IO;
using System.Runtime.InteropServices;
using CATHODE.Scripting;
using CathodeLib;

namespace CATHODE
{
    /* Handles CATHODE RESOURCES.BIN files */
    //This file seems to govern data being drawn from either MVR or COMMANDS
    public class ResourcesDatabase : CathodeFile
    {
        //TODO: tidy how we access these
        public Header _header;
        public Entry[] _entries;

        /* Load the file */
        public ResourcesDatabase(string path) : base(path) { }

        #region FILE_IO
        /* Load the file */
        protected override bool Load()
        {
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

        #region HELPERS
        /*
        public void OrderEntries()
        {
            List<CathodeResourcesEntry> entrieslist = new List<CathodeResourcesEntry>();
            entrieslist.AddRange(entries);
            entrieslist.OrderBy(o => o.IndexFromMVREntry);
            entries = entrieslist.ToArray();
        }
        */
        #endregion

        #region STRUCTURES
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct Header
        {
            public fourcc Magic;
            public int UnknownOne_; //maybe file version
            public int EntryCount;
            public int UnknownZero_;
        };

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct Entry
        {
            public ShortGuid NodeID;
            public int IDFromMVREntry; //ResourceID?
            public int IndexFromMVREntry; // NOTE: This is an entry index in this file itself.
        };
        #endregion
    }
}