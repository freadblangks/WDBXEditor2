using System;

namespace DBFileReaderLib
{
    [Flags]
    public enum DB2Flags
    {
        None = 0x0,
        Sparse = 0x1, //'Has offset map'
        SecondaryKey = 0x2, //'Has relationship data'ᵘ // This may be 'secondary keys' and is unrelated to WDC1+ relationships
        Index = 0x4, //'Has non-inline IDs'
        Unknown1 = 0x8, // modern client explicitly throws an exception
        BitPacked = 0x10 //Is bitpacked'ᵘ // WDC1+
    }
}
