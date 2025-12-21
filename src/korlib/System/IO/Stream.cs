// ProtonOS korlib - Stream
// Minimal stub for System.IO.Stream to support reflection APIs.

namespace System.IO
{
    /// <summary>
    /// Provides a generic view of a sequence of bytes. This is an abstract class.
    /// </summary>
    public abstract class Stream : IDisposable
    {
        /// <summary>When overridden in a derived class, gets a value indicating whether the current stream supports reading.</summary>
        public abstract bool CanRead { get; }

        /// <summary>When overridden in a derived class, gets a value indicating whether the current stream supports seeking.</summary>
        public abstract bool CanSeek { get; }

        /// <summary>When overridden in a derived class, gets a value indicating whether the current stream supports writing.</summary>
        public abstract bool CanWrite { get; }

        /// <summary>When overridden in a derived class, gets the length in bytes of the stream.</summary>
        public abstract long Length { get; }

        /// <summary>When overridden in a derived class, gets or sets the position within the current stream.</summary>
        public abstract long Position { get; set; }

        /// <summary>When overridden in a derived class, clears all buffers for this stream and causes any buffered data to be written to the underlying device.</summary>
        public abstract void Flush();

        /// <summary>When overridden in a derived class, reads a sequence of bytes from the current stream.</summary>
        public abstract int Read(byte[] buffer, int offset, int count);

        /// <summary>When overridden in a derived class, sets the position within the current stream.</summary>
        public abstract long Seek(long offset, SeekOrigin origin);

        /// <summary>When overridden in a derived class, sets the length of the current stream.</summary>
        public abstract void SetLength(long value);

        /// <summary>When overridden in a derived class, writes a sequence of bytes to the current stream.</summary>
        public abstract void Write(byte[] buffer, int offset, int count);

        /// <summary>Releases all resources used by the Stream.</summary>
        public void Dispose()
        {
            Dispose(true);
        }

        /// <summary>Releases the unmanaged resources used by the Stream and optionally releases the managed resources.</summary>
        protected virtual void Dispose(bool disposing) { }

        /// <summary>Reads a byte from the stream and advances the position within the stream by one byte.</summary>
        public virtual int ReadByte()
        {
            byte[] buffer = new byte[1];
            int result = Read(buffer, 0, 1);
            return result == 0 ? -1 : buffer[0];
        }

        /// <summary>Writes a byte to the current position in the stream and advances the position within the stream by one byte.</summary>
        public virtual void WriteByte(byte value)
        {
            byte[] buffer = new byte[1] { value };
            Write(buffer, 0, 1);
        }

        /// <summary>Copies bytes from the current stream to the destination stream.</summary>
        public virtual void CopyTo(Stream destination)
        {
            CopyTo(destination, 81920);
        }

        /// <summary>Copies bytes from the current stream to the destination stream, using a specified buffer size.</summary>
        public virtual void CopyTo(Stream destination, int bufferSize)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (bufferSize <= 0) throw new ArgumentOutOfRangeException(nameof(bufferSize));

            byte[] buffer = new byte[bufferSize];
            int read;
            while ((read = Read(buffer, 0, buffer.Length)) > 0)
            {
                destination.Write(buffer, 0, read);
            }
        }

        /// <summary>A Stream with no backing store.</summary>
        public static readonly Stream Null = new NullStream();

        private sealed class NullStream : Stream
        {
            public override bool CanRead => true;
            public override bool CanSeek => true;
            public override bool CanWrite => true;
            public override long Length => 0;
            public override long Position { get => 0; set { } }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => 0;
            public override long Seek(long offset, SeekOrigin origin) => 0;
            public override void SetLength(long value) { }
            public override void Write(byte[] buffer, int offset, int count) { }
        }
    }

    /// <summary>
    /// Specifies the position in a stream to use for seeking.
    /// </summary>
    public enum SeekOrigin
    {
        /// <summary>Specifies the beginning of a stream.</summary>
        Begin = 0,
        /// <summary>Specifies the current position within a stream.</summary>
        Current = 1,
        /// <summary>Specifies the end of a stream.</summary>
        End = 2
    }
}
