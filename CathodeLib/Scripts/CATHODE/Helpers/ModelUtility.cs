using System;
using System.Collections.Generic;
using System.IO;
using CATHODE;

#if UNITY_EDITOR || UNITY_STANDALONE
using UnityEngine;
#elif GODOT
using Godot;
using System.Numerics;
using Matrix4x4 = System.Numerics.Matrix4x4;
using Quaternion = System.Numerics.Quaternion;
using Vector2 = Godot.Vector2;
using Vector3 = Godot.Vector3;
using Vector4 = Godot.Vector4;
using Color = Godot.Color;
#else
using System.Numerics;
using System.Drawing;
#endif

namespace CathodeLib
{
    public class cMesh : IEquatable<cMesh>
    {
        public List<UInt16> Indices = new List<UInt16>();
        public List<Vector3> Vertices = new List<Vector3>();
        public List<Vector3> Normals = new List<Vector3>();
        public List<Vector4> BiNormals = new List<Vector4>();
        public List<Vector4> Tangents = new List<Vector4>();
        public List<Color> Colours = new List<Color>();
        public List<Vector2>[] UVs = new List<Vector2>[0];

        public List<Vector4> BoneIndexes = new List<Vector4>();
        public List<Vector4> BoneWeights = new List<Vector4>();

        public static bool operator ==(cMesh x, cMesh y)
        {
            if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
            if (ReferenceEquals(y, null)) return false;
            if (ReferenceEquals(x, y)) return true;
            if (!ListsEqual(x.Indices, y.Indices)) return false;
            if (!ListsEqual(x.Vertices, y.Vertices)) return false;
            if (!ListsEqual(x.Normals, y.Normals)) return false;
            if (!ListsEqual(x.BiNormals, y.BiNormals)) return false;
            if (!ListsEqual(x.Tangents, y.Tangents)) return false;
            if (!ListsEqual(x.Colours, y.Colours)) return false;
            if (!UVArraysEqual(x.UVs, y.UVs)) return false;
            if (!ListsEqual(x.BoneIndexes, y.BoneIndexes)) return false;
            if (!ListsEqual(x.BoneWeights, y.BoneWeights)) return false;
            return true;
        }

        public static bool operator !=(cMesh x, cMesh y)
        {
            return !(x == y);
        }

        public bool Equals(cMesh other)
        {
            return this == other;
        }

        public override bool Equals(object obj)
        {
            return obj is cMesh mesh && this.Equals(mesh);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + (Indices?.Count ?? 0);
                foreach (var index in Indices ?? new List<UInt16>())
                {
                    hash = hash * 23 + index.GetHashCode();
                }
                hash = hash * 23 + (Vertices?.Count ?? 0);
                foreach (var vertex in Vertices ?? new List<Vector3>())
                {
                    hash = hash * 23 + vertex.GetHashCode();
                }
                hash = hash * 23 + (Normals?.Count ?? 0);
                foreach (var normal in Normals ?? new List<Vector3>())
                {
                    hash = hash * 23 + normal.GetHashCode();
                }
                hash = hash * 23 + (BiNormals?.Count ?? 0);
                foreach (var binormal in BiNormals ?? new List<Vector4>())
                {
                    hash = hash * 23 + binormal.GetHashCode();
                }
                hash = hash * 23 + (Tangents?.Count ?? 0);
                foreach (var tangent in Tangents ?? new List<Vector4>())
                {
                    hash = hash * 23 + tangent.GetHashCode();
                }
                hash = hash * 23 + (Colours?.Count ?? 0);
                foreach (var colour in Colours ?? new List<Color>())
                {
                    hash = hash * 23 + colour.GetHashCode();
                }
                hash = hash * 23 + (UVs?.Length ?? 0);
                if (UVs != null)
                {
                    for (int i = 0; i < UVs.Length; i++)
                    {
                        hash = hash * 23 + (UVs[i]?.Count ?? 0);
                        if (UVs[i] != null)
                        {
                            foreach (var uv in UVs[i])
                            {
                                hash = hash * 23 + uv.GetHashCode();
                            }
                        }
                    }
                }
                hash = hash * 23 + (BoneIndexes?.Count ?? 0);
                foreach (var boneIndex in BoneIndexes ?? new List<Vector4>())
                {
                    hash = hash * 23 + boneIndex.GetHashCode();
                }
                hash = hash * 23 + (BoneWeights?.Count ?? 0);
                foreach (var boneWeight in BoneWeights ?? new List<Vector4>())
                {
                    hash = hash * 23 + boneWeight.GetHashCode();
                }
                return hash;
            }
        }

        private static bool ListsEqual<T>(List<T> x, List<T> y)
        {
            if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
            if (ReferenceEquals(y, null)) return false;
            if (x.Count != y.Count) return false;
            for (int i = 0; i < x.Count; i++)
            {
                if (!EqualityComparer<T>.Default.Equals(x[i], y[i])) return false;
            }
            return true;
        }

        private static bool UVArraysEqual(List<Vector2>[] x, List<Vector2>[] y)
        {
            if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
            if (ReferenceEquals(y, null)) return false;
            if (x.Length != y.Length) return false;
            for (int i = 0; i < x.Length; i++)
            {
                if (!ListsEqual(x[i], y[i])) return false;
            }
            return true;
        }
    }

    public static class ModelUtility
    {
        /// <summary>
        /// Convert a CS2 submesh to a Mesh object with easier to handle data
        /// </summary>
        public static cMesh ToMesh(this Models.CS2.Component.LOD.Submesh submesh)
        {
            cMesh mesh = new cMesh();

            if (submesh == null || submesh.Data == null || submesh.Data.Length == 0)
                return mesh;
            if (ReferenceEquals(submesh.VertexFormatFull, null) || submesh.VertexFormatFull.Attributes == null)
                return mesh;

            using (BinaryReader reader = new BinaryReader(new MemoryStream(submesh.Data)))
            {
                for (int i = 0; i < submesh.VertexFormatFull.Attributes.Count; ++i)
                {
                    if (i == submesh.VertexFormatFull.Attributes.Count - 1)
                    {
                        for (int x = 0; x < submesh.IndexCount; x++)
                            mesh.Indices.Add(reader.ReadUInt16());
                        continue;
                    }

                    for (int x = 0; x < submesh.VertexCount; ++x)
                    {
                        for (int y = 0; y < submesh.VertexFormatFull.Attributes[i].Count; ++y)
                        {
                            Models.VertexFormat.Attribute attr = submesh.VertexFormatFull.Attributes[i][y];
                            Vector4 v = ReadVertexData(reader, attr.Type);

                            switch (attr.Usage)
                            {
                                case Models.VertexFormat.Usage.Position:
#if UNITY_EDITOR || UNITY_STANDALONE
                                    mesh.Vertices.Add(v * submesh.VertexScale);
#else
                                    mesh.Vertices.Add(new Vector3(v.X * submesh.VertexScale, v.Y * submesh.VertexScale, v.Z * submesh.VertexScale));
#endif
                                    break;
                                case Models.VertexFormat.Usage.BlendWeight:
                                    mesh.BoneWeights.Add(v);
                                    break;
                                case Models.VertexFormat.Usage.BlendIndices:
                                    mesh.BoneIndexes.Add(v);
                                    break;
                                case Models.VertexFormat.Usage.Normal:
#if UNITY_EDITOR || UNITY_STANDALONE
                                    mesh.Normals.Add(v);
#else
                                    mesh.Normals.Add(new Vector3(v.X, v.Y, v.Z));
#endif
                                    break;
                                case Models.VertexFormat.Usage.TexCoord:
                                    if (attr.Index >= mesh.UVs.Length)
                                        Array.Resize(ref mesh.UVs, attr.Index + 1);
                                    if (mesh.UVs[attr.Index] == null)
                                        mesh.UVs[attr.Index] = new List<Vector2>();
#if UNITY_EDITOR || UNITY_STANDALONE
                                    mesh.UVs[attr.Index].Add(v * 16.0f);
#else
                                    mesh.UVs[attr.Index].Add(new Vector2(v.X * 16.0f, v.Y * 16.0f)); // TODO: is Z/W used?
#endif
                                    break;
                                case Models.VertexFormat.Usage.Tangent:
                                    mesh.Tangents.Add(v);
                                    break;
                                case Models.VertexFormat.Usage.Binormal:
                                    mesh.BiNormals.Add(v);
                                    break;
                                case Models.VertexFormat.Usage.Color:
#if UNITY_EDITOR || UNITY_STANDALONE
                                    mesh.Colours.Add(v);
#elif GODOT
                                    mesh.Colours.Add(new Color(v.X, v.Y, v.Z, v.W));
#else
                                    mesh.Colours.Add(Color.FromArgb(ColourByte(v.W), ColourByte(v.X), ColourByte(v.Y), ColourByte(v.Z)));
#endif
                                    break;
                            }
                        }
                    }
                    Utilities.Align(reader, 16);
                }
            }
            return mesh;
        }

        /// <summary>
        /// Convert a Mesh object to CS2 submesh data and update the relevant submesh parameters to match
        /// </summary>
        public static void ToSubmeshData(this cMesh mesh, Models.CS2.Component.LOD.Submesh submesh)
        {
            if (mesh == null || submesh?.VertexFormatFull == null)
                return;

            submesh.VertexCount = mesh.Vertices.Count;
            submesh.IndexCount = mesh.Indices.Count;

            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                for (int i = 0; i < submesh.VertexFormatFull.Attributes.Count; ++i)
                {
                    if (i == submesh.VertexFormatFull.Attributes.Count - 1)
                    {
                        for (int x = 0; x < mesh.Indices.Count; x++)
                            writer.Write(mesh.Indices[x]);
                        continue;
                    }

                    for (int x = 0; x < mesh.Vertices.Count; ++x)
                    {
                        for (int y = 0; y < submesh.VertexFormatFull.Attributes[i].Count; ++y)
                        {
                            Models.VertexFormat.Attribute attr = submesh.VertexFormatFull.Attributes[i][y];
                            Vector4 v = GetVertexDataForAttribute(mesh, attr, x, submesh.VertexScale);
                            WriteVertexData(writer, v, attr.Type);
                        }
                    }
                    Utilities.Align(writer, 16);
                }
                Utilities.Align(writer, 16);
                submesh.Data = stream.ToArray();
            }
        }

        /// <summary>
        /// Whether a material's shader reads the vertex colour stream: its family's VERTEX_COLOUR feature is on.
        /// </summary>
        public static bool ReadsVertexColour(Materials.Material material)
        {
            Shaders.Shader shader = material?.Shader;
            if (shader == null) return false;
            int? bit = CATHODE.ShaderTypes.ShaderUtility.GetShaderFunctionalityIndex(shader.Ubershader, CATHODE.ShaderTypes.ShaderIndexType.FEATURES, "VERTEX_COLOUR");
            return bit != null && (shader.UbershaderFeatureFlags & (1L << bit.Value)) != 0;
        }

        /// <summary>
        /// What a material's vertex shader takes in, read from its bytecode's input signature (system values left out), or
        /// null when there is no bytecode to read.
        /// </summary>
        public static List<Models.VertexFormat.Attribute> VertexShaderInputs(Materials.Material material)
        {
            byte[] dxbc = material?.Shader?.VertexShader;
            if (dxbc == null || dxbc.Length < 32 || dxbc[0] != 'D' || dxbc[1] != 'X' || dxbc[2] != 'B' || dxbc[3] != 'C')
                return null;
            List<Models.VertexFormat.Attribute> inputs = new List<Models.VertexFormat.Attribute>();
            int chunks = BitConverter.ToInt32(dxbc, 28);
            for (int c = 0; c < chunks; c++)
            {
                int at = BitConverter.ToInt32(dxbc, 32 + (c * 4));
                if (at < 0 || at + 16 > dxbc.Length || dxbc[at] != 'I' || dxbc[at + 1] != 'S' || dxbc[at + 2] != 'G' || dxbc[at + 3] != 'N')
                    continue;
                int start = at + 8;
                int count = BitConverter.ToInt32(dxbc, start);
                for (int e = 0; e < count; e++)
                {
                    int element = start + 8 + (e * 24);
                    int name = start + BitConverter.ToInt32(dxbc, element);
                    int end = name;
                    while (end < dxbc.Length && dxbc[end] != 0) end++;
                    Models.VertexFormat.Usage usage;
                    switch (System.Text.Encoding.ASCII.GetString(dxbc, name, end - name))
                    {
                        case "POSITION": usage = Models.VertexFormat.Usage.Position; break;
                        case "NORMAL": usage = Models.VertexFormat.Usage.Normal; break;
                        case "TANGENT": usage = Models.VertexFormat.Usage.Tangent; break;
                        case "BINORMAL": usage = Models.VertexFormat.Usage.Binormal; break;
                        case "BLENDINDICES": usage = Models.VertexFormat.Usage.BlendIndices; break;
                        case "BLENDWEIGHT": usage = Models.VertexFormat.Usage.BlendWeight; break;
                        case "TEXCOORD": usage = Models.VertexFormat.Usage.TexCoord; break;
                        case "COLOR": usage = Models.VertexFormat.Usage.Color; break;
                        default: continue; //SV_VertexID and the like come from the GPU, not the buffer
                    }
                    inputs.Add(new Models.VertexFormat.Attribute(Models.VertexFormat.Type.Unknown, usage, BitConverter.ToInt32(dxbc, element + 4)));
                }
            }
            return inputs;
        }

        /* Every shipped submesh gives its material's vertex shader each input it takes - 24,903 over three levels, not one
           short. The game puts the input layout together from the two, and one short of an input is drawn through some other
           layout or not at all: an imported gun given a material reading vertex colour drew as a box of random triangles as
           big as its VertexScale (25 Oct 2026), and a player model whose shader takes binormals and a second UV set (Ripley's
           flight suit's, copied) didn't draw (26 Oct). */
        /// <summary>
        /// Give a submesh whatever its material's vertex shader takes that it hasn't got, made the way the game's own models
        /// have them: a white colour (no tint, which is what a model file without colours means), a binormal along the
        /// texture's V across the normal and tangent, extra UV sets at zero (the game's characters' TEXCOORD7 is), and a
        /// normal or tangent from the triangles where even those are missing. They go on the end of the stream holding the
        /// normal (a stream of their own when there is none); every other byte stays as it was. Without the shader's bytecode
        /// to read, only a colour is given, to a material with its VERTEX_COLOUR feature on. Returns whether anything changed.
        /// </summary>
        public static bool EnsureShaderInputs(this Models.CS2.Component.LOD.Submesh submesh)
        {
            Models.VertexFormat format = submesh?.VertexFormatFull;
            if (ReferenceEquals(format, null) || format.Attributes == null || format.Attributes.Count < 2 || submesh.Data == null || submesh.Material == null)
                return false;

            bool Has(Models.VertexFormat.Usage usage, int index)
            {
                foreach (List<Models.VertexFormat.Attribute> stream in format.Attributes)
                    foreach (Models.VertexFormat.Attribute attribute in stream)
                        if (attribute.Usage == usage && attribute.Index == index) return true;
                return false;
            }
            List<Models.VertexFormat.Attribute> wanted = VertexShaderInputs(submesh.Material);
            if (wanted == null)
                wanted = ReadsVertexColour(submesh.Material) ? new List<Models.VertexFormat.Attribute>() { new Models.VertexFormat.Attribute(Models.VertexFormat.Type.Unknown, Models.VertexFormat.Usage.Color) } : new List<Models.VertexFormat.Attribute>();

            //What can be made, in the order it goes on: positions and skinning can't be
            List<Models.VertexFormat.Attribute> added = new List<Models.VertexFormat.Attribute>();
            foreach (Models.VertexFormat.Usage usage in new[] { Models.VertexFormat.Usage.Normal, Models.VertexFormat.Usage.Tangent, Models.VertexFormat.Usage.Binormal, Models.VertexFormat.Usage.TexCoord, Models.VertexFormat.Usage.Color })
                foreach (Models.VertexFormat.Attribute input in wanted)
                    if (input.Usage == usage && !Has(input.Usage, input.Index) && !added.Exists(o => o.Usage == input.Usage && o.Index == input.Index))
                        added.Add(new Models.VertexFormat.Attribute(
                            usage == Models.VertexFormat.Usage.Color ? Models.VertexFormat.Type.Color : usage == Models.VertexFormat.Usage.TexCoord ? Models.VertexFormat.Type.S16_2N : Models.VertexFormat.Type.FP32_3,
                            usage, input.Index));
            if (added.Count == 0)
                return false;

            //The last entry stands for the indices; the rest are vertex streams, each padded to 16 bytes
            int streams = format.Attributes.Count - 1;
            int[] strides = new int[streams];
            int target = -1;
            for (int i = 0; i < streams; i++)
            {
                foreach (Models.VertexFormat.Attribute attribute in format.Attributes[i])
                {
                    int size = SizeOf(attribute.Type);
                    if (size < 0)
                        return false;
                    strides[i] += size;
                    if (attribute.Usage == Models.VertexFormat.Usage.Normal && target == -1)
                        target = i;
                }
            }
            //The partial format (depth and shadow passes) describes the first stream too, so that one is never made longer
            if (target == 0)
                target = -1;

            int expected = 0;
            for (int i = 0; i < streams; i++)
                expected += Padded(submesh.VertexCount * strides[i]);
            expected += Padded(submesh.IndexCount * 2);
            if (expected != submesh.Data.Length)
                return false;

            float[] normals = null, tangents = null, binormals = null;
            if (added.Exists(o => o.Usage == Models.VertexFormat.Usage.Normal || o.Usage == Models.VertexFormat.Usage.Tangent || o.Usage == Models.VertexFormat.Usage.Binormal))
                if (!ShadingBasis(submesh, strides, out normals, out tangents, out binormals))
                    return false;

            byte[] old = submesh.Data;
            using (MemoryStream stream = new MemoryStream(old.Length + Padded(submesh.VertexCount * 40)))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                void WriteAdded(int v)
                {
                    foreach (Models.VertexFormat.Attribute attribute in added)
                    {
                        switch (attribute.Usage)
                        {
                            case Models.VertexFormat.Usage.Color:
                                writer.Write(0xFFFFFFFFu);
                                break;
                            case Models.VertexFormat.Usage.TexCoord:
                                writer.Write(0);
                                break;
                            default:
                                float[] basis = attribute.Usage == Models.VertexFormat.Usage.Normal ? normals : attribute.Usage == Models.VertexFormat.Usage.Tangent ? tangents : binormals;
                                writer.Write(basis[v * 3]);
                                writer.Write(basis[(v * 3) + 1]);
                                writer.Write(basis[(v * 3) + 2]);
                                break;
                        }
                    }
                }

                int read = 0;
                for (int i = 0; i < streams; i++)
                {
                    int length = submesh.VertexCount * strides[i];
                    if (i == target)
                    {
                        for (int v = 0; v < submesh.VertexCount; v++)
                        {
                            writer.Write(old, read + (v * strides[i]), strides[i]);
                            WriteAdded(v);
                        }
                    }
                    else
                    {
                        writer.Write(old, read, length);
                    }
                    Utilities.Align(writer, 16);
                    read += Padded(length);
                }
                if (target == -1)
                {
                    for (int v = 0; v < submesh.VertexCount; v++)
                        WriteAdded(v);
                    Utilities.Align(writer, 16);
                }
                //The indices, and their padding
                writer.Write(old, read, old.Length - read);
                submesh.Data = stream.ToArray();
            }

            //A format of its own: a loaded one is shared by every submesh with the same layout
            Models.VertexFormat completed = new Models.VertexFormat();
            foreach (List<Models.VertexFormat.Attribute> attributes in format.Attributes)
                completed.Attributes.Add(new List<Models.VertexFormat.Attribute>(attributes));
            if (target != -1)
                completed.Attributes[target].AddRange(added);
            else
                completed.Attributes.Insert(streams, added);
            submesh.VertexFormatFull = completed;
            return true;
        }

        /* The character shaders take normal, tangent and binormal packed into bytes (Dec3N: unsigned, about 128) and undo
           it with x*2-1 - every shipped submesh drawn with CA_CHARACTER, CA_SKIN, CA_SKIN_OCCLUSION or CA_EYE (1,006 over
           two levels) stores them so. Floats, as the environment shaders take them, came out as -3..1: an imported player
           model drawn black (26 Oct 2026). Hair and low-LOD characters ship both ways, so their shaders are left to the mesh. */
        private static readonly HashSet<CATHODE.ShaderTypes.SHADER_LIST> PackedBasisShaders = new HashSet<CATHODE.ShaderTypes.SHADER_LIST>()
        {
            CATHODE.ShaderTypes.SHADER_LIST.CA_CHARACTER,
            CATHODE.ShaderTypes.SHADER_LIST.CA_SKIN,
            CATHODE.ShaderTypes.SHADER_LIST.CA_SKIN_OCCLUSION,
            CATHODE.ShaderTypes.SHADER_LIST.CA_EYE,
        };

        /// <summary>
        /// Store a submesh's float normals, tangents and binormals packed (Dec3N) when its material's shader is one that only
        /// reads them packed. Every other byte stays as it was. Returns whether anything changed.
        /// </summary>
        public static bool EnsurePackedBasis(this Models.CS2.Component.LOD.Submesh submesh)
        {
            Models.VertexFormat format = submesh?.VertexFormatFull;
            Shaders.Shader shader = submesh?.Material?.Shader;
            if (shader == null || !PackedBasisShaders.Contains(shader.Ubershader) || ReferenceEquals(format, null) || format.Attributes == null || format.Attributes.Count < 2 || submesh.Data == null)
                return false;

            bool Packs(Models.VertexFormat.Attribute attribute) => attribute.Type == Models.VertexFormat.Type.FP32_3 &&
                (attribute.Usage == Models.VertexFormat.Usage.Normal || attribute.Usage == Models.VertexFormat.Usage.Tangent || attribute.Usage == Models.VertexFormat.Usage.Binormal);

            int streams = format.Attributes.Count - 1;
            int[] strides = new int[streams];
            bool any = false;
            for (int i = 0; i < streams; i++)
            {
                foreach (Models.VertexFormat.Attribute attribute in format.Attributes[i])
                {
                    int size = SizeOf(attribute.Type);
                    if (size < 0)
                        return false;
                    strides[i] += size;
                    if (Packs(attribute))
                    {
                        //The partial format (depth and shadow passes) describes the first stream too
                        if (i == 0) return false;
                        any = true;
                    }
                }
            }
            if (!any)
                return false;
            int expected = 0;
            for (int i = 0; i < streams; i++)
                expected += Padded(submesh.VertexCount * strides[i]);
            expected += Padded(submesh.IndexCount * 2);
            if (expected != submesh.Data.Length)
                return false;

            byte[] old = submesh.Data;
            using (MemoryStream stream = new MemoryStream(old.Length))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                int read = 0;
                for (int i = 0; i < streams; i++)
                {
                    int length = submesh.VertexCount * strides[i];
                    for (int v = 0; v < submesh.VertexCount; v++)
                    {
                        int at = read + (v * strides[i]);
                        foreach (Models.VertexFormat.Attribute attribute in format.Attributes[i])
                        {
                            int size = SizeOf(attribute.Type);
                            if (Packs(attribute))
                            {
                                writer.Write(ToBiasedByte(BitConverter.ToSingle(old, at)));
                                writer.Write(ToBiasedByte(BitConverter.ToSingle(old, at + 4)));
                                writer.Write(ToBiasedByte(BitConverter.ToSingle(old, at + 8)));
                                writer.Write((byte)0);
                            }
                            else
                            {
                                writer.Write(old, at, size);
                            }
                            at += size;
                        }
                    }
                    Utilities.Align(writer, 16);
                    read += Padded(length);
                }
                //The indices, and their padding
                writer.Write(old, read, old.Length - read);
                submesh.Data = stream.ToArray();
            }

            //A format of its own: a loaded one is shared by every submesh with the same layout
            Models.VertexFormat packed = new Models.VertexFormat();
            foreach (List<Models.VertexFormat.Attribute> attributes in format.Attributes)
            {
                List<Models.VertexFormat.Attribute> copy = new List<Models.VertexFormat.Attribute>();
                foreach (Models.VertexFormat.Attribute attribute in attributes)
                    copy.Add(Packs(attribute) ? new Models.VertexFormat.Attribute(Models.VertexFormat.Type.Dec3N, attribute.Usage, attribute.Index) : attribute);
                packed.Attributes.Add(copy);
            }
            submesh.VertexFormatFull = packed;
            return true;
        }

        /* Every shipped skinned submesh lists its rig's bones in order, from 0 up - 2,151 over four levels, not one otherwise.
           The game takes a vertex's blend indices as bones of the rig, not as places in the submesh's list, so a shorter list
           (an import's own, of only the bones it uses) set every vertex on the wrong bone: an imported player model kept
           near its rest pose, turned and stretched, while the rig under it moved (26 Oct 2026). */
        /// <summary>
        /// Give a skinned submesh the bone list the game's own have - the rig's bones in order, from 0 to the highest it
        /// uses - with its blend indices renumbered to match. Every other byte stays as it was. Returns whether anything changed.
        /// </summary>
        public static bool EnsureRigOrderBones(this Models.CS2.Component.LOD.Submesh submesh)
        {
            Models.VertexFormat format = submesh?.VertexFormatFull;
            if (submesh?.Bones == null || submesh.Bones.Count == 0 || ReferenceEquals(format, null) || format.Attributes == null || format.Attributes.Count < 2 || submesh.Data == null)
                return false;
            bool inOrder = true;
            int highest = 0, lowest = 0;
            for (int i = 0; i < submesh.Bones.Count; i++)
            {
                if (submesh.Bones[i] != i) inOrder = false;
                highest = Math.Max(highest, submesh.Bones[i]);
                lowest = Math.Min(lowest, submesh.Bones[i]);
            }
            if (inOrder || highest > 255 || lowest < 0)
                return false;

            //Where the blend indices are: their stream, their place in it, and the streams before it
            int streams = format.Attributes.Count - 1;
            int[] strides = new int[streams];
            int indexStream = -1, indexOffset = 0;
            for (int i = 0; i < streams; i++)
            {
                foreach (Models.VertexFormat.Attribute attribute in format.Attributes[i])
                {
                    int size = SizeOf(attribute.Type);
                    if (size < 0)
                        return false;
                    if (attribute.Usage == Models.VertexFormat.Usage.BlendIndices && indexStream == -1)
                    {
                        if (attribute.Type != Models.VertexFormat.Type.U8_4)
                            return false;
                        indexStream = i;
                        indexOffset = strides[i];
                    }
                    strides[i] += size;
                }
            }
            if (indexStream == -1)
                return false;
            int expected = 0, streamStart = 0;
            for (int i = 0; i < streams; i++)
            {
                if (i == indexStream) streamStart = expected;
                expected += Padded(submesh.VertexCount * strides[i]);
            }
            expected += Padded(submesh.IndexCount * 2);
            if (expected != submesh.Data.Length)
                return false;

            byte[] data = (byte[])submesh.Data.Clone();
            for (int v = 0; v < submesh.VertexCount; v++)
            {
                int at = streamStart + (v * strides[indexStream]) + indexOffset;
                for (int k = 0; k < 4; k++)
                    data[at + k] = (byte)(data[at + k] < submesh.Bones.Count ? submesh.Bones[data[at + k]] : 0);
            }
            submesh.Data = data;
            submesh.Bones.Clear();
            for (int i = 0; i <= highest; i++)
                submesh.Bones.Add(i);
            return true;
        }

        /* Each vertex's normal, tangent and binormal (three floats a vertex each): the mesh's own normal and tangent where it
           has them, else from its triangles and first UV set; the binormal across the two, turned to run along the texture's
           V as every shipped binormal does (mirrored UVs turn it over). False when the data can't be read. */
        private static bool ShadingBasis(Models.CS2.Component.LOD.Submesh submesh, int[] strides, out float[] normals, out float[] tangents, out float[] binormals)
        {
            int count = submesh.VertexCount;
            normals = new float[count * 3];
            tangents = new float[count * 3];
            binormals = new float[count * 3];
            float[] positions = new float[count * 3], uvs = new float[count * 2];
            bool hasNormal = false, hasTangent = false;
            ushort[] indices = new ushort[submesh.IndexCount];
            try
            {
                using (BinaryReader reader = new BinaryReader(new MemoryStream(submesh.Data)))
                {
                    for (int i = 0; i < strides.Length; i++)
                    {
                        long start = reader.BaseStream.Position;
                        for (int v = 0; v < count; v++)
                        {
                            foreach (Models.VertexFormat.Attribute attribute in submesh.VertexFormatFull.Attributes[i])
                            {
                                var value = ReadVertexData(reader, attribute.Type);
                                float[] into = null; int width = 3;
                                if (attribute.Usage == Models.VertexFormat.Usage.Position) into = positions;
                                else if (attribute.Usage == Models.VertexFormat.Usage.Normal) { into = normals; hasNormal = true; }
                                else if (attribute.Usage == Models.VertexFormat.Usage.Tangent) { into = tangents; hasTangent = true; }
                                else if (attribute.Usage == Models.VertexFormat.Usage.TexCoord && attribute.Index == 0) { into = uvs; width = 2; }
                                if (into == null) continue;
                                into[v * width] = value.X;
                                into[(v * width) + 1] = value.Y;
                                if (width == 3) into[(v * width) + 2] = value.Z;
                            }
                        }
                        reader.BaseStream.Position = start + Padded(count * strides[i]);
                    }
                    for (int x = 0; x < indices.Length; x++)
                        indices[x] = reader.ReadUInt16();
                }
            }
            catch
            {
                return false;
            }

            //Face normals and UV directions, summed per vertex (larger triangles count for more)
            float[] faceNormals = new float[count * 3], uDirections = new float[count * 3], vDirections = new float[count * 3];
            for (int t = 0; t + 2 < indices.Length; t += 3)
            {
                int a = indices[t], b = indices[t + 1], c = indices[t + 2];
                if (a >= count || b >= count || c >= count) continue;
                float e1x = positions[b * 3] - positions[a * 3], e1y = positions[(b * 3) + 1] - positions[(a * 3) + 1], e1z = positions[(b * 3) + 2] - positions[(a * 3) + 2];
                float e2x = positions[c * 3] - positions[a * 3], e2y = positions[(c * 3) + 1] - positions[(a * 3) + 1], e2z = positions[(c * 3) + 2] - positions[(a * 3) + 2];
                float nx = (e1y * e2z) - (e1z * e2y), ny = (e1z * e2x) - (e1x * e2z), nz = (e1x * e2y) - (e1y * e2x);
                float du1 = uvs[b * 2] - uvs[a * 2], dv1 = uvs[(b * 2) + 1] - uvs[(a * 2) + 1];
                float du2 = uvs[c * 2] - uvs[a * 2], dv2 = uvs[(c * 2) + 1] - uvs[(a * 2) + 1];
                float det = (du1 * dv2) - (du2 * dv1);
                foreach (int corner in new[] { a, b, c })
                {
                    faceNormals[corner * 3] += nx; faceNormals[(corner * 3) + 1] += ny; faceNormals[(corner * 3) + 2] += nz;
                    if (Math.Abs(det) < 1e-12f) continue;
                    float r = 1.0f / det;
                    uDirections[corner * 3] += ((e1x * dv2) - (e2x * dv1)) * r; uDirections[(corner * 3) + 1] += ((e1y * dv2) - (e2y * dv1)) * r; uDirections[(corner * 3) + 2] += ((e1z * dv2) - (e2z * dv1)) * r;
                    vDirections[corner * 3] += ((e2x * du1) - (e1x * du2)) * r; vDirections[(corner * 3) + 1] += ((e2y * du1) - (e1y * du2)) * r; vDirections[(corner * 3) + 2] += ((e2z * du1) - (e1z * du2)) * r;
                }
            }

            for (int v = 0; v < count; v++)
            {
                int o = v * 3;
                if (!hasNormal) { normals[o] = faceNormals[o]; normals[o + 1] = faceNormals[o + 1]; normals[o + 2] = faceNormals[o + 2]; }
                Normalise(normals, o, 0, 1, 0);
                if (!hasTangent)
                {
                    //The U direction, flattened onto the surface
                    float d = (uDirections[o] * normals[o]) + (uDirections[o + 1] * normals[o + 1]) + (uDirections[o + 2] * normals[o + 2]);
                    tangents[o] = uDirections[o] - (normals[o] * d); tangents[o + 1] = uDirections[o + 1] - (normals[o + 1] * d); tangents[o + 2] = uDirections[o + 2] - (normals[o + 2] * d);
                }
                Normalise(tangents, o, Math.Abs(normals[o]) < 0.9f ? 1 : 0, Math.Abs(normals[o]) < 0.9f ? 0 : 1, 0);
                binormals[o] = (normals[o + 1] * tangents[o + 2]) - (normals[o + 2] * tangents[o + 1]);
                binormals[o + 1] = (normals[o + 2] * tangents[o]) - (normals[o] * tangents[o + 2]);
                binormals[o + 2] = (normals[o] * tangents[o + 1]) - (normals[o + 1] * tangents[o]);
                Normalise(binormals, o, 0, 0, 1);
                if ((binormals[o] * vDirections[o]) + (binormals[o + 1] * vDirections[o + 1]) + (binormals[o + 2] * vDirections[o + 2]) < 0)
                {
                    binormals[o] = -binormals[o]; binormals[o + 1] = -binormals[o + 1]; binormals[o + 2] = -binormals[o + 2];
                }
            }
            return true;
        }

        /* Scale three floats to unit length, or set them to a fallback direction when they have none */
        private static void Normalise(float[] values, int at, float x, float y, float z)
        {
            float length = (float)Math.Sqrt((values[at] * values[at]) + (values[at + 1] * values[at + 1]) + (values[at + 2] * values[at + 2]));
            if (length < 1e-8f) { values[at] = x; values[at + 1] = y; values[at + 2] = z; return; }
            values[at] /= length; values[at + 1] /= length; values[at + 2] /= length;
        }

        private static int Padded(int length)
        {
            return (length + 15) & ~15;
        }

        /* Bytes per vertex of an attribute, or -1 for one whose size isn't known */
        private static int SizeOf(Models.VertexFormat.Type type)
        {
            switch (type)
            {
                case Models.VertexFormat.Type.FP32_1:
                case Models.VertexFormat.Type.Color:
                case Models.VertexFormat.Type.U8_4:
                case Models.VertexFormat.Type.S16_2:
                case Models.VertexFormat.Type.U8_4N:
                case Models.VertexFormat.Type.S16_2N:
                case Models.VertexFormat.Type.U16_2N:
                case Models.VertexFormat.Type.UDec3:
                case Models.VertexFormat.Type.Dec3N:
                case Models.VertexFormat.Type.FP16_2:
                case Models.VertexFormat.Type.S8_4N:
                    return 4;
                case Models.VertexFormat.Type.FP32_2:
                case Models.VertexFormat.Type.S16_4:
                case Models.VertexFormat.Type.S16_4N:
                case Models.VertexFormat.Type.U16_4N:
                case Models.VertexFormat.Type.FP16_4:
                    return 8;
                case Models.VertexFormat.Type.FP32_3:
                    return 12;
                case Models.VertexFormat.Type.FP32_4:
                    return 16;
            }
            return -1;
        }

        private static byte ToBiasedByte(float value)
        {
            return (byte)Math.Max(0, Math.Min(255, Math.Round(value * 127.0f) + 128));
        }

        /// <summary>
        /// Decode one vertex attribute. Public so callers that need to rewrite a single attribute in
        /// place - resizing a mesh touches positions and must leave every other attribute, including
        /// the position W whose meaning is unknown, byte for byte as it was - can use the same codec
        /// the rest of this class reads and writes with.
        /// </summary>
        public static Vector4 ReadVertexData(BinaryReader reader, Models.VertexFormat.Type type)
        {
            switch (type)
            {
                case Models.VertexFormat.Type.FP32_1:
                    return new Vector4(reader.ReadSingle(), 0, 0, 0);
                case Models.VertexFormat.Type.FP32_2:
                    return new Vector4(reader.ReadSingle(), reader.ReadSingle(), 0, 0);
                case Models.VertexFormat.Type.FP32_3:
                    return new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), 0);
                case Models.VertexFormat.Type.FP32_4:
                    return new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                case Models.VertexFormat.Type.Color:
                    //The game declares it DXGI_FORMAT_B8G8R8A8_UNORM: blue, green, red, alpha in memory. Read as red, green, blue, alpha
                    uint data = reader.ReadUInt32();
                    return new Vector4((float)((data & 0x00FF0000) >> 16) / 255.0f, (float)((data & 0x0000FF00) >> 8) / 255.0f, (float)((data & 0x000000FF) >> 0) / 255.0f, (float)((data & 0xFF000000) >> 24) / 255.0f);
                case Models.VertexFormat.Type.U8_4:
                    return new Vector4(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
                case Models.VertexFormat.Type.S16_2:
                    return new Vector4(reader.ReadInt16(), reader.ReadInt16(), 0, 0);
                case Models.VertexFormat.Type.S16_4:
                    return new Vector4(reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16());
                case Models.VertexFormat.Type.U8_4N:
                    return new Vector4((float)reader.ReadByte() / 255.0f, (float)reader.ReadByte() / 255.0f, (float)reader.ReadByte() / 255.0f, (float)reader.ReadByte() / 255.0f);
                case Models.VertexFormat.Type.S16_2N:
                    return new Vector4((float)reader.ReadInt16() / (float)Int16.MaxValue, (float)reader.ReadInt16() / (float)Int16.MaxValue, 0, 0);
                case Models.VertexFormat.Type.S16_4N:
                    return new Vector4((float)reader.ReadInt16() / (float)Int16.MaxValue, (float)reader.ReadInt16() / (float)Int16.MaxValue, (float)reader.ReadInt16() / (float)Int16.MaxValue, (float)reader.ReadInt16() / (float)Int16.MaxValue);
                case Models.VertexFormat.Type.U16_2N:
                    return new Vector4((float)reader.ReadUInt16() / (float)UInt16.MaxValue, (float)reader.ReadUInt16() / (float)UInt16.MaxValue, 0, 0);
                case Models.VertexFormat.Type.U16_4N:
                    return new Vector4((float)reader.ReadUInt16() / (float)UInt16.MaxValue, (float)reader.ReadUInt16() / (float)UInt16.MaxValue, (float)reader.ReadUInt16() / (float)UInt16.MaxValue, (float)reader.ReadUInt16() / (float)UInt16.MaxValue);
                case Models.VertexFormat.Type.Dec3N:
                    //Declared as DEC3N, but the data is three unsigned bytes biased around 128, with the fourth unused.
                    //Decoding it as 10:10:10 gives normals that neither point the right way nor come out unit length.
                    byte dx = reader.ReadByte(), dy = reader.ReadByte(), dz = reader.ReadByte();
                    reader.ReadByte();
                    return new Vector4((dx - 128.0f) / 127.0f, (dy - 128.0f) / 127.0f, (dz - 128.0f) / 127.0f, 0);
            }
            throw new Exception("Unsupported VertexFormatType");
        }

        private static Vector4 GetVertexDataForAttribute(cMesh mesh, Models.VertexFormat.Attribute attr, int vertexIndex, int vertexScale)
        {
            switch (attr.Usage)
            {
                case Models.VertexFormat.Usage.Position:
                    if (vertexIndex < mesh.Vertices.Count)
                    {
                        Vector3 pos = mesh.Vertices[vertexIndex];
#if UNITY_EDITOR || UNITY_STANDALONE
                        return new Vector4(pos.x / vertexScale, pos.y / vertexScale, pos.z / vertexScale, 0);
#else
                        return new Vector4(pos.X / vertexScale, pos.Y / vertexScale, pos.Z / vertexScale, 0);
#endif
                    }
                    return new Vector4(0, 0, 0, 0);
                case Models.VertexFormat.Usage.BlendWeight:
                    if (vertexIndex < mesh.BoneWeights.Count)
                        return mesh.BoneWeights[vertexIndex];
                    return new Vector4(0, 0, 0, 0);
                case Models.VertexFormat.Usage.BlendIndices:
                    if (vertexIndex < mesh.BoneIndexes.Count)
                        return mesh.BoneIndexes[vertexIndex];
                    return new Vector4(0, 0, 0, 0);
                case Models.VertexFormat.Usage.Normal:
                    if (vertexIndex < mesh.Normals.Count)
                    {
                        Vector3 normal = mesh.Normals[vertexIndex];
#if UNITY_EDITOR || UNITY_STANDALONE
                        return new Vector4(normal.x, normal.y, normal.z, 0);
#else
                        return new Vector4(normal.X, normal.Y, normal.Z, 0);
#endif
                    }
                    return new Vector4(0, 0, 0, 0);
                case Models.VertexFormat.Usage.TexCoord:
                    if (attr.Index < mesh.UVs.Length && mesh.UVs[attr.Index] != null && vertexIndex < mesh.UVs[attr.Index].Count)
                    {
                        Vector2 uv = mesh.UVs[attr.Index][vertexIndex];
#if UNITY_EDITOR || UNITY_STANDALONE
                        return new Vector4(uv.x / 16.0f, uv.y / 16.0f, 0, 0);
#else
                        return new Vector4(uv.X / 16.0f, uv.Y / 16.0f, 0, 0);
#endif
                    }
                    return new Vector4(0, 0, 0, 0);
                case Models.VertexFormat.Usage.Tangent:
                    if (vertexIndex < mesh.Tangents.Count)
                        return mesh.Tangents[vertexIndex];
                    return new Vector4(0, 0, 0, 0);
                case Models.VertexFormat.Usage.Binormal:
                    if (vertexIndex < mesh.BiNormals.Count)
                        return mesh.BiNormals[vertexIndex];
                    return new Vector4(0, 0, 0, 0);
                case Models.VertexFormat.Usage.Color:
                    if (vertexIndex < mesh.Colours.Count)
                    {
                        Color c = mesh.Colours[vertexIndex];
#if UNITY_EDITOR || UNITY_STANDALONE
                        return new Vector4(c.r / 255.0f, c.g / 255.0f, c.b / 255.0f, c.a / 255.0f);
#elif GODOT
                        return new Vector4(c.R, c.G, c.B, c.A);
#else
                        return new Vector4(c.R / 255.0f, c.G / 255.0f, c.B / 255.0f, c.A / 255.0f);
#endif
                    }
                    return new Vector4(0, 0, 0, 0);
                default:
                    return new Vector4(0, 0, 0, 0);
            }
        }

        /// <summary>Encode one vertex attribute - the exact inverse of <see cref="ReadVertexData"/>.</summary>
        /* A 0-1 colour channel as a byte, rounded, so a colour read and written again comes back the same */
        private static byte ColourByte(float value)
        {
            return (byte)Math.Max(0, Math.Min(255, Math.Round(value * 255.0f)));
        }

        public static void WriteVertexData(BinaryWriter writer, Vector4 v, Models.VertexFormat.Type type)
        {
            switch (type)
            {
                case Models.VertexFormat.Type.FP32_1:
#if UNITY_EDITOR || UNITY_STANDALONE
                    writer.Write(v.x);
#else
                    writer.Write(v.X);
#endif
                    break;
                case Models.VertexFormat.Type.FP32_2:
#if UNITY_EDITOR || UNITY_STANDALONE
                    writer.Write(v.x);
                    writer.Write(v.y);
#else
                    writer.Write(v.X);
                    writer.Write(v.Y);
#endif
                    break;
                case Models.VertexFormat.Type.FP32_3:
#if UNITY_EDITOR || UNITY_STANDALONE
                    writer.Write(v.x);
                    writer.Write(v.y);
                    writer.Write(v.z);
#else
                    writer.Write(v.X);
                    writer.Write(v.Y);
                    writer.Write(v.Z);
#endif
                    break;
                case Models.VertexFormat.Type.FP32_4:
#if UNITY_EDITOR || UNITY_STANDALONE
                    writer.Write(v.x);
                    writer.Write(v.y);
                    writer.Write(v.z);
                    writer.Write(v.w);
#else
                    writer.Write(v.X);
                    writer.Write(v.Y);
                    writer.Write(v.Z);
                    writer.Write(v.W);
#endif
                    break;
                case Models.VertexFormat.Type.Color:
                    //Red, green, blue, alpha written as the game reads it: blue, green, red, alpha in memory (see ReadVertexData)
#if UNITY_EDITOR || UNITY_STANDALONE
                    uint data = ((uint)ColourByte(v.w) << 24) | ((uint)ColourByte(v.x) << 16) | ((uint)ColourByte(v.y) << 8) | (uint)ColourByte(v.z);
#else
                    uint data = ((uint)ColourByte(v.W) << 24) | ((uint)ColourByte(v.X) << 16) | ((uint)ColourByte(v.Y) << 8) | (uint)ColourByte(v.Z);
#endif
                    writer.Write(data);
                    break;
                case Models.VertexFormat.Type.U8_4:
#if UNITY_EDITOR || UNITY_STANDALONE
                    writer.Write((byte)v.x);
                    writer.Write((byte)v.y);
                    writer.Write((byte)v.z);
                    writer.Write((byte)v.w);
#else
                    writer.Write((byte)v.X);
                    writer.Write((byte)v.Y);
                    writer.Write((byte)v.Z);
                    writer.Write((byte)v.W);
#endif
                    break;
                case Models.VertexFormat.Type.S16_2:
#if UNITY_EDITOR || UNITY_STANDALONE
                    writer.Write((Int16)v.x);
                    writer.Write((Int16)v.y);
#else
                    writer.Write((Int16)v.X);
                    writer.Write((Int16)v.Y);
#endif
                    break;
                case Models.VertexFormat.Type.S16_4:
#if UNITY_EDITOR || UNITY_STANDALONE
                    writer.Write((Int16)v.x);
                    writer.Write((Int16)v.y);
                    writer.Write((Int16)v.z);
                    writer.Write((Int16)v.w);
#else
                    writer.Write((Int16)v.X);
                    writer.Write((Int16)v.Y);
                    writer.Write((Int16)v.Z);
                    writer.Write((Int16)v.W);
#endif
                    break;
                case Models.VertexFormat.Type.U8_4N:
#if UNITY_EDITOR || UNITY_STANDALONE
                    writer.Write((byte)(v.x * 255.0f));
                    writer.Write((byte)(v.y * 255.0f));
                    writer.Write((byte)(v.z * 255.0f));
                    writer.Write((byte)(v.w * 255.0f));
#else
                    writer.Write((byte)(v.X * 255.0f));
                    writer.Write((byte)(v.Y * 255.0f));
                    writer.Write((byte)(v.Z * 255.0f));
                    writer.Write((byte)(v.W * 255.0f));
#endif
                    break;
                case Models.VertexFormat.Type.S16_2N:
#if UNITY_EDITOR || UNITY_STANDALONE
                    writer.Write((Int16)(v.x * Int16.MaxValue));
                    writer.Write((Int16)(v.y * Int16.MaxValue));
#else
                    writer.Write((Int16)(v.X * Int16.MaxValue));
                    writer.Write((Int16)(v.Y * Int16.MaxValue));
#endif
                    break;
                case Models.VertexFormat.Type.S16_4N:
#if UNITY_EDITOR || UNITY_STANDALONE
                    writer.Write((Int16)(v.x * Int16.MaxValue));
                    writer.Write((Int16)(v.y * Int16.MaxValue));
                    writer.Write((Int16)(v.z * Int16.MaxValue));
                    writer.Write((Int16)(v.w * Int16.MaxValue));
#else
                    writer.Write((Int16)(v.X * Int16.MaxValue));
                    writer.Write((Int16)(v.Y * Int16.MaxValue));
                    writer.Write((Int16)(v.Z * Int16.MaxValue));
                    writer.Write((Int16)(v.W * Int16.MaxValue));
#endif
                    break;
                case Models.VertexFormat.Type.U16_2N:
#if UNITY_EDITOR || UNITY_STANDALONE
                    writer.Write((UInt16)(v.x * UInt16.MaxValue));
                    writer.Write((UInt16)(v.y * UInt16.MaxValue));
#else
                    writer.Write((UInt16)(v.X * UInt16.MaxValue));
                    writer.Write((UInt16)(v.Y * UInt16.MaxValue));
#endif
                    break;
                case Models.VertexFormat.Type.U16_4N:
#if UNITY_EDITOR || UNITY_STANDALONE
                    writer.Write((UInt16)(v.x * UInt16.MaxValue));
                    writer.Write((UInt16)(v.y * UInt16.MaxValue));
                    writer.Write((UInt16)(v.z * UInt16.MaxValue));
                    writer.Write((UInt16)(v.w * UInt16.MaxValue));
#else
                    writer.Write((UInt16)(v.X * UInt16.MaxValue));
                    writer.Write((UInt16)(v.Y * UInt16.MaxValue));
                    writer.Write((UInt16)(v.Z * UInt16.MaxValue));
                    writer.Write((UInt16)(v.W * UInt16.MaxValue));
#endif
                    break;
                case Models.VertexFormat.Type.Dec3N:
                    //See ReadVertexData: three bytes biased around 128, with a fourth that's always zero
#if UNITY_EDITOR || UNITY_STANDALONE
                    writer.Write(ToBiasedByte(v.x));
                    writer.Write(ToBiasedByte(v.y));
                    writer.Write(ToBiasedByte(v.z));
#else
                    writer.Write(ToBiasedByte(v.X));
                    writer.Write(ToBiasedByte(v.Y));
                    writer.Write(ToBiasedByte(v.Z));
#endif
                    writer.Write((byte)0);
                    break;
                default:
                    throw new Exception("Unsupported VertexFormatType");
            }
        }
    }
}
