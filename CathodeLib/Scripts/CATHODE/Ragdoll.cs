using CATHODE.Animations;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CATHODE
{
    /// <summary>
    /// DATA/GLOBAL/ANIMATION.PAK -> SKELE/RAGS (and SKELE/RAGS64)
    ///
    /// A bare Havok packfile - no CATHODE wrapper around it at all. It holds the physics
    /// rig (hkaRagdollInstance and friends) the game drops a character onto when it dies.
    /// </summary>
    public class Ragdoll : CathodeFile
    {
        public static new Implementation Implementation = Implementation.LOAD | Implementation.CREATE | Implementation.SAVE;

        /// <summary>The parsed Havok packfile. Edit it and call <see cref="CathodeFile.Save()"/> to write it back.</summary>
        public HavokPackfile Havok = null;

        public Ragdoll(string path, AnimationStrings strings = null) : base(path)
        {
            _strings = strings;
            _loaded = Load();
        }
        public Ragdoll(MemoryStream stream, AnimationStrings strings = null, string path = "") : base(stream, path)
        {
            _strings = strings;
            _loaded = Load(stream);
        }
        public Ragdoll(byte[] data, AnimationStrings strings = null, string path = "") : base(data, path)
        {
            _strings = strings;
            using (MemoryStream stream = new MemoryStream(data))
            {
                _loaded = Load(stream);
            }
        }

        private AnimationStrings _strings;
        private byte[] _content = new byte[0];

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            _content = stream.ToArray();
            Havok = new HavokPackfile(_content);
            return Havok.Loaded;
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
            return _content.Length == 0 ? null : _content;
        }
        #endregion

        #region ACCESSORS
        /// <summary>
        /// The rigid bodies standing in for the skeleton, the constraints between them, and which
        /// body each bone maps to.
        /// </summary>
        public HavokPackfile.RagdollInstance GetInstance()
        {
            return Havok?.GetRagdoll();
        }

        /// <summary>Names of the rigid bodies making up the rig, in ragdoll order.</summary>
        public List<string> GetBodyNames()
        {
            return GetInstance()?.Bodies ?? new List<string>();
        }
        #endregion
    }
}
