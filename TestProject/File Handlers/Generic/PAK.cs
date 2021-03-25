using System;
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestProject.File_Handlers.PAK
{
    public class PAK
    {
        public static alien_pak Load(string filepath, bool BigEndian)
        {
            alien_pak Result = new alien_pak();
            BinaryReader Stream = new BinaryReader(File.OpenRead(filepath));

            alien_pak_header Header = Utilities.Consume<alien_pak_header>(ref Stream);
            if (BigEndian)
            {
                Header.Version = BinaryPrimitives.ReverseEndianness(Header.Version);
                Header.MaxEntryCount = BinaryPrimitives.ReverseEndianness(Header.MaxEntryCount);
                Header.EntryCount = BinaryPrimitives.ReverseEndianness(Header.EntryCount);
            }

            List<alien_pak_entry> Entries = Utilities.ConsumeArray<alien_pak_entry>(ref Stream, Header.MaxEntryCount);

            //todo-mattf; remove the need for this
            long resetpos = Stream.BaseStream.Position;
            byte[] DataStart = Stream.ReadBytes((int)Stream.BaseStream.Length - (int)resetpos);
            Stream.BaseStream.Position = resetpos;

            List<byte[]> EntryDatas = new List<byte[]>(Header.MaxEntryCount);
            for (int EntryIndex = 0; EntryIndex < Header.MaxEntryCount; ++EntryIndex)
            {
                if (BigEndian)
                {
                    alien_pak_entry Entry = Entries[EntryIndex];
                    Entry.Length = BinaryPrimitives.ReverseEndianness(Entries[EntryIndex].Length);
                    Entry.DataLength = BinaryPrimitives.ReverseEndianness(Entries[EntryIndex].DataLength);
                    Entry.UnknownIndex = BinaryPrimitives.ReverseEndianness(Entries[EntryIndex].UnknownIndex);
                    Entry.BINIndex = BinaryPrimitives.ReverseEndianness(Entries[EntryIndex].BINIndex);
                    Entry.Offset = BinaryPrimitives.ReverseEndianness(Entries[EntryIndex].Offset);
                    Entries[EntryIndex] = Entry;
                }
                byte[] Buffer = (Entries[EntryIndex].DataLength == -1) ? new byte[]{ } : Stream.ReadBytes(Entries[EntryIndex].DataLength);
                EntryDatas.Add(Buffer);
            }

            Result.Header = Header;
            Result.Entries = Entries;
            Result.DataStart = DataStart;
            Result.EntryDatas = EntryDatas;

            Stream.Close();
            return Result;
        }
    }
}


//Length: 32 bytes
public struct alien_pak_header
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

//Length: 48 bytes
public struct alien_pak_entry
{
    public int _Unknown1; //TODO: Is 'alien_pak_header' 40 bytes long instead?
    public int _Unknown2;
    public int Length;
    public int DataLength; // TODO: Seems to be the aligned version of Length, aligned to 16 bytes.
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

public struct alien_pak
{
    public alien_pak_header Header;
    public List<alien_pak_entry> Entries;
    
    public byte[] DataStart;
    public List<byte[]> EntryDatas;
};