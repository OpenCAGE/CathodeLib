using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CATHODE
{
    public class TexturePtr : IEquatable<TexturePtr>
    {
        public Textures.TEX4 Texture = null;
        public Source Location = Source.NONE;

        public enum Source
        {
            GLOBAL = 2, //Texture comes from ENV/GLOBAL
            LEVEL = 0,  //Texture comes from the level
            NONE = -1,  //Texture is located nowhere
        }

        public TexturePtr() { }

        public TexturePtr(BinaryReader reader, Textures texturesGlobal, Textures texturesLevel)
        {
            int index = reader.ReadInt16();
            int source = reader.ReadInt16();
            if (source == -1)
            {
                Texture = null;
                return;
            }
            Location = (Source)source;
            Texture = Location == Source.LEVEL ? texturesLevel.GetAtWriteIndex(index) : texturesGlobal.GetAtWriteIndex(index);
        }

        public void Write(BinaryWriter writer, Textures texturesGlobal, Textures texturesLevel)
        {
            writer.Write((Int16)(Location == Source.LEVEL ? texturesLevel.GetWriteIndex(Texture) : texturesGlobal.GetWriteIndex(Texture)));
            writer.Write((Int16)Location);
        }

        public void RemapToLevel(Level level)
        {
            if (Location != TexturePtr.Source.GLOBAL || Texture == null)
                return;

            Textures.TEX4 globalTexture = Texture;

            globalTexture.UsageFlags &= ~Textures.TextureUsageFlag.IS_GLOBAL_PACK;
            globalTexture.UsageFlags |= Textures.TextureUsageFlag.IS_LEVEL_PACK;

            Texture = level.Textures.ImportEntry(globalTexture);
            Location = Source.LEVEL;

            globalTexture.UsageFlags |= Textures.TextureUsageFlag.IS_GLOBAL_PACK;
            globalTexture.UsageFlags &= ~Textures.TextureUsageFlag.IS_LEVEL_PACK;
        }

        public static bool operator ==(TexturePtr x, TexturePtr y)
        {
            if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
            if (ReferenceEquals(y, null)) return ReferenceEquals(x, null);
            if (x.Texture != y.Texture) return false;
            if (x.Location != y.Location) return false;
            return true;
        }

        public static bool operator !=(TexturePtr x, TexturePtr y)
        {
            return !(x == y);
        }

        public bool Equals(TexturePtr other)
        {
            return this == other;
        }

        public override bool Equals(object obj)
        {
            return obj is TexturePtr ptr && this == ptr;
        }

        public override int GetHashCode()
        {
            int hashCode = -1234567890;
            hashCode = hashCode * -1521134295 + (Texture?.GetHashCode() ?? 0);
            hashCode = hashCode * -1521134295 + Location.GetHashCode();
            return hashCode;
        }
    };
}
