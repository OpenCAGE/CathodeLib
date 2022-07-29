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
        protected GenericPAKHeader header;
        protected byte[] data;

        protected GenericPAKEntry[] entryHeaders;
        protected List<byte[]> entryContents;

        protected void LoadPAK(string filepath, bool BigEndian)
        {
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
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct GenericPAKHeader
    {
        public fourcc magic; //unused
        public int versionPAK;
        public int versionBIN;
        public int entryCount;
        public int entryCountValidation; //should match entryCount
        public int _Unknown3;
        public int _Unknown4;
        public int _Unknown5;

        public void EndianSwap()
        {
            //swap magic?
            versionPAK = BinaryPrimitives.ReverseEndianness(versionPAK);
            versionBIN = BinaryPrimitives.ReverseEndianness(versionBIN);
            entryCount = BinaryPrimitives.ReverseEndianness(entryCount);
            entryCountValidation = BinaryPrimitives.ReverseEndianness(entryCountValidation);
            _Unknown3 = BinaryPrimitives.ReverseEndianness(_Unknown3);
            _Unknown4 = BinaryPrimitives.ReverseEndianness(_Unknown4);
            _Unknown5 = BinaryPrimitives.ReverseEndianness(_Unknown5);
        }
    };

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct GenericPAKEntry
    {
        public int _Unknown1; //TODO: Is 'alien_pak_header' 40 bytes long instead?
        public int _Unknown2;
        public int length;
        public int lengthValidation; //should match length
        public int offset;
        public int _Unknown3;
        public int _Unknown4;
        public int _Unknown5;
        public Int16 _Unknown6;
        public Int16 indexBIN;
        public int _Unknown7;
        public int _Unknown8;
        public int _Unknown9;

        public void EndianSwap()
        {
            _Unknown1 = BinaryPrimitives.ReverseEndianness(_Unknown1);
            _Unknown2 = BinaryPrimitives.ReverseEndianness(_Unknown2);
            length = BinaryPrimitives.ReverseEndianness(length);
            lengthValidation = BinaryPrimitives.ReverseEndianness(lengthValidation);
            offset = BinaryPrimitives.ReverseEndianness(offset);
            _Unknown3 = BinaryPrimitives.ReverseEndianness(_Unknown3);
            _Unknown4 = BinaryPrimitives.ReverseEndianness(_Unknown4);
            _Unknown5 = BinaryPrimitives.ReverseEndianness(_Unknown5);
            _Unknown6 = BinaryPrimitives.ReverseEndianness(_Unknown6);
            indexBIN = BinaryPrimitives.ReverseEndianness(indexBIN);
            _Unknown7 = BinaryPrimitives.ReverseEndianness(_Unknown7);
            _Unknown8 = BinaryPrimitives.ReverseEndianness(_Unknown8);
            _Unknown9 = BinaryPrimitives.ReverseEndianness(_Unknown9);
        }
    };
}