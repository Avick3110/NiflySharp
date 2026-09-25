using System;
using System.IO;

namespace NiflySharp
{
    /// <summary>
    /// Tells apart the kinds of <see cref="InvalidDataException"/> that <see cref="NifFile.Load(System.IO.Stream, NifFileLoadOptions)"/> throws.
    /// </summary>
    public static class NifLoadErrors
    {
        /// <summary>
        /// The <see cref="Exception.Data"/> key set when a block disagrees with its stored size in the header: a count
        /// inside it needs more bytes than the stored size has left, or reading it went past the stored size. Either
        /// the size table or the block's data is wrong. Without the key, a count or size did not fit in the file.
        /// </summary>
        public const string BlockSizeMismatchKey = "NiflySharp.BlockSizeMismatch";

        /// <summary>
        /// Whether <paramref name="ex"/> says a block disagrees with its stored size.
        /// </summary>
        public static bool IsBlockSizeMismatch(Exception ex)
        {
            return ex is InvalidDataException && ex.Data.Contains(BlockSizeMismatchKey);
        }

        internal static InvalidDataException BlockSizeMismatch(string message, Exception innerException = null)
        {
            var ex = new InvalidDataException(message, innerException);
            ex.Data[BlockSizeMismatchKey] = true;
            return ex;
        }
    }
}
