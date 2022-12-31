using CathodeLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace CATHODE.Assets
{
    public class AlienVBF
    {
        public int ElementCount;
        public List<AlienVBFE> Elements;
    }
    
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct AlienVBFE
    {
        public int ArrayIndex;
        public int Offset; // NOTE: Offset within data structure, generally not important.
        public VBFE_InputType VariableType; //(int)
        public VBFE_InputSlot ShaderSlot; //(int)
        public int VariantIndex; // NOTE: Variant index such as UVs: (UV0, UV1, UV2...)
        public int Unknown_; // NOTE: Seems to be always 2?
    };

    public enum VBFE_InputType
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

    public enum VBFE_InputSlot
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
    public struct CS2
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
}
