using CATHODE.Scripting;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace CATHODE
{
    /* DATA/ENV/PRODUCTION/x/WORLD/SNDNODENETWORK.DAT */
    public class SoundNodeNetwork : CathodeFile
    {
        private List<string> Entries = new List<string>();
        public static new Implementation Implementation = Implementation.NONE;
        public SoundNodeNetwork(string path) : base(path) { }

        private int _test = 0;
        private int test
        {
            set
            {
                _test = value;
                Console.WriteLine(_test);
            }
            get
            {
                return _test;
            }
        }
        private float _testf = 0;
        private float testf
        {
            set
            {
                _testf = value;
                Console.WriteLine(_testf);
            }
            get
            {
                return _testf;
            }
        }

        //This file is seemingly somewhat ordered by size? Biggest listener networks get their headers written first ish.

        #region FILE_IO
        override protected bool LoadInternal()
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                reader.BaseStream.Position += 4;
                int count1 = reader.ReadInt16();
                int entryCount = reader.ReadInt16();
                for (int x = 0; x < entryCount; x++)
                {
                    if (Entries.Count != 0 && Entries[Entries.Count - 1] == "Corridor Junction Area")
                    {
                        string sddsf = "";
                    }

                    int strLength = reader.ReadInt16();
                    string listenerName = ""; //confirmed via dev screenshot
                    for (int i = 0; i < strLength; i++) listenerName += reader.ReadChar();
                    Entries.Add(listenerName);

                    //start header
                    Console.WriteLine("start header");

                    int typeID = reader.ReadInt16();

                    test = reader.ReadInt16();  
                    test = reader.ReadInt16();  

                    test = reader.ReadInt16();  
                    test = reader.ReadInt16();  

                    testf = reader.ReadSingle(); //always 1?
                    if (testf != 1)
                    {
                        throw new Exception("");
                    }

                    testf = reader.ReadSingle();
                    testf = reader.ReadSingle();
                    testf = reader.ReadSingle();

                    testf = reader.ReadSingle();
                    testf = reader.ReadSingle();
                    testf = reader.ReadSingle();

                    //Somewhere here we should have an Ambience sound event (maybe start/stop?)

                    //I'm unsure if these are actually int16
                    test = reader.ReadInt16();
                    test = reader.ReadInt16();
                    test = reader.ReadInt16();

                    if (test == 0)
                    {
                        string sdfdf = "";
                        continue; //44 bytes
                    }
                    //^ doing this gets us a bit further on BSP_TORRENS, but we still crash after "Torrens Bridge Corridor" due to something there.

                    //I'm unsure if these are actually int16
                    test = reader.ReadInt16();
                    test = reader.ReadInt16();

                    Console.WriteLine("Header Over");

                    //end next bit (48) <- this is all useful above here but just skipping for now, figure out datatypes from ps3 dump

                    //TODO: typeID is not the type ID as i expected, since two type 0's load differently on tech_hub

                    switch (typeID)
                    {
                        //case 0:
                         //   reader.BaseStream.Position = 13044; //temp hack
                         //   break;
                        default:
                            //if (typeID == 0)
                            //{
                            //    string dafssdf = "";
                            //}

                            Console.WriteLine("!!!! loading type: " + typeID);
                            LoadRecursive(reader);
                            break;
                    }
                }




                //footer? noticing a pattern here

                test = reader.ReadInt32(); 
                test = reader.ReadInt32();
                test = reader.ReadInt32();

                test = reader.ReadInt16();
                test = reader.ReadInt16();
                test = reader.ReadByte();
                test = reader.ReadByte();

                test = reader.ReadInt16();
                test = reader.ReadByte();
                test = reader.ReadByte();

                test = reader.ReadInt16();
                test = reader.ReadByte();
                test = reader.ReadByte();

                test = reader.ReadInt16();
                test = reader.ReadByte();
                test = reader.ReadByte();

                test = reader.ReadInt16();
                test = reader.ReadByte();
                test = reader.ReadByte();

                test = reader.ReadInt16();

                // --

                test = reader.ReadInt32();
                test = reader.ReadInt32();
                test = reader.ReadInt32();

                test = reader.ReadInt16();
                test = reader.ReadInt16();
                test = reader.ReadByte();
                test = reader.ReadByte();

                test = reader.ReadInt16();
                test = reader.ReadByte();
                test = reader.ReadByte();

                test = reader.ReadInt16();
                test = reader.ReadByte();
                test = reader.ReadByte();

                test = reader.ReadInt16();
                test = reader.ReadByte();
                test = reader.ReadByte();

                test = reader.ReadInt16();

                // --

                test = reader.ReadInt32();
                test = reader.ReadInt32();
                test = reader.ReadInt32();

                test = reader.ReadInt16();
                test = reader.ReadInt16();
                test = reader.ReadByte();
                test = reader.ReadByte();

                test = reader.ReadInt16();
                test = reader.ReadByte();
                test = reader.ReadByte();

                test = reader.ReadInt16();
                test = reader.ReadByte();
                test = reader.ReadByte();

                test = reader.ReadInt16();

                // ---

                test = reader.ReadInt32();
                test = reader.ReadInt32();
                test = reader.ReadInt32();
                test = reader.ReadInt16();
                test = reader.ReadInt16();

                string bruh = "";

                //38
                reader.BaseStream.Position += 38;

                int a = reader.ReadInt16();
                int b = reader.ReadInt16();
                int c = reader.ReadInt16();
                ShortGuid d = Utilities.Consume<ShortGuid>(reader);
                int e = reader.ReadInt32();


                for (int i = 0; i < 999; i++)
                {
                    UInt16 x_index = reader.ReadUInt16();
                    UInt16 y_index = reader.ReadUInt16();
                    UInt16 z_index = reader.ReadUInt16();
                }
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
            }
            return true;
        }
        #endregion

        private void LoadRecursive(BinaryReader reader)
        {
            test = reader.ReadInt16(); //small
            test = reader.ReadInt16(); //bigger

            int countOne = reader.ReadInt16(); //count of next block
            Console.WriteLine("Count of block 1: " + countOne);

            for (int z = 0; z < countOne; z++)
            {
                test = reader.ReadInt16(); //something index
                int countTwo = reader.ReadInt16(); //count of next ints
                Console.WriteLine("Count of block 2: " + countTwo);

                if (countTwo == 0)
                {
                    LoadRecursive(reader);

                    break; //??
                }

                for (int i = 0; i < countTwo; i++)
                {
                    test = reader.ReadInt32(); //probs indexes to the float array at the bottom of the file
                }
            }
        }
    }
}