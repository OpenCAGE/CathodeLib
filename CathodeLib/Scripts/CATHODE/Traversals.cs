using CATHODE.Scripting;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using UnityEngine;
#else
using System.Numerics;
#endif

namespace CATHODE.EXPERIMENTAL
{
    /* DATA/ENV/PRODUCTION/x/WORLD/STATE_x/TRAVERSAL */
    public class Traversals : CathodeFile
    {
        //NOTE: Not bothering finishing reversing this one as it's not actually used.
        //      The TRAVERSAL file is only populated in the AUTOGENERATION folder which isn't used by the game at runtime.
        //      Have included write support though to write the empty file.

        /*public*/ private List<Entry> Entries = new List<Entry>();
        public static new Implementation Implementation = Implementation.SAVE;
        public Traversals(string path) : base(path) { }

        private char[] _magic = new char[4] { 't', 'r', 'a', 'v' };
        private int _version = 2;

        #region FILE_IO
        override protected bool LoadInternal()
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                char[] magic = reader.ReadChars(4);
                if (!magic.SequenceEqual(_magic)) throw new Exception();
                int version = reader.ReadInt32();
                if (version != _version) throw new Exception();

                int entryCount = reader.ReadInt16();
                for (int i = 0; i < entryCount; i++)
                {
                    Entries.Add(Utilities.Consume<Entry>(reader));
                }

                //note: there is more data left behind here.
            } 
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                writer.Write(_magic);
                writer.Write(_version);

                /*
                writer.Write((Int16)Entries.Count);
                for (int i = 0; i < Entries.Count; i++)
                {

                }
                */

                writer.Write((Int16)0);
                writer.Write((Int16)1);
                writer.Write((Int16)1);
                writer.Write((Int16)0);
                writer.Write((Int16)0);
                writer.Write((Int16)0);
                writer.Write((Int16)0);
                writer.Write((Int16)0);
                writer.Write(16256);
            }
            return true;
        }
        #endregion

        #region STRUCTURES
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class Entry
        {
            public short EntryIndex;
            public int UnknownID; // NOTE: It is the same value for all entries in 'sci_hub'. Only seen in this file.
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
            public byte[] Unknowns_; //5
            public ShortGuid NodeID; // NOTE: This is found in 'commands.pak' and it is a "Traversal" node for the Alien indicates animation.
                                     //  Seen in 'resources.bin', 'nav_mesh', 'traversal' and 'commands.pak'.
            public ShortGuid ResourcesBINID; // NOTE: Seen in 'resources.bin', 'nav_mesh', 'traversal'. MATT: is this instance id?
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
            public Vector3[] Ps; //20
        };
        #endregion
    }
}