﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
#if UNITY_EDITOR || UNITY_STANDALONE
using UnityEngine;
#else
using System.Numerics;
#endif

namespace CATHODE.Models
{
    public class ModelBIN
    {
        public static alien_model_bin Load(string filepath)
        {
            alien_model_bin Result = new alien_model_bin();
            BinaryReader Stream = new BinaryReader(File.OpenRead(filepath));

            alien_model_bin_header Header = Utilities.Consume<alien_model_bin_header>(ref Stream);

            Result.VertexBufferFormats = new List<alien_vertex_buffer_format>(Header.VertexInputCount);
            for (int EntryIndex = 0; EntryIndex < Header.VertexInputCount; ++EntryIndex)
            {
                long startPos = Stream.BaseStream.Position;
                int count = 1;
                while (Stream.ReadByte() != 0xFF)
                {
                    Stream.BaseStream.Position += Marshal.SizeOf(typeof(alien_vertex_buffer_format_element)) - 1;
                    count++;
                }
                Stream.BaseStream.Position = startPos;

                alien_vertex_buffer_format VertexInput = new alien_vertex_buffer_format();
                VertexInput.ElementCount = count;
                VertexInput.Elements = Utilities.ConsumeArray<alien_vertex_buffer_format_element>(ref Stream, VertexInput.ElementCount);
                Result.VertexBufferFormats.Add(VertexInput);
            }

            int FileNamesStartCount = Stream.ReadInt32();
            byte[] FileNamesStart = Stream.ReadBytes(FileNamesStartCount);

            List<string> ModelFilePaths = new List<string>(Header.ModelCount);
            List<string> ModelPartNames = new List<string>(Header.ModelCount);

            List<alien_model_bin_model_info> ModelInfos = Utilities.ConsumeArray<alien_model_bin_model_info>(ref Stream, Header.ModelCount);
            for (int EntryIndex = 0; EntryIndex < Header.ModelCount; ++EntryIndex)
            {
                alien_model_bin_model_info ModelInfo = ModelInfos[EntryIndex];
                ModelFilePaths.Add(Utilities.ReadString(FileNamesStart, ModelInfo.FileNameOffset).Replace('\\', '/'));
                ModelPartNames.Add(Utilities.ReadString(FileNamesStart, ModelInfo.ModelPartNameOffset).Replace('\\', '/'));
            }

            int BoneBufferCount = Stream.ReadInt32();
            byte[] BoneBuffer = Utilities.ConsumeArray<byte>(ref Stream, BoneBufferCount).ToArray();

            //TODO: implement bone parsing

            Result.Header = Header;
            Result.Models = ModelInfos;
            Result.ModelFilePaths = ModelFilePaths;
            Result.ModelLODPartNames = ModelPartNames;

            return Result;
        }
    }
}


public enum alien_vertex_input_type
{
    AlienVertexInputType_v3 = 0x03,
    // TODO: Present at 'bsp_torrens' but I haven't seen models that contain that being rendered yet.
    AlienVertexInputType_Unknown0_ = 0x04,
    AlienVertexInputType_u32_C = 0x05,
    AlienVertexInputType_v4u8_i = 0x06,
    AlienVertexInputType_v4u8_f = 0x09,
    AlienVertexInputType_v2s16_UV = 0x0A,
    AlienVertexInputType_v4s16_f = 0x0B,
    AlienVertexInputType_v4u8_NTB = 0x0F,
    AlienVertexInputType_u16 = 0x13,
};

public enum alien_vertex_input_slot
{
    AlienVertexInputSlot_P = 0x01,
    AlienVertexInputSlot_BW = 0x02, // NOTE: Bone Weights
    AlienVertexInputSlot_BI = 0x03, // NOTE: Bone Indices
    AlienVertexInputSlot_N = 0x04,
    AlienVertexInputSlot_UV = 0x06,
    AlienVertexInputSlot_T = 0x07, // NOTE: Tangent
    AlienVertexInputSlot_B = 0x08, // NOTE: Bitangent
    AlienVertexInputSlot_C = 0x0A, // NOTE: Color? Specular? What is this?
};

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct alien_model_bin_header
{
    public fourcc FourCC;
    public int ModelCount;
    public Int16 Unknown0_;
    public Int16 Version;
    public int VertexInputCount;
};

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct alien_model_bin_model_info
{
    public int FileNameOffset;
    public int UnknownZero_; // NOTE: Always 0 on starting area.
    public int ModelPartNameOffset;
    public float UnknownValue0_; // NOTE: Always 0 on starting area.
    public Vector3 AABBMin;
    public float LODMinDistance_;
    public Vector3 AABBMax;
    public float LODMaxDistance_;
    public int NextInLODGroup_;
    public int FirstModelInGroupForNextLOD_;
    public int MaterialLibraryIndex;
    public uint Unknown2_; // NOTE: Flags?
    public int UnknownIndex; // NOTE: -1 means no index. Seems to be related to Next/Parent.
    public uint BlockSize;
    public int CollisionIndex_; // NODE: If this is not -1, model piece name starts with "COL_" and are always character models.
    public int BoneArrayOffset_; // TODO: This indexes on the leftover data and points to an array of linearly growing indices.
                                 //  And the array count is BoneCount.
    public UInt16 VertexFormatIndex;
    public UInt16 VertexFormatIndexLowDetail;
    public UInt16 VertexFormatIndexDefault_; // TODO: This is always equals to the VertexFormatIndex
    public UInt16 ScaleFactor;
    public Int16 HeadRelated_; // NOTE: Seems to be valid on some 'HEAD' models, otherwise -1. Maybe morphing related???
    public UInt16 VertexCount;
    public UInt16 IndexCount;
    public UInt16 BoneCount;
};

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct alien_vertex_buffer_format_element
{
    public int ArrayIndex;
    public int Offset; // NOTE: Offset within data structure, generally not important.
    public alien_vertex_input_type VariableType; //(int)
    public alien_vertex_input_slot ShaderSlot; //(int)
    public int VariantIndex; // NOTE: Variant index such as UVs: (UV0, UV1, UV2...)
    public int Unknown_; // NOTE: Seems to be always 2?
};

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct fourcc
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public char[] V; //4
}

public struct alien_vertex_buffer_format
{
    public int ElementCount;
    public List<alien_vertex_buffer_format_element> Elements;
};

public struct alien_model_bin
{
    public byte[] Buffer;
    public alien_model_bin_header Header;
    public List<alien_vertex_buffer_format> VertexBufferFormats;
    public List<alien_model_bin_model_info> Models;
    public List<string> ModelFilePaths;
    public List<string> ModelLODPartNames;
};