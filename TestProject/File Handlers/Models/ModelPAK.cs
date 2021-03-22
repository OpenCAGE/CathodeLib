using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace TestProject.File_Handlers.Models
{
    class ModelPAK
    {
        public static List<alien_pak_model> Load(alien_pak PAK)
        {
            List<alien_pak_model> Models = new List<alien_pak_model>(PAK.Header.EntryCount);

            for (int EntryIndex = 0; EntryIndex < PAK.Header.EntryCount; ++EntryIndex)
            {
                byte[] EntryBuffer = PAK.EntryDatas[EntryIndex];
                BinaryReader Stream = new BinaryReader(new MemoryStream(EntryBuffer));

                alien_pak_model Model = new alien_pak_model();

                // TODO: Maybe this stuff can be moved to AlienLoadPAK, probably using an 'alien_pak_entry' value to check if
                //  it is an array of entries.
                alien_pak_model_entry_header EntryHeader = Utilities.Consume<alien_pak_model_entry_header>(Stream);
                EntryHeader.FirstChunkIndex = BinaryPrimitives.ReverseEndianness(EntryHeader.FirstChunkIndex);
                EntryHeader.ChunkCount = BinaryPrimitives.ReverseEndianness(EntryHeader.ChunkCount);
                Model.Header = EntryHeader;

                Model.ChunkInfos = Utilities.ConsumeArray<alien_pak_model_chunk_info>(Stream, EntryHeader.ChunkCount);
                Utilities.Align(Stream, 16);
                Model.Chunks = new List<byte[]>(EntryHeader.ChunkCount);

                for (int ChunkIndex = 0; ChunkIndex < EntryHeader.ChunkCount; ++ChunkIndex)
                {
                    alien_pak_model_chunk_info ChunkInfo = Model.ChunkInfos[ChunkIndex];
                    ChunkInfo.BINIndex = BinaryPrimitives.ReverseEndianness(ChunkInfo.BINIndex);
                    ChunkInfo.Offset = BinaryPrimitives.ReverseEndianness(ChunkInfo.Offset);
                    ChunkInfo.Size = BinaryPrimitives.ReverseEndianness(ChunkInfo.Size);

                    Stream.BaseStream.Position = ChunkInfo.Offset;
                    byte[] Chunk = Stream.ReadBytes(ChunkInfo.Size);
                    Model.Chunks.Add(Chunk);
                }

                Models.Add(Model);
            }

            return Models;
        }
    }
}

struct alien_pak_model_entry_header
{
    public int FirstChunkIndex;
    public int ChunkCount;
    // NOTE: Apparently always zeroes below here
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public int[] Unknown; //4
};

struct alien_pak_model_chunk_info
{
    public int BINIndex;
    public int Offset;
    public int Size;
    public int _Unknown1; // NOTE: Probably struct padding
};

struct alien_pak_model
{
    public alien_pak_model_entry_header Header;
    public List<alien_pak_model_chunk_info> ChunkInfos;
    public List<byte[]> Chunks;
};