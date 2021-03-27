using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace TestProject.File_Handlers.Shaders
{
    public class IDXRemap
    {
        public static alien_shader_idx_remap Load(string FullFilePath)
        {
            alien_shader_idx_remap Result = new alien_shader_idx_remap();

            Result.PAK = File_Handlers.PAK.PAK.Load(FullFilePath, false);
            Result.Datas = new List<alien_shader_idx_remap_data>(Result.PAK.Header.EntryCount);

            for (int EntryIndex = 0; EntryIndex < Result.PAK.Header.EntryCount; ++EntryIndex)
            {
                Result.Datas.Add(Utilities.Consume<alien_shader_idx_remap_data>(Result.PAK.EntryDatas[EntryIndex]));
            }

            return Result;
        }
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct alien_shader_idx_remap_data
{
    public int Index;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
    public int[] Unknown0_; //3
};

public struct alien_shader_idx_remap
{
    public alien_pak PAK;
    public List<alien_shader_idx_remap_data> Datas;
};