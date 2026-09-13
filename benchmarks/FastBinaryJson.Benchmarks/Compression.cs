using System;
using System.IO;
using System.IO.Compression;

namespace FastBinaryJson.Benchmarks
{
    /// <summary>
    /// Compressed-size measurement only. These are deterministic one-shot calls used by the size
    /// report - they are deliberately not on any timed benchmark path, because per-blob stream
    /// construction costs far more than the codec itself and would measure plumbing, not format.
    /// </summary>
    public static class Compression
    {
        public static int GzipSize(byte[] payload)
        {
            using (MemoryStream output = new MemoryStream())
            {
                using (GZipStream gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
                {
                    gzip.Write(payload, 0, payload.Length);
                }

                return (int)output.Length;
            }
        }

        public static int BrotliSize(byte[] payload)
        {
            byte[] buffer = new byte[BrotliEncoder.GetMaxCompressedLength(payload.Length)];
            if (!BrotliEncoder.TryCompress(payload, buffer, out int written, quality: 11, window: 22))
            {
                throw new InvalidOperationException("Brotli compression did not fit the maximum-length buffer.");
            }

            return written;
        }
    }
}
