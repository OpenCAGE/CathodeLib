using CATHODE.Generic;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace CATHODE.Models
{
    public class ModelPAK : CathodePAK
    {
        public List<ModelData> Models;

        public void Load(string FullFilePath)
        {
            LoadPAK(FullFilePath, true);

            Models = new List<ModelData>(PAKHeader.EntryCount);
            for (int EntryIndex = 0; EntryIndex < PAKHeader.EntryCount; ++EntryIndex)
            {
                byte[] EntryBuffer = EntryDatas[EntryIndex];
                BinaryReader Stream = new BinaryReader(new MemoryStream(EntryBuffer));

                ModelData Model = new ModelData();

                // TODO: Maybe this stuff can be moved to AlienLoadPAK, probably using an 'alien_pak_entry' value to check if
                //  it is an array of entries.
                ModelHeader EntryHeader = Utilities.Consume<ModelHeader>(Stream);
                EntryHeader.FirstChunkIndex = BinaryPrimitives.ReverseEndianness(EntryHeader.FirstChunkIndex);
                EntryHeader.ChunkCount = BinaryPrimitives.ReverseEndianness(EntryHeader.ChunkCount);
                Model.Header = EntryHeader;

                Model.ChunkInfos = Utilities.ConsumeArray<ModelChunkInfo>(Stream, EntryHeader.ChunkCount);
                Utilities.Align(Stream, 16);
                Model.Chunks = new List<byte[]>(EntryHeader.ChunkCount);

                for (int ChunkIndex = 0; ChunkIndex < EntryHeader.ChunkCount; ++ChunkIndex)
                {
                    ModelChunkInfo ChunkInfo = Model.ChunkInfos[ChunkIndex];
                    ChunkInfo.BINIndex = BinaryPrimitives.ReverseEndianness(ChunkInfo.BINIndex);
                    ChunkInfo.Offset = BinaryPrimitives.ReverseEndianness(ChunkInfo.Offset);
                    ChunkInfo.Size = BinaryPrimitives.ReverseEndianness(ChunkInfo.Size);
                    Model.ChunkInfos[ChunkIndex] = ChunkInfo;

                    Stream.BaseStream.Position = ChunkInfo.Offset;
                    byte[] Chunk = Stream.ReadBytes(ChunkInfo.Size);
                    Model.Chunks.Add(Chunk);
                }

                Models.Add(Model);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ModelHeader
    {
        public int FirstChunkIndex;
        public int ChunkCount;
        // NOTE: Apparently always zeroes below here
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public int[] Unknown; //4
    };

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ModelChunkInfo
    {
        public int BINIndex;
        public int Offset;
        public int Size;
        public int _Unknown1; // NOTE: Probably struct padding
    };

    public struct ModelData
    {
        public ModelHeader Header;
        public ModelChunkInfo[] ChunkInfos;
        public List<byte[]> Chunks;
    };
}