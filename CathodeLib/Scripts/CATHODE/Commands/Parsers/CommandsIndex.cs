using CATHODE.Scripting;
using CATHODE.Scripting.Internal;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;

namespace CATHODE
{
    /// <summary>
    /// A composite as the index reader sees it: its ID and name, and the composites it instances.
    /// </summary>
    public struct CompositeIndexEntry
    {
        public ShortGuid ID;
        public string Name;
        /// <summary>The composites this one instances directly (distinct), so a picker can follow nesting without a parse.</summary>
        public List<ShortGuid> Instances;
        /// <summary>The level's root composite (its first entry point).</summary>
        public bool IsRoot;

        public override string ToString()
        {
            return Name;
        }
    }

    /// <summary>
    /// Reads only the composite table of a COMMANDS.PAK: every composite's ID and name, and which
    /// composites it instances. A full parse of a shipped level takes seconds and gigabytes of graph;
    /// this walks the header, the composite offset table, one string per composite and the function
    /// entity list (8 bytes an entry), so a browser can list what a level holds - and what porting a
    /// composite would bring along - without loading it.
    /// </summary>
    internal static class CommandsIndex
    {
        public static List<CompositeIndexEntry> Read(byte[] content)
        {
            using (BinaryReader reader = new BinaryReader(new MemoryStream(content)))
            {
                //Three entry point IDs (root, GLOBAL, PAUSEMENU), then the parameter table position and
                //count, then the composite table position and count
                ShortGuid rootId = new ShortGuid(reader);
                reader.BaseStream.Position = 12 + 8;
                int compositeOffsetPos = reader.ReadInt32() * 4;
                int compositeCount = reader.ReadInt32();

                reader.BaseStream.Position = compositeOffsetPos;
                int[] compositeOffsets = Utilities.ConsumeArray<int>(reader, compositeCount);

                List<CompositeIndexEntry> entries = new List<CompositeIndexEntry>(compositeCount);
                for (int i = 0; i < compositeCount; i++)
                {
                    //Each composite block: 4 zero bytes, then the script start offset (top byte is a flag),
                    //the offset pairs for every data block with the ID after the first - the same steps as
                    //the full reader
                    reader.BaseStream.Position = (compositeOffsets[i] * 4) + 4;
                    OffsetPair[] offsetPairs = new OffsetPair[(int)CompositeFileData.NUMBER_OF_SCRIPT_BLOCKS];
                    int scriptStartOffset = 0;
                    ShortGuid id = ShortGuid.Invalid;
                    for (int x = 0; x < (int)CompositeFileData.NUMBER_OF_SCRIPT_BLOCKS; x++)
                    {
                        if (x == 0)
                        {
                            byte[] startOffsetRaw = reader.ReadBytes(4);
                            startOffsetRaw[3] = 0x00;
                            scriptStartOffset = BitConverter.ToInt32(startOffsetRaw, 0);
                        }
                        offsetPairs[x] = Utilities.Consume<OffsetPair>(reader);
                        if (x == 0) id = new ShortGuid(reader);
                    }

                    reader.BaseStream.Position = (scriptStartOffset * 4) + 4;
                    string name = Utilities.ReadString(reader);

                    //The same tidy-up the full loader applies (CommandsUtils.SetPrettyNames and the root
                    //rename): a shipped PAK stores names upper-cased and the root as a build machine path,
                    //while the vanilla path table knows the proper-cased name for every shipped composite.
                    //A level OpenCAGE has saved already holds tidy names, and the table lookup then agrees.
                    string prettyPath = CustomTable.Vanilla.CompositePaths.GetPrettyPath(id);
                    if (prettyPath != "") name = prettyPath;
                    name = name.Replace("/", "\\");
                    if (id == rootId)
                    {
                        string[] nameSplit = name.Split('\\');
                        name = nameSplit[nameSplit.Length - 1];
                    }

                    //Function entities are (entity ID, function ID) pairs; a function ID that is not one of
                    //the engine's own functions names a composite this one instances
                    List<ShortGuid> instances = new List<ShortGuid>();
                    OffsetPair functions = offsetPairs[(int)CompositeFileData.FUNCTION_ENTITIES];
                    reader.BaseStream.Position = functions.GlobalOffset * 4;
                    for (int y = 0; y < functions.EntryCount; y++)
                    {
                        reader.BaseStream.Position = (functions.GlobalOffset * 4) + (y * 8) + 4;
                        ShortGuid functionID = new ShortGuid(reader);
                        if (functionID.IsFunctionType || instances.Contains(functionID))
                            continue;
                        instances.Add(functionID);
                    }

                    entries.Add(new CompositeIndexEntry() { ID = id, Name = name, Instances = instances, IsRoot = id == rootId });
                }
                return entries;
            }
        }
    }
}
