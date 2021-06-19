using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace CATHODE.Misc
{
    class CollisionMAP
    {
        public static alien_collision_map Load(string FullFilePath)
        {
            alien_collision_map Result = new alien_collision_map();
            BinaryReader Stream = new BinaryReader(File.OpenRead(FullFilePath));

            Result.Header = Utilities.Consume<alien_collision_map_header>(ref Stream);
            Result.Entries = Utilities.ConsumeArray<alien_collision_map_entry>(ref Stream, Result.Header.EntryCount);

            return Result;
        }
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct alien_collision_map_header
{
    public int DataSize;
    public int EntryCount;
};

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct alien_collision_map_entry
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public int[] Unknowns1; //12
    public int ID;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 7)]
    public int[] Unknowns2; //12
};

public struct alien_collision_map
{
    public alien_collision_map_header Header;
    public List<alien_collision_map_entry> Entries;
};