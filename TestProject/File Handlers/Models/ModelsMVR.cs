using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace CATHODE.Models
{
    /* Handles CATHODE MVR files */
    public class ModelsMVR
    {
        private string filepath;
        private alien_mvr_header header;
        private List<alien_mvr_entry> movers;

        /* Load an MVR file */
        public ModelsMVR(string pathToFile)
        {
            filepath = pathToFile;

            BinaryReader Stream = new BinaryReader(File.OpenRead(filepath));
            header = Utilities.Consume<alien_mvr_header>(ref Stream);
            movers = Utilities.ConsumeArray<alien_mvr_entry>(ref Stream, (int)header.EntryCount);
            Stream.Close();
        }

        /* Save the MVR file */
        public void Save()
        {
            FileStream stream = new FileStream(filepath, FileMode.Create);
            Utilities.Write<alien_mvr_header>(ref stream, header);
            for (int i = 0; i < movers.Count; i++) Utilities.Write<alien_mvr_entry>(ref stream, movers[i]);
            stream.Close();
        }

        /* Data accessors */
        public int EntryCount { get { return movers.Count; } }
        public List<alien_mvr_entry> Entries { get { return movers; } }
        public alien_mvr_entry GetEntry(int i)
        {
            return movers[i];
        }

        /* Data setters */
        public void SetEntry(int i, alien_mvr_entry content)
        {
            movers[i] = content;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct alien_mvr_header
    {
        public uint Unknown0_;
        public uint EntryCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public uint[] Unknown1_; //6
    };

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct alien_mvr_entry
    {
        public Matrix4x4 Transform;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
        public float[] Unknowns0_; // NOTE: The 0th element is -Pi on entry 61 of 'hab_airport'.
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public float[] Unknowns1_;
        public float UnknownValue2_;
        public float UnknownValue3_;
        public float UnknownValue4_; //id for something? not found in any other level files
        public int Unknown2_;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public float[] Unknown2f_;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public uint[] Unknowns2_;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public Vector3[] UnknownMinMax_; // NOTE: Sometimes I see 'nan's here too.
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 9)]
        public int[] Unknowns3_; // NOTE: The 9th and 11th elements seem to be incrementing indices.
        public uint REDSIndex; // Index 45
        public uint ModelCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
        public int[] Unknowns5_; //index 0 collision map entry?, final index is always 128?
        public uint NodeID; // Index 52
        public uint UnknownValue0; //another id for something?
        public uint UnknownIndex;
        public uint UnknownValue1;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public float[] Unknowns4_; //zeroing index 2 stop model rendering
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public int[] Unknowns6_;
    };
}