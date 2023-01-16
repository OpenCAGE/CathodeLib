using CathodeLib;
using System;
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace CATHODE.LEGACY
{
    public class CathodePAK
    {
        public GenericPAKHeader header;
        public byte[] data;

        public byte[] dataStart;

        public GenericPAKEntry[] entryHeaders;
        public List<byte[]> entryContents;

        protected void LoadPAK(string filepath, bool BigEndian)
        {
            /*
            BinaryReader Stream = new BinaryReader(File.OpenRead(filepath));

            header = Utilities.Consume<GenericPAKHeader>(Stream);
            if (BigEndian) header.EndianSwap();

            entryHeaders = Utilities.ConsumeArray<GenericPAKEntry>(Stream, header.entryCount);
            entryContents = new List<byte[]>(header.entryCount);
            for (int i = 0; i < header.entryCount; ++i)
            {
                if (BigEndian) entryHeaders[i].EndianSwap();
                byte[] Buffer = (entryHeaders[i].length == -1) ? new byte[]{ } : Stream.ReadBytes(entryHeaders[i].length);
                entryContents.Add(Buffer);
            }

            Stream.Close();
            */

            BinaryReader Stream = new BinaryReader(File.OpenRead(filepath));

            GenericPAKHeader Header = Utilities.Consume<GenericPAKHeader>(Stream);
            if (BigEndian)
            {
                Header.Version = BinaryPrimitives.ReverseEndianness(Header.Version);
                Header.MaxEntryCount = BinaryPrimitives.ReverseEndianness(Header.MaxEntryCount);
                Header.EntryCount = BinaryPrimitives.ReverseEndianness(Header.EntryCount);
            }

            GenericPAKEntry[] Entries = Utilities.ConsumeArray<GenericPAKEntry>(Stream, Header.MaxEntryCount);

            //todo-mattf; remove the need for this
            long resetpos = Stream.BaseStream.Position;
            dataStart = Stream.ReadBytes((int)Stream.BaseStream.Length - (int)resetpos);
            Stream.BaseStream.Position = resetpos;

            List<byte[]> EntryDatas = new List<byte[]>(Header.MaxEntryCount);
            for (int EntryIndex = 0; EntryIndex < Header.MaxEntryCount; ++EntryIndex)
            {
                if (BigEndian)
                {
                    GenericPAKEntry Entry = Entries[EntryIndex];
                    Entry.Length = BinaryPrimitives.ReverseEndianness(Entries[EntryIndex].Length);
                    Entry.DataLength = BinaryPrimitives.ReverseEndianness(Entries[EntryIndex].DataLength);
                    Entry.UnknownIndex = BinaryPrimitives.ReverseEndianness(Entries[EntryIndex].UnknownIndex);
                    Entry.BINIndex = BinaryPrimitives.ReverseEndianness(Entries[EntryIndex].BINIndex);
                    Entry.Offset = BinaryPrimitives.ReverseEndianness(Entries[EntryIndex].Offset);
                    Entries[EntryIndex] = Entry;
                }
                byte[] Buffer = (Entries[EntryIndex].DataLength == -1) ? new byte[] { } : Stream.ReadBytes(Entries[EntryIndex].DataLength);
                EntryDatas.Add(Buffer);
            }

            header = Header;
            entryHeaders = Entries;
            entryContents = EntryDatas;

            Stream.Close();
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct GenericPAKHeader
    {
        public int _Unknown1;
        public int _Unknown2;
        public int Version;
        public int EntryCount;
        public int MaxEntryCount;
        public int _Unknown3;
        public int _Unknown4;
        public int _Unknown5;
    };

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct GenericPAKEntry
    {
        public int _Unknown1; //TODO: Is 'alien_pak_header' 40 bytes long instead?
        public int _Unknown2;
        public int Length;
        public int DataLength; //TODO: Seems to be the aligned version of Length, aligned to 16 bytes.
        public int Offset;
        public int _Unknown3;
        public int _Unknown4;
        public int _Unknown5;
        public Int16 UnknownIndex;
        public Int16 BINIndex;
        public int _Unknown6;
        public int _Unknown7;
        public int _Unknown8;
    };
}