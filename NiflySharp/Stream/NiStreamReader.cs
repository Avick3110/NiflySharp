using System.IO;
using System.Text;

namespace NiflySharp.Stream
{
    public class NiStreamReader
    {
        public BinaryReader Reader { get; }

        public NifFile File { get; }

        public NiStreamReader(System.IO.Stream stream, NifFile file)
        {
            Reader = new BinaryReader(stream, Encoding.UTF8, true);
            File = file;
        }

        public string GetLine(int maxCount)
        {
            var byteArr = new byte[maxCount];

            int i = 0;
            byte b;
            while (i < maxCount && (b = Reader.ReadByte()) != '\n')
            {
                byteArr[i] = b;
                i++;
            }

            return Encoding.Latin1.GetString(byteArr, 0, i).TrimEnd('\0');
        }

        /// <summary>
        /// Bytes left to read, or <see cref="long.MaxValue"/> when the stream cannot report its length.
        /// </summary>
        internal long BytesLeft
        {
            get
            {
                var s = Reader.BaseStream;
                return s.CanSeek ? s.Length - s.Position : long.MaxValue;
            }
        }

        /// <summary>
        /// Throws when <paramref name="count"/> elements of at least <paramref name="minElementSize"/> bytes each
        /// cannot fit in the bytes left, so a corrupt count fails before anything is allocated for it.
        /// </summary>
        internal void CheckCount(long count, int minElementSize, string what)
        {
            if (count <= 0)
                return;

            long left = BytesLeft;
            if (count > left / minElementSize)
                throw new InvalidDataException($"{what} of {count} needs at least {count * minElementSize} bytes, but only {left} are left in the stream.");
        }
    }
}
