namespace Mono.Debugger.Soft
{
    // The upstream library only uses this DTO from Microsoft.FileFormats.
    // Keeping it locally avoids pulling an obsolete development-only feed.
    public sealed class PdbChecksum
    {
        public PdbChecksum(string algorithmName, byte[] checksum)
        {
            AlgorithmName = algorithmName;
            Checksum = checksum;
        }

        public string AlgorithmName { get; }
        public byte[] Checksum { get; }
    }
}
