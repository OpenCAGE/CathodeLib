using System;
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace CATHODE.Generic
{
    public class CathodePAK
    {
        protected GenericPAKHeader PAKHeader;
        protected GenericPAKEntry[] PAKEntries;

        protected byte[] DataStart;
        protected List<byte[]> EntryDatas;

        protected void LoadPAK(string filepath, bool BigEndian)
        {
            BinaryReader Stream = new BinaryReader(File.OpenRead(filepath));

            PAKHeader = Utilities.Consume<GenericPAKHeader>(Stream);
            if (BigEndian)
            {
                PAKHeader.Version = BinaryPrimitives.ReverseEndianness(PAKHeader.Version);
                PAKHeader.MaxEntryCount = BinaryPrimitives.ReverseEndianness(PAKHeader.MaxEntryCount);
                PAKHeader.EntryCount = BinaryPrimitives.ReverseEndianness(PAKHeader.EntryCount);
            }

            PAKEntries = Utilities.ConsumeArray<GenericPAKEntry>(Stream, PAKHeader.MaxEntryCount);

            //todo-mattf; remove the need for this
            long resetpos = Stream.BaseStream.Position;
            DataStart = Stream.ReadBytes((int)Stream.BaseStream.Length - (int)resetpos);
            Stream.BaseStream.Position = resetpos;

            EntryDatas = new List<byte[]>(PAKHeader.MaxEntryCount);
            for (int EntryIndex = 0; EntryIndex < PAKHeader.MaxEntryCount; ++EntryIndex)
            {
                if (BigEndian)
                {
                    GenericPAKEntry Entry = PAKEntries[EntryIndex];
                    Entry.Length = BinaryPrimitives.ReverseEndianness(PAKEntries[EntryIndex].Length);
                    Entry.DataLength = BinaryPrimitives.ReverseEndianness(PAKEntries[EntryIndex].DataLength);
                    Entry.UnknownIndex = BinaryPrimitives.ReverseEndianness(PAKEntries[EntryIndex].UnknownIndex);
                    Entry.BINIndex = BinaryPrimitives.ReverseEndianness(PAKEntries[EntryIndex].BINIndex);
                    Entry.Offset = BinaryPrimitives.ReverseEndianness(PAKEntries[EntryIndex].Offset);
                    PAKEntries[EntryIndex] = Entry;
                }
                byte[] Buffer = (PAKEntries[EntryIndex].DataLength == -1) ? new byte[]{ } : Stream.ReadBytes(PAKEntries[EntryIndex].DataLength);
                EntryDatas.Add(Buffer);
            }

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