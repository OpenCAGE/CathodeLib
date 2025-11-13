using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CATHODE
{
    public class TexturePtr
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
    };
}
