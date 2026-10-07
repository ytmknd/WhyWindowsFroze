using System.Runtime.InteropServices;

namespace WhyWindowsFroze
{
    internal static class Native
    {
        // ---- Prefetch 展開 (Xpress Huffman) ----
        public const ushort COMPRESSION_FORMAT_XPRESS_HUFF = 4;

        [DllImport("ntdll.dll")]
        public static extern uint RtlGetCompressionWorkSpaceSize(ushort format, out uint bufferWorkSpaceSize, out uint fragmentWorkSpaceSize);

        [DllImport("ntdll.dll")]
        public static extern uint RtlDecompressBufferEx(ushort format, byte[] uncompressed, int uncompressedSize,
            byte[] compressed, int compressedSize, out int finalUncompressedSize, byte[] workSpace);
    }
}
