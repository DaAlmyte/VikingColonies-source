using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace VikingColonies.Core
{
    /// <summary>A building's plan as saved with its hut: the plan's text, UTF-8, gzip-compressed (frozen at 1.0).</summary>
    public static class PlanBlob
    {
        public static byte[] Pack(string text)
        {
            byte[] raw = Encoding.UTF8.GetBytes(text ?? "");
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, CompressionMode.Compress)) gzip.Write(raw, 0, raw.Length);
                return output.ToArray();
            }
        }

        /// <summary>At most this many characters unpacked (about 4 million): the biggest real plan is about 32 thousand.</summary>
        public const int MaxUnpacked = 4 * 1024 * 1024;

        /// <summary>False (with why) for data that isn't a packed plan, or would unpack to more than <see cref="MaxUnpacked"/>. Never throws.</summary>
        public static bool TryUnpack(byte[] blob, out string text, out string problem)
        {
            text = "";
            problem = "";
            if (blob == null || blob.Length == 0)
            {
                problem = "no data";
                return false;
            }
            try
            {
                using (var input = new GZipStream(new MemoryStream(blob), CompressionMode.Decompress))
                using (var reader = new StreamReader(input, Encoding.UTF8))
                {
                    // Read in pieces up to a limit: a few kilobytes can unpack to gigabytes (the security sweep of 1.4.1).
                    var whole = new StringBuilder();
                    var buffer = new char[8192];
                    int read;
                    while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (whole.Length + read > MaxUnpacked)
                        {
                            problem = $"too big: over {MaxUnpacked / (1024 * 1024)} million characters unpacked";
                            return false;
                        }
                        whole.Append(buffer, 0, read);
                    }
                    text = whole.ToString();
                }
                return true;
            }
            catch (Exception e)
            {
                problem = e.Message;
                return false;
            }
        }
    }
}
