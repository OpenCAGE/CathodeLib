using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using CATHODE.Commands;

namespace CATHODE.Misc
{
    /* Handles CATHODE RESOURCES.BIN files */
    //This file seems to govern data being drawn from either MVR or COMMANDS
    public class ResourcesDatabase
    {
        private string filepath;
        public CathodeResourcesHeader header;
        public CathodeResourcesEntry[] entries;

        /* Load the file */
        public ResourcesDatabase(string path)
        {
            filepath = path;

            BinaryReader Stream = new BinaryReader(File.OpenRead(path));
            header = Utilities.Consume<CathodeResourcesHeader>(Stream);
            entries = Utilities.ConsumeArray<CathodeResourcesEntry>(Stream, header.EntryCount);
            Stream.Close();
        }

        /*
        public void OrderEntries()
        {
            List<CathodeResourcesEntry> entrieslist = new List<CathodeResourcesEntry>();
            entrieslist.AddRange(entries);
            entrieslist.OrderBy(o => o.IndexFromMVREntry);
            entries = entrieslist.ToArray();
        }
        */

        /* Save the file */
        public void Save()
        {
            BinaryWriter stream = new BinaryWriter(File.OpenWrite(filepath));
            stream.BaseStream.SetLength(0);
            Utilities.Write<CathodeResourcesHeader>(stream, header);
            Utilities.Write<CathodeResourcesEntry>(stream, entries);
            stream.Close();
        }

        /* Data accessors */
        public int EntryCount { get { return entries.Length; } }
        public CathodeResourcesEntry[] Entries { get { return entries; } }
        public CathodeResourcesEntry GetEntry(int i)
        {
            return entries[i];
        }

        /* Data setters */
        public void SetEntry(int i, CathodeResourcesEntry content)
        {
            entries[i] = content;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CathodeResourcesHeader
    {
        public fourcc Magic;
        public int UnknownOne_; //maybe file version
        public int EntryCount;
        public int UnknownZero_;
    };

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CathodeResourcesEntry
    {
        public ShortGuid NodeID;
        public int IDFromMVREntry; //ResourceID?
        public int IndexFromMVREntry; // NOTE: This is an entry index in this file itself.
    };
}