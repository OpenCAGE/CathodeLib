using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace CATHODE.Misc
{
    public class EnvironmentAnimationDAT
    {
        public static alien_animation_dat Load(string filepath)
        {
            alien_animation_dat Result = new alien_animation_dat();
            BinaryReader Stream = new BinaryReader(File.OpenRead(filepath));

            Result.Header = Utilities.Consume<alien_animation_header>(ref Stream);
            Result.Entries0 = Utilities.ConsumeArray<alien_animation_entry0>(ref Stream, (int)Result.Header.EntryCount0);
            Result.Matrices0 = Utilities.ConsumeArray<Matrix4x4>(ref Stream, (int)Result.Header.EntryCount0);
            Result.Matrices1 = Utilities.ConsumeArray<Matrix4x4>(ref Stream, (int)Result.Header.EntryCount0);
            Result.IDs0 = Utilities.ConsumeArray<int>(ref Stream, (int)Result.Header.EntryCount0);
            Result.IDs1 = Utilities.ConsumeArray<int>(ref Stream, (int)Result.Header.EntryCount0);
            Result.Entries1 = Utilities.ConsumeArray<alien_animation_entry1>(ref Stream, (int)Result.Header.EntryCount0);

            Stream.Close();
            return Result;
        }
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct alien_animation_header
{
    public int Version;
    public int TotalFileSize;

    public int MatricesOffset0;
    public int MatrixCount0;

    public int MatricesOffset1;
    public int MatrixCount1;

    public int EntriesOffset1;
    public int EntryCount1;

    public int EntriesOffset0;
    public int EntryCount0;

    public int IDsOffset0;
    public int IDCount0;

    public int IDsOffset1;
    public int IDCount1;

    public int Unknown0_;
    public int Unknown1_;
};

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct alien_animation_entry0
{
    public Matrix4x4 Matrix;
    public int ID;
    public int UnknownZero0_;
    public int ResourceIndex;
    public int IDCount0;
    public int IDIndex0;
    public int IDCount1;
    public int IDIndex1;
    public int MatrixCount;
    public int MatrixIndex;
    public int EntryIndex1;
    public int EntryCount1;
    public int UnknownZero1_;
};

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct alien_animation_entry1
{
    public int ID;
    public Vector3 P;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
    public int[] V;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    public byte[] Unknown_;
};

public struct alien_animation_dat
{
    public alien_animation_header Header;
    public List<alien_animation_entry0> Entries0;
    public List<Matrix4x4> Matrices0;
    public List<Matrix4x4> Matrices1;
    public List<int> IDs0;
    public List<int> IDs1;
    public List<alien_animation_entry1> Entries1;
};