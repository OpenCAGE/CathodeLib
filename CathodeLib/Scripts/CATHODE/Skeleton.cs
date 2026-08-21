using CATHODE.Animations;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace CATHODE
{
    /// <summary>
    /// DATA/GLOBAL/ANIMATION.PAK -> SKELE/SK (and SKELE/SK64)
    ///
    /// A length prefixed Havok packfile holding an hkaSkeleton, then CATHODE's own tables:
    /// bone maps, partial name bone maps, float maps, the IK setup, and who this skeleton
    /// retargets onto. Havok's bone names are stripped in retail data, so the names only exist
    /// as hashes in the bone map.
    /// </summary>
    public class Skeleton : CathodeFile
    {
        public static new Implementation Implementation = Implementation.LOAD | Implementation.CREATE | Implementation.SAVE;

        /// <summary>Skeleton name, as stored inside the Havok data (e.g. "MALE" or "ALIEN").</summary>
        public string Name = "";

        /// <summary>Bones in Havok order - a parent always comes before its children.</summary>
        public List<Bone> Bones = new List<Bone>();

        /// <summary>
        /// The skeletons animation can be retargeted onto from this one, and the SKELE/MAPS entry
        /// holding each mapping. Mirrors the pairs <see cref="SkeletonDB"/> lists globally.
        /// </summary>
        public List<Retarget> Retargets = new List<Retarget>();

        /// <summary>Partial bone names to bone index, for looking a bone up by a name fragment.</summary>
        public List<Mapping> PartialNameBoneMaps = new List<Mapping>();

        /// <summary>Named float slots the animation system can drive on this skeleton.</summary>
        public List<Mapping> FloatMaps = new List<Mapping>();

        /// <summary>Which bones drive foot, look-at, arm and weapon IK, and their limits.</summary>
        public IKData IK = new IKData();

        public Skeleton(string path, AnimationStrings strings) : base(path)
        {
            _strings = strings;
            _loaded = Load();
        }
        public Skeleton(MemoryStream stream, AnimationStrings strings, string path = "") : base(stream, path)
        {
            _strings = strings;
            _loaded = Load(stream);
        }
        public Skeleton(byte[] data, AnimationStrings strings, string path = "") : base(data, path)
        {
            _strings = strings;
            using (MemoryStream stream = new MemoryStream(data))
            {
                _loaded = Load(stream);
            }
        }

        private AnimationStrings _strings;

        /* The Havok packfile is kept verbatim and patched in place on save - rebuilding it would mean
         * regenerating the bind pose animation that sits alongside the skeleton in the same file. */
        private byte[] _havok = new byte[0];
        private HavokPackfile _packfile;
        private uint _poseOffset, _parentsOffset, _bonesOffset;
        private uint _boneStride, _pointerSize;

        /* Every table lists its entries in its own order, kept so a save comes back out byte identical */
        private List<int> _nameSlots = new List<int>();
        private List<int> _partialSlots = new List<int>();
        private List<int> _floatSlots = new List<int>();
        private List<int> _retargetSlots = new List<int>();

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            if (_strings == null)
                return false;

            Bones.Clear();
            Retargets.Clear();
            PartialNameBoneMaps.Clear();
            FloatMaps.Clear();
            _nameSlots.Clear();
            _partialSlots.Clear();
            _floatSlots.Clear();
            _retargetSlots.Clear();

            using (BinaryReader reader = new BinaryReader(stream))
            {
                int havokLength = reader.ReadInt32();
                if (havokLength < 0 || havokLength > reader.BaseStream.Length - 4)
                    return false;
                _havok = reader.ReadBytes(havokLength);

                if (!ReadHavokSkeleton())
                    return false;

                //Bone names, as hashes pointing at the Havok bone index
                _nameSlots = HashTable.Read(reader, (r, n) => r.ReadInt32(), _strings);
                List<string> boneNames = ReadNames(reader, _nameSlots.Count);
                for (int i = 0; i < _nameSlots.Count; i++)
                {
                    if (_nameSlots[i] < 0 || _nameSlots[i] >= Bones.Count) continue;
                    Bones[_nameSlots[i]].Name = boneNames[i];
                }

                //The same shape again, keyed on the partial names animation uses to find a bone
                PartialNameBoneMaps = ReadMapping(reader, _partialSlots);

                //Named float slots the animation system can drive
                FloatMaps = ReadMapping(reader, _floatSlots);

                IK = ReadIK(reader);

                Retargets = HashTable.Read(reader, (r, n) => new Retarget
                {
                    Skeleton = n,
                    MappingFile = _strings.GetString(r.ReadUInt32()),
                }, _strings, _retargetSlots);

                return true;
            }
        }

        /* HashTable.Read hands the name to the item reader, but we need the names alongside the
         * indices, so re-read the lookup pairs we just walked over. */
        private List<string> ReadNames(BinaryReader reader, int count)
        {
            long end = reader.BaseStream.Position;
            reader.BaseStream.Position = end - (count * 4) - (count * 8);
            string[] names = new string[count];
            for (int i = 0; i < count; i++)
            {
                uint hash = reader.ReadUInt32();
                int index = reader.ReadInt32();
                if (index >= 0 && index < count) names[index] = _strings.GetString(hash);
            }
            reader.BaseStream.Position = end;
            return names.ToList();
        }

        private List<Mapping> ReadMapping(BinaryReader reader, List<int> slots)
        {
            return HashTable.Read(reader, (r, n) => new Mapping { Name = n, Value = r.ReadInt32() }, _strings, slots);
        }

        private void WriteMapping(BinaryWriter writer, List<Mapping> mapping, List<int> slots)
        {
            HashTable.Write(writer, mapping, x => x.Name, (w, x) => w.Write(x.Value), _strings, slots);
        }

        private static Vector3 ReadVector3(BinaryReader reader)
        {
            return new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }

        private static void WriteVector3(BinaryWriter writer, Vector3 value)
        {
            writer.Write(value.X);
            writer.Write(value.Y);
            writer.Write(value.Z);
        }

        private IKData ReadIK(BinaryReader reader)
        {
            IKData ik = new IKData();

            ik.HipIndex = reader.ReadInt16();
            ik.HipForwardLocal = ReadVector3(reader);
            ik.HipUpLocal = ReadVector3(reader);
            ik.MaxCosineKneeAngle = reader.ReadSingle();
            ik.MinCosineKneeAngle = reader.ReadSingle();

            ik.LeftLeg = ReadLeg(reader);
            ik.RightLeg = ReadLeg(reader);

            ik.HeadIndex = reader.ReadInt16();
            ik.NeckIndex = reader.ReadInt16();
            ik.LookLeftLimit = reader.ReadSingle();
            ik.LookRightLimit = reader.ReadSingle();
            ik.LookUpLimit = reader.ReadSingle();
            ik.LookDownLimit = reader.ReadSingle();
            ik.HeadForwardLocal = ReadVector3(reader);
            ik.DistanceToEye = ReadVector3(reader);
            ik.NeckUpLocal = ReadVector3(reader);
            ik.NeckForwardLocal = ReadVector3(reader);
            ik.HasEyesRaw = reader.ReadByte();
            ik.LeftEyeIndex = reader.ReadInt16();
            ik.RightEyeIndex = reader.ReadInt16();

            ik.LeftArm = ReadArm(reader);
            ik.RightArm = ReadArm(reader);
            ik.MaxCosineElbowAngle = reader.ReadSingle();
            ik.MinCosineElbowAngle = reader.ReadSingle();

            ik.LeftRoll = ReadRoll(reader);
            ik.RightRoll = ReadRoll(reader);

            ik.LeftWeaponBoneIndex = reader.ReadInt16();
            ik.RightWeaponBoneIndex = reader.ReadInt16();
            return ik;
        }

        private void WriteIK(BinaryWriter writer, IKData ik)
        {
            writer.Write(ik.HipIndex);
            WriteVector3(writer, ik.HipForwardLocal);
            WriteVector3(writer, ik.HipUpLocal);
            writer.Write(ik.MaxCosineKneeAngle);
            writer.Write(ik.MinCosineKneeAngle);

            WriteLeg(writer, ik.LeftLeg);
            WriteLeg(writer, ik.RightLeg);

            writer.Write(ik.HeadIndex);
            writer.Write(ik.NeckIndex);
            writer.Write(ik.LookLeftLimit);
            writer.Write(ik.LookRightLimit);
            writer.Write(ik.LookUpLimit);
            writer.Write(ik.LookDownLimit);
            WriteVector3(writer, ik.HeadForwardLocal);
            WriteVector3(writer, ik.DistanceToEye);
            WriteVector3(writer, ik.NeckUpLocal);
            WriteVector3(writer, ik.NeckForwardLocal);
            writer.Write(ik.HasEyesRaw);
            writer.Write(ik.LeftEyeIndex);
            writer.Write(ik.RightEyeIndex);

            WriteArm(writer, ik.LeftArm);
            WriteArm(writer, ik.RightArm);
            writer.Write(ik.MaxCosineElbowAngle);
            writer.Write(ik.MinCosineElbowAngle);

            WriteRoll(writer, ik.LeftRoll);
            WriteRoll(writer, ik.RightRoll);

            writer.Write(ik.LeftWeaponBoneIndex);
            writer.Write(ik.RightWeaponBoneIndex);
        }

        private static IKData.Leg ReadLeg(BinaryReader reader)
        {
            return new IKData.Leg
            {
                HipIndex = reader.ReadInt16(),
                KneeIndex = reader.ReadInt16(),
                SecondKneeIndex = reader.ReadInt16(),
                AnkleIndex = reader.ReadInt16(),
                ToeIndex = reader.ReadInt16(),
                KneeRotationAxis = ReadVector3(reader),
                IsThreeJointRaw = reader.ReadByte(),
            };
        }

        private static void WriteLeg(BinaryWriter writer, IKData.Leg leg)
        {
            writer.Write(leg.HipIndex);
            writer.Write(leg.KneeIndex);
            writer.Write(leg.SecondKneeIndex);
            writer.Write(leg.AnkleIndex);
            writer.Write(leg.ToeIndex);
            WriteVector3(writer, leg.KneeRotationAxis);
            writer.Write(leg.IsThreeJointRaw);
        }

        private static IKData.Arm ReadArm(BinaryReader reader)
        {
            return new IKData.Arm
            {
                ArmIndex = reader.ReadInt16(),
                ForearmIndex = reader.ReadInt16(),
                HandIndex = reader.ReadInt16(),
                ElbowAxis = ReadVector3(reader),
            };
        }

        private static void WriteArm(BinaryWriter writer, IKData.Arm arm)
        {
            writer.Write(arm.ArmIndex);
            writer.Write(arm.ForearmIndex);
            writer.Write(arm.HandIndex);
            WriteVector3(writer, arm.ElbowAxis);
        }

        private static IKData.Roll ReadRoll(BinaryReader reader)
        {
            return new IKData.Roll
            {
                Axis = ReadVector3(reader),
                MajorIndex = reader.ReadInt16(),
                MinorIndex = reader.ReadInt16(),
                MajorPercent = reader.ReadSingle(),
                MinorPercent = reader.ReadSingle(),
                MajorMax = reader.ReadSingle(),
                MinorMax = reader.ReadSingle(),
                MajorMin = reader.ReadSingle(),
                MinorMin = reader.ReadSingle(),
            };
        }

        private static void WriteRoll(BinaryWriter writer, IKData.Roll roll)
        {
            WriteVector3(writer, roll.Axis);
            writer.Write(roll.MajorIndex);
            writer.Write(roll.MinorIndex);
            writer.Write(roll.MajorPercent);
            writer.Write(roll.MinorPercent);
            writer.Write(roll.MajorMax);
            writer.Write(roll.MinorMax);
            writer.Write(roll.MajorMin);
            writer.Write(roll.MinorMin);
        }

        private bool ReadHavokSkeleton()
        {
            _packfile = new HavokPackfile(_havok);
            if (!_packfile.Loaded) return false;

            HavokPackfile.PackfileObject skeleton = _packfile.Objects.FirstOrDefault(o => o.ClassName == "hkaSkeleton");
            if (skeleton == null) return false;

            /* hkaSkeleton for hk_2012.2.0: hkReferencedObject header, then m_name, then the
             * m_parentIndices / m_bones / m_referencePose arrays. */
            _pointerSize = _packfile.Header.PointerSize;
            uint header = _pointerSize == 8 ? 16u : 8u;
            uint array = _pointerSize + 8;
            _boneStride = _pointerSize == 8 ? 16u : 8u;

            uint nameField = skeleton.DataOffset + header;
            _parentsOffset = nameField + _pointerSize;
            _bonesOffset = _parentsOffset + array;
            _poseOffset = _bonesOffset + array;

            Name = _packfile.TryResolveLocal(nameField, out uint nameAt) ? _packfile.ReadStringAt(nameAt) : "";

            if (!_packfile.TryGetHkArray(_parentsOffset, out uint parents, out int parentCount)) return false;
            if (!_packfile.TryGetHkArray(_bonesOffset, out uint bones, out int boneCount)) return false;
            if (!_packfile.TryGetHkArray(_poseOffset, out uint pose, out int poseCount)) return false;
            if (parentCount != boneCount || poseCount != boneCount) return false;

            byte[] data = _packfile.DataPayload;
            for (int i = 0; i < boneCount; i++)
            {
                int p = (int)pose + (i * 48);
                uint nameSlot = bones + (uint)(i * _boneStride);
                Bones.Add(new Bone
                {
                    Name = _packfile.TryResolveLocal(nameSlot, out uint boneName) ? _packfile.ReadStringAt(boneName) : "",
                    ParentIndex = BitConverter.ToInt16(data, (int)parents + (i * 2)),
                    LockTranslation = data[(int)(nameSlot + _pointerSize)] != 0,
                    Translation = ReadVector4(data, p),
                    Rotation = new Quaternion(BitConverter.ToSingle(data, p + 16), BitConverter.ToSingle(data, p + 20),
                                              BitConverter.ToSingle(data, p + 24), BitConverter.ToSingle(data, p + 28)),
                    Scale = ReadVector4(data, p + 32),
                });
            }

            //Remember where the arrays live in the original file so we can patch them back
            _parentsOffset = _packfile.DataSectionOffset + parents;
            _bonesOffset = _packfile.DataSectionOffset + bones;
            _poseOffset = _packfile.DataSectionOffset + pose;
            return true;
        }

        private static Vector4 ReadVector4(byte[] data, int offset)
        {
            return new Vector4(BitConverter.ToSingle(data, offset), BitConverter.ToSingle(data, offset + 4),
                               BitConverter.ToSingle(data, offset + 8), BitConverter.ToSingle(data, offset + 12));
        }

        override protected bool SaveInternal()
        {
            byte[] content = ToBytes();
            if (content == null) return false;
            File.WriteAllBytes(_filepath, content);
            return true;
        }

        /// <summary>
        /// Serialise back to the format stored in ANIMATION.PAK.
        /// </summary>
        public byte[] ToBytes()
        {
            if (_havok.Length == 0) return null;

            PatchHavok();

            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(_havok.Length);
                writer.Write(_havok);

                WriteNameTable(writer, _nameSlots);
                WriteMapping(writer, PartialNameBoneMaps, _partialSlots);
                WriteMapping(writer, FloatMaps, _floatSlots);
                WriteIK(writer, IK);
                HashTable.Write(writer, Retargets, x => x.Skeleton, (w, x) => w.Write(_strings.GetID(x.MappingFile)), _strings, _retargetSlots);
                return stream.ToArray();
            }
        }

        /* Both trailing tables are "name -> bone index", stored as lookup pairs then the indices.
         * Names come from the bones so a rename carries through; fallback covers indices that
         * don't point at a bone. */
        private void WriteNameTable(BinaryWriter writer, List<int> slots)
        {
            List<string> names = new List<string>(slots.Count);
            for (int i = 0; i < slots.Count; i++)
                names.Add(slots[i] >= 0 && slots[i] < Bones.Count ? Bones[slots[i]].Name : "");

            writer.Write(slots.Count);
            writer.Write(slots.Count);
            HashTable.WriteLookup(writer, names, x => x, _strings);
            for (int i = 0; i < slots.Count; i++)
                writer.Write(slots[i]);
        }

        /* Write the pose and hierarchy back into the Havok bytes. Bone count and names are fixed -
         * changing those would mean rebuilding the packfile and the bind pose animation with it. */
        private void PatchHavok()
        {
            for (int i = 0; i < Bones.Count; i++)
            {
                Bone bone = Bones[i];
                WriteInt16(_parentsOffset + (uint)(i * 2), (short)bone.ParentIndex);
                _havok[_bonesOffset + (i * _boneStride) + _pointerSize] = (byte)(bone.LockTranslation ? 1 : 0);

                uint p = _poseOffset + (uint)(i * 48);
                WriteVector4(p, bone.Translation);
                WriteVector4(p + 16, new Vector4(bone.Rotation.X, bone.Rotation.Y, bone.Rotation.Z, bone.Rotation.W));
                WriteVector4(p + 32, bone.Scale);
            }
        }

        private void WriteInt16(uint offset, short value)
        {
            _havok[offset] = (byte)(value & 0xFF);
            _havok[offset + 1] = (byte)((value >> 8) & 0xFF);
        }

        private void WriteVector4(uint offset, Vector4 value)
        {
            Buffer.BlockCopy(BitConverter.GetBytes(value.X), 0, _havok, (int)offset, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(value.Y), 0, _havok, (int)offset + 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(value.Z), 0, _havok, (int)offset + 8, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(value.W), 0, _havok, (int)offset + 12, 4);
        }
        #endregion

        #region ACCESSORS
        /// <summary>
        /// Havok holds skeletons Z up, while CS2 meshes are Y up. This rotation takes a transform
        /// from the skeleton's own space into the space the mesh vertices live in, i.e. (x, y, z) -> (x, z, -y).
        ///
        /// Measured, not assumed: across the game's character models, the weighted centroid of the
        /// vertices bound to each bone lands within a few centimetres of that bone under this
        /// mapping, and metres away under any other.
        /// </summary>
        public static readonly Matrix4x4 ToMeshSpace = Matrix4x4.CreateRotationX(-(float)(Math.PI / 2.0));

        /// <summary>
        /// Each bone's transform relative to the skeleton root, in the skeleton's own (Z up) space.
        /// </summary>
        public List<Matrix4x4> GetModelSpacePose()
        {
            List<Matrix4x4> pose = new List<Matrix4x4>(Bones.Count);
            for (int i = 0; i < Bones.Count; i++)
            {
                Matrix4x4 local = Bones[i].LocalTransform;
                pose.Add(Bones[i].ParentIndex >= 0 && Bones[i].ParentIndex < i ? local * pose[Bones[i].ParentIndex] : local);
            }
            return pose;
        }

        /// <summary>
        /// Each bone's transform relative to the skeleton root, rotated into mesh space - the bind
        /// pose a model exporter needs so skin weights land on the right place.
        /// </summary>
        public List<Matrix4x4> GetBindPose()
        {
            List<Matrix4x4> pose = GetModelSpacePose();
            for (int i = 0; i < pose.Count; i++)
                pose[i] = pose[i] * ToMeshSpace;
            return pose;
        }

        /// <summary>
        /// A bone's transform relative to its parent, in mesh space. Only the root needs rotating -
        /// everything below it inherits the parent's orientation.
        /// </summary>
        public Matrix4x4 GetLocalBindTransform(int bone)
        {
            if (bone < 0 || bone >= Bones.Count) return Matrix4x4.Identity;
            return Bones[bone].ParentIndex < 0 ? Bones[bone].LocalTransform * ToMeshSpace : Bones[bone].LocalTransform;
        }

        public int IndexOf(string boneName)
        {
            for (int i = 0; i < Bones.Count; i++)
                if (string.Equals(Bones[i].Name, boneName, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        /* Mesh data is in the host engine's vector type in the viewer builds, while the bone maths
           below is all System.Numerics. Convert once at the boundary. */
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
        private static System.Numerics.Vector3 ToNumerics(UnityEngine.Vector3 value)
        {
            return new System.Numerics.Vector3(value.x, value.y, value.z);
        }
        private static System.Numerics.Vector4 ToNumerics(UnityEngine.Vector4 value)
        {
            return new System.Numerics.Vector4(value.x, value.y, value.z, value.w);
        }
#elif GODOT
        private static System.Numerics.Vector3 ToNumerics(Godot.Vector3 value)
        {
            return new System.Numerics.Vector3(value.X, value.Y, value.Z);
        }
        private static System.Numerics.Vector4 ToNumerics(Godot.Vector4 value)
        {
            return new System.Numerics.Vector4(value.X, value.Y, value.Z, value.W);
        }
#else
        private static System.Numerics.Vector3 ToNumerics(System.Numerics.Vector3 value) { return value; }
        private static System.Numerics.Vector4 ToNumerics(System.Numerics.Vector4 value) { return value; }
#endif
        /// <summary>
        /// The number of bones a skeleton needs before it can drive this model, i.e. one past the
        /// highest bone index any of its submeshes reference.
        /// </summary>
        public static int RequiredBoneCount(Models.CS2 model)
        {
            int highest = -1;
            if (model == null) return 0;
            foreach (Models.CS2.Component component in model.Components)
                foreach (Models.CS2.Component.LOD lod in component.LODs)
                    foreach (Models.CS2.Component.LOD.Submesh submesh in lod.Submeshes)
                        foreach (int bone in submesh.Bones)
                            if (bone > highest) highest = bone;
            return highest + 1;
        }

        /// <summary>
        /// How well this skeleton fits a model, as the mean distance in metres between each bone and
        /// the centre of the vertices weighted to it. Lower is better; the right skeleton for a
        /// character comes out a few centimetres out and the wrong one lands tens of centimetres away.
        /// Returns -1 if the model isn't skinned or this skeleton is too small for it.
        /// </summary>
        public float ScoreFit(Models.CS2 model)
        {
            Dictionary<int, Vector3> weighted = new Dictionary<int, Vector3>();
            Dictionary<int, float> totals = new Dictionary<int, float>();
            if (model == null || Bones.Count < RequiredBoneCount(model)) return -1;

            foreach (Models.CS2.Component component in model.Components)
                foreach (Models.CS2.Component.LOD lod in component.LODs)
                    foreach (Models.CS2.Component.LOD.Submesh submesh in lod.Submeshes)
                    {
                        if (submesh.Bones.Count == 0) continue;

                        cMesh mesh = ModelUtility.ToMesh(submesh);
                        int count = Math.Min(mesh.Vertices.Count, Math.Min(mesh.BoneIndexes.Count, mesh.BoneWeights.Count));
                        for (int v = 0; v < count; v++)
                        {
                            Vector4 indices = ToNumerics(mesh.BoneIndexes[v]), weights = ToNumerics(mesh.BoneWeights[v]);
                            for (int slot = 0; slot < 4; slot++)
                            {
                                float weight = slot == 0 ? weights.X : slot == 1 ? weights.Y : slot == 2 ? weights.Z : weights.W;
                                if (weight <= 0.001f) continue;

                                float raw = slot == 0 ? indices.X : slot == 1 ? indices.Y : slot == 2 ? indices.Z : indices.W;
                                int local = (int)Math.Round(raw);
                                if (local < 0 || local >= submesh.Bones.Count) continue;

                                int bone = submesh.Bones[local];
                                Vector3 vertex = ToNumerics(mesh.Vertices[v]);
                                weighted[bone] = (weighted.TryGetValue(bone, out Vector3 sum) ? sum : Vector3.Zero) + vertex * weight;
                                totals[bone] = (totals.TryGetValue(bone, out float t) ? t : 0) + weight;
                            }
                        }
                    }

            if (weighted.Count == 0) return -1;

            List<Matrix4x4> pose = GetBindPose();
            double total = 0;
            foreach (KeyValuePair<int, Vector3> entry in weighted)
            {
                if (entry.Key < 0 || entry.Key >= pose.Count) return -1;
                total += (entry.Value / totals[entry.Key] - pose[entry.Key].Translation).Length();
            }
            return (float)(total / weighted.Count);
        }
        #endregion

        #region STRUCTURES
        public class Bone
        {
            /// <summary>Bone name from CATHODE's hash table - Havok's own copy is stripped in retail data.</summary>
            public string Name = "";

            /// <summary>Index into <see cref="Bones"/>, or -1 for a root.</summary>
            public int ParentIndex = -1;

            public bool LockTranslation = false;

            /* Stored as hkVector4 - the W lanes are padding Havok never initialises, so they're kept
             * verbatim rather than reconstructed, to keep saves byte identical. */
            public Vector4 Translation = new Vector4(0, 0, 0, 0);
            public Quaternion Rotation = Quaternion.Identity;
            public Vector4 Scale = new Vector4(1, 1, 1, 0);

            public Vector3 Position
            {
                get { return new Vector3(Translation.X, Translation.Y, Translation.Z); }
                set { Translation = new Vector4(value, Translation.W); }
            }

            public Vector3 ScaleXYZ
            {
                get { return new Vector3(Scale.X, Scale.Y, Scale.Z); }
                set { Scale = new Vector4(value, Scale.W); }
            }

            /// <summary>Bone transform relative to its parent.</summary>
            public Matrix4x4 LocalTransform =>
                Matrix4x4.CreateScale(ScaleXYZ) * Matrix4x4.CreateFromQuaternion(Rotation) * Matrix4x4.CreateTranslation(Position);

            public override string ToString() => Name;
        }

        /// <summary>
        /// A skeleton this one's animation can be retargeted onto.
        /// </summary>
        public class Retarget
        {
            /// <summary>Name of the skeleton being mapped to.</summary>
            public string Skeleton = "";

            /// <summary>Name whose hash is the SKELE/MAPS entry holding the mapping.</summary>
            public string MappingFile = "";

            public override string ToString() => Skeleton;
        }

        /// <summary>
        /// A named slot on the skeleton pointing at a bone index (or, for float maps, a float slot).
        /// </summary>
        public class Mapping
        {
            public string Name = "";
            public int Value;

            public override string ToString() => Name + " = " + Value;
        }

        /// <summary>
        /// Which bones the runtime drives procedurally, and the limits it has to respect.
        /// Indices are into <see cref="Bones"/>; -1 means the rig doesn't have that joint.
        /// </summary>
        public class IKData
        {
            public short HipIndex;
            public Vector3 HipForwardLocal;
            public Vector3 HipUpLocal;
            public float MaxCosineKneeAngle;
            public float MinCosineKneeAngle;

            public Leg LeftLeg = new Leg();
            public Leg RightLeg = new Leg();

            public short HeadIndex;
            public short NeckIndex;
            public float LookLeftLimit;
            public float LookRightLimit;
            public float LookUpLimit;
            public float LookDownLimit;
            public Vector3 HeadForwardLocal;
            public Vector3 DistanceToEye;
            public Vector3 NeckUpLocal;
            public Vector3 NeckForwardLocal;
            /* Stored as a byte, and the game leaves junk in it when the flag is meaningless, so
             * the raw value is kept and the flag reads it the way the engine does. */
            public byte HasEyesRaw;
            public bool HasEyes
            {
                get { return HasEyesRaw != 0; }
                set { HasEyesRaw = (byte)(value ? 1 : 0); }
            }

            public short LeftEyeIndex;
            public short RightEyeIndex;

            public Arm LeftArm = new Arm();
            public Arm RightArm = new Arm();
            public float MaxCosineElbowAngle;
            public float MinCosineElbowAngle;

            /// <summary>Forearm twist distribution for the left arm.</summary>
            public Roll LeftRoll = new Roll();

            /// <summary>Forearm twist distribution for the right arm.</summary>
            public Roll RightRoll = new Roll();

            /// <summary>Bones a weapon attaches to.</summary>
            public short LeftWeaponBoneIndex;
            public short RightWeaponBoneIndex;

            public class Leg
            {
                public short HipIndex;
                public short KneeIndex;
                public short SecondKneeIndex;
                public short AnkleIndex;
                public short ToeIndex;
                public Vector3 KneeRotationAxis;

                /* As with HasEyes, kept raw because the game doesn't always initialise it */
                public byte IsThreeJointRaw;

                /// <summary>Set for digitigrade legs, which bend at a second knee.</summary>
                public bool IsThreeJoint
                {
                    get { return IsThreeJointRaw != 0; }
                    set { IsThreeJointRaw = (byte)(value ? 1 : 0); }
                }
            }

            public class Arm
            {
                public short ArmIndex;
                public short ForearmIndex;
                public short HandIndex;
                public Vector3 ElbowAxis;
            }

            public class Roll
            {
                public Vector3 Axis;
                public short MajorIndex;
                public short MinorIndex;
                public float MajorPercent;
                public float MinorPercent;
                public float MajorMax;
                public float MinorMax;
                public float MajorMin;
                public float MinorMin;
            }
        }
        #endregion
    }
}

