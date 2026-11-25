using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace YouTubeMp3
{
    // Writes the ID3 Comments field (a COMM frame), which Apple Music shows as Comments and Smart Playlists can match.
    // FFmpeg can't do this: it stores "comment" as a custom TXXX frame that Apple Music ignores.
    internal static class Id3Comment
    {
        private const int Padding = 512;

        // Replaces any comment in the file's ID3v2 tag with this text; empty text removes it. Returns false, leaving
        // the file as it was, when the tag uses a layout this doesn't rewrite (unsynchronisation, an extended header).
        internal static bool Set(string file, string text)
        {
            byte[] data = File.ReadAllBytes(file);
            byte[] updated = Rewrite(data, text);
            if (updated == null) return false;
            string temp = file + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp";
            try
            {
                File.WriteAllBytes(temp, updated);
                File.Copy(temp, file, true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            return true;
        }

        internal static byte[] Rewrite(byte[] data, string text)
        {
            text = (text ?? "").Trim();
            int version = 3, audio = 0;
            var frames = new List<byte[]>();
            if (data.Length >= 10 && data[0] == 'I' && data[1] == 'D' && data[2] == '3')
            {
                version = data[3];
                byte flags = data[5];
                if (version < 3 || version > 4 || (flags & 0x80) != 0 || (flags & 0x40) != 0) return null;
                int size = SyncSafe(data, 6);
                audio = 10 + size + ((flags & 0x10) != 0 ? 10 : 0);
                if (audio > data.Length) return null;
                int pos = 10, end = 10 + size;
                while (pos + 10 <= end && data[pos] != 0)
                {
                    string id = Encoding.ASCII.GetString(data, pos, 4);
                    int length = version == 4 ? SyncSafe(data, pos + 4) : BigEndian(data, pos + 4);
                    if (length < 0 || pos + 10 + length > end) return null;
                    if (!IsComment(id, data, pos + 10, length))
                    {
                        var frame = new byte[10 + length];
                        Buffer.BlockCopy(data, pos, frame, 0, frame.Length);
                        frames.Add(frame);
                    }
                    pos += 10 + length;
                }
            }
            if (text.Length > 0) frames.Add(CommentFrame(text, version));

            int body = Padding;
            foreach (var frame in frames) body += frame.Length;
            var output = new MemoryStream(10 + body + data.Length - audio);
            output.Write(new byte[] { (byte)'I', (byte)'D', (byte)'3', (byte)version, 0, 0 }, 0, 6);
            output.Write(ToSyncSafe(body), 0, 4);
            foreach (var frame in frames) output.Write(frame, 0, frame.Length);
            output.Write(new byte[Padding], 0, Padding);
            output.Write(data, audio, data.Length - audio);
            return output.ToArray();
        }

        // COMM frames, and the TXXX "comment" frames FFmpeg and yt-dlp leave behind (often the video's link).
        private static bool IsComment(string id, byte[] data, int start, int length)
        {
            if (id == "COMM") return true;
            if (id != "TXXX" || length < 2) return false;
            string description = Decode(data[start], data, start + 1, length - 1);
            int stop = description.IndexOf('\0');
            if (stop >= 0) description = description.Substring(0, stop);
            return String.Equals(description.Trim(), "comment", StringComparison.OrdinalIgnoreCase);
        }

        private static byte[] CommentFrame(string text, int version)
        {
            var body = new MemoryStream();
            if (version == 4)
            {
                body.WriteByte(3);   // UTF-8
                body.Write(Encoding.ASCII.GetBytes("eng"), 0, 3);
                body.WriteByte(0);   // empty description
                var bytes = Encoding.UTF8.GetBytes(text);
                body.Write(bytes, 0, bytes.Length);
            }
            else
            {
                body.WriteByte(1);   // UTF-16 with a byte order mark, which iTunes and Apple Music read
                body.Write(Encoding.ASCII.GetBytes("eng"), 0, 3);
                body.Write(new byte[] { 0xFF, 0xFE, 0, 0 }, 0, 4);   // empty description
                body.Write(new byte[] { 0xFF, 0xFE }, 0, 2);
                var bytes = Encoding.Unicode.GetBytes(text);
                body.Write(bytes, 0, bytes.Length);
            }
            byte[] content = body.ToArray();
            var frame = new byte[10 + content.Length];
            Encoding.ASCII.GetBytes("COMM").CopyTo(frame, 0);
            byte[] size = version == 4 ? ToSyncSafe(content.Length) : new[] { (byte)(content.Length >> 24), (byte)(content.Length >> 16),
                (byte)(content.Length >> 8), (byte)content.Length };
            size.CopyTo(frame, 4);
            content.CopyTo(frame, 10);
            return frame;
        }

        private static string Decode(byte encoding, byte[] data, int start, int length)
        {
            switch (encoding)
            {
                case 1: return Encoding.Unicode.GetString(data, start, length).TrimStart('﻿', '￾');
                case 2: return Encoding.BigEndianUnicode.GetString(data, start, length);
                case 3: return Encoding.UTF8.GetString(data, start, length);
                default: return Encoding.GetEncoding(28591).GetString(data, start, length);
            }
        }

        private static int SyncSafe(byte[] b, int i) { return (b[i] & 0x7F) << 21 | (b[i + 1] & 0x7F) << 14 | (b[i + 2] & 0x7F) << 7 | (b[i + 3] & 0x7F); }
        private static int BigEndian(byte[] b, int i) { return b[i] << 24 | b[i + 1] << 16 | b[i + 2] << 8 | b[i + 3]; }
        private static byte[] ToSyncSafe(int n) { return new[] { (byte)((n >> 21) & 0x7F), (byte)((n >> 14) & 0x7F), (byte)((n >> 7) & 0x7F), (byte)(n & 0x7F) }; }
    }
}
