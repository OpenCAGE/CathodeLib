using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using static CathodeLib.CompositeFlowgraphTable.FlowgraphMeta;

namespace CathodeLib
{
    /* Page data newer than the table's own format (comment boxes, and whatever else is added later) is written after the last
       page as an extension block of tagged, length-prefixed chunks. The table's version byte is left alone: a reader that
       predates the block stops after the pages, so it still loads every page and simply ignores (and on save, drops) the
       rest. A table with nothing to put in the block is written exactly as before. */
    public partial class CompositeFlowgraphTable
    {
        private const uint ExtensionMagic = 0x5846434F; //"OCFX"
        private const byte ExtensionVersion = 1;

        /// <summary>Chunk tag of the pages' comment boxes ("CMNT").</summary>
        public const uint ChunkComments = 0x544E4D43;

        //Other partial files add chunks of their own: tag -> payload (none when there is nothing to write), and reading
        //one back (handled = true when the tag is theirs). The reader is left at the chunk's end whatever they read.
        partial void WriteExtensionChunks(List<KeyValuePair<uint, byte[]>> chunks);
        partial void ReadExtensionChunk(uint tag, BinaryReader reader, long end, ref bool handled);

        private void WriteExtensions(BinaryWriter writer)
        {
            List<KeyValuePair<uint, byte[]>> chunks = new List<KeyValuePair<uint, byte[]>>();
            byte[] comments = WriteComments();
            if (comments != null)
                chunks.Add(new KeyValuePair<uint, byte[]>(ChunkComments, comments));
            WriteExtensionChunks(chunks);
            chunks.RemoveAll(o => o.Value == null);
            if (chunks.Count == 0)
                return;

            writer.Write(ExtensionMagic);
            writer.Write(ExtensionVersion);
            writer.Write(chunks.Count);
            foreach (KeyValuePair<uint, byte[]> chunk in chunks)
            {
                writer.Write(chunk.Key);
                writer.Write(chunk.Value.Length);
                writer.Write(chunk.Value);
            }
        }

        private void ReadExtensions(BinaryReader reader)
        {
            Stream stream = reader.BaseStream;
            if (!stream.CanSeek || stream.Length - stream.Position < 9)
                return;
            long start = stream.Position;
            if (reader.ReadUInt32() != ExtensionMagic)
            {
                stream.Position = start;
                return;
            }

            try
            {
                reader.ReadByte(); //block version: chunks are skipped by length, so a newer block still reads the chunks known here
                int chunkCount = reader.ReadInt32();
                for (int i = 0; i < chunkCount; i++)
                {
                    uint tag = reader.ReadUInt32();
                    int length = reader.ReadInt32();
                    long end = stream.Position + length;
                    if (length < 0 || end > stream.Length)
                        break;
                    bool handled = false;
                    if (tag == ChunkComments)
                    {
                        ReadComments(reader, end);
                        handled = true;
                    }
                    if (!handled)
                        ReadExtensionChunk(tag, reader, end, ref handled);
                    stream.Position = end;
                }
            }
            catch (EndOfStreamException)
            {
                //A cut-short block loses what was not read; the pages are already in
            }
        }

        private byte[] WriteComments()
        {
            int pages = 0;
            for (int i = 0; i < flowgraphs.Count; i++)
                if (flowgraphs[i].Comments != null && flowgraphs[i].Comments.Count != 0)
                    pages++;
            if (pages == 0)
                return null;

            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(pages);
                for (int i = 0; i < flowgraphs.Count; i++)
                {
                    List<CommentMeta> comments = flowgraphs[i].Comments;
                    if (comments == null || comments.Count == 0)
                        continue;
                    writer.Write(i);
                    writer.Write(comments.Count);
                    foreach (CommentMeta comment in comments)
                    {
                        //Each record says how long it is, so fields added later are skipped by a reader that does not know them
                        using (MemoryStream record = new MemoryStream())
                        using (BinaryWriter recordWriter = new BinaryWriter(record))
                        {
                            recordWriter.Write(comment.Bounds.X);
                            recordWriter.Write(comment.Bounds.Y);
                            recordWriter.Write(comment.Bounds.Width);
                            recordWriter.Write(comment.Bounds.Height);
                            recordWriter.Write(comment.Title ?? "");
                            recordWriter.Write(comment.Colour.IsEmpty ? 0 : comment.Colour.ToArgb());
                            recordWriter.Flush();
                            writer.Write((int)record.Length);
                            writer.Write(record.ToArray());
                        }
                    }
                }
                writer.Flush();
                return stream.ToArray();
            }
        }

        private void ReadComments(BinaryReader reader, long end)
        {
            int pages = reader.ReadInt32();
            for (int p = 0; p < pages && reader.BaseStream.Position < end; p++)
            {
                int pageIndex = reader.ReadInt32();
                int count = reader.ReadInt32();
                List<CommentMeta> comments = pageIndex >= 0 && pageIndex < flowgraphs.Count ? flowgraphs[pageIndex].Comments : null;
                for (int c = 0; c < count; c++)
                {
                    int length = reader.ReadInt32();
                    long recordEnd = reader.BaseStream.Position + length;
                    if (length < 0 || recordEnd > end)
                        return;
                    CommentMeta comment = new CommentMeta();
                    comment.Bounds = new Rectangle(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
                    comment.Title = reader.ReadString();
                    int argb = reader.ReadInt32();
                    comment.Colour = argb == 0 ? Color.Empty : Color.FromArgb(argb);
                    reader.BaseStream.Position = recordEnd;
                    comments?.Add(comment);
                }
            }
        }

        public partial class FlowgraphMeta
        {
            /// <summary>
            /// The page's comment boxes, drawn behind its nodes. Only how the page is presented: not part of what the page
            /// is (see Equals), and never needed to match it to its composite.
            /// </summary>
            public List<CommentMeta> Comments = new List<CommentMeta>();

            //Other partial files deep-copy what they add to a page
            partial void CloneExtensions(FlowgraphMeta copy);

            /// <summary>Give a page built field by field (PageRewriter.Copy) this page's comment boxes and other extension data.</summary>
            internal void CopyExtrasTo(FlowgraphMeta copy) => CloneComments(copy);

            private void CloneComments(FlowgraphMeta copy)
            {
                copy.Comments = new List<CommentMeta>(Comments?.Count ?? 0);
                if (Comments != null)
                {
                    foreach (CommentMeta comment in Comments)
                        if (comment != null)
                            copy.Comments.Add(comment.Clone());
                }
                CloneExtensions(copy);
            }

            /// <summary>A titled box on a page, in canvas coordinates.</summary>
            public class CommentMeta
            {
                public Rectangle Bounds;
                public string Title = "";
                /// <summary>The box's colour, or Color.Empty for the editor's default.</summary>
                public Color Colour = Color.Empty;

                public CommentMeta Clone() => (CommentMeta)MemberwiseClone();
            }
        }
    }
}
