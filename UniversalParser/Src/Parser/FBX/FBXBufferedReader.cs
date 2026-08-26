using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace UniversalParser.Src.Parser.FBX
{
    /// <summary>
    /// FBX 定位读取器。
    ///
    /// 使用 RandomAccess 保证不改变 FileStream.Position，
    /// 但通过大块缓存把大量小型随机读取合并成少量顺序读取。
    ///
    /// 该类只用于单次 ParseAsync 调用链，不保证多线程安全。
    /// </summary>
    internal sealed class FBXBufferedReader : IDisposable
    {
        private readonly SafeFileHandle _handle;
        private readonly long _fileLength;

        private byte[] _buffer;
        private long _bufferStart = -1;
        private int _bufferLength;

        private bool _disposed;

        public FBXBufferedReader(
            SafeFileHandle handle,
            long fileLength,
            int bufferSize = 1024 * 1024)
        {
            ArgumentNullException.ThrowIfNull(handle);

            _handle = handle;
            _fileLength = fileLength;

            bufferSize = Math.Clamp(
                bufferSize,
                64 * 1024,
                16 * 1024 * 1024);

            _buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        }

        public int BufferSize => _buffer.Length;

        public async ValueTask<int> ReadAtAsync(
            long offset,
            Memory<byte> destination,
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(
                _disposed,
                this);

            if (destination.IsEmpty)
                return 0;

            if (offset < 0 || offset >= _fileLength)
                return 0;

            int readable = (int)Math.Min(
                destination.Length,
                _fileLength - offset);

            if (readable <= 0)
                return 0;

            if (readable > _buffer.Length)
            {
                return await ReadDirectAsync(
                    offset,
                    destination[..readable],
                    cancellationToken);
            }

            bool cacheHit =
                _bufferStart >= 0 &&
                offset >= _bufferStart &&
                offset + readable <= _bufferStart + _bufferLength;

            if (!cacheHit)
            {
                await FillAsync(
                    offset,
                    cancellationToken);
            }

            long cacheOffset = offset - _bufferStart;

            if (cacheOffset < 0 ||
                cacheOffset >= _bufferLength)
            {
                return 0;
            }

            int available = Math.Min(
                readable,
                _bufferLength - (int)cacheOffset);

            _buffer.AsMemory(
                    (int)cacheOffset,
                    available)
                .CopyTo(destination);

            return available;
        }

        public async ValueTask<bool> ReadExactlyAtAsync(
            long offset,
            Memory<byte> destination,
            CancellationToken cancellationToken = default)
        {
            int read = await ReadAtAsync(
                offset,
                destination,
                cancellationToken);

            return read == destination.Length;
        }

        private async ValueTask FillAsync(
            long offset,
            CancellationToken cancellationToken)
        {
            _bufferStart = offset;
            _bufferLength = 0;

            int wanted = (int)Math.Min(
                _buffer.Length,
                _fileLength - offset);

            while (_bufferLength < wanted)
            {
                int read = await RandomAccess.ReadAsync(
                    _handle,
                    _buffer.AsMemory(
                        _bufferLength,
                        wanted - _bufferLength),
                    offset + _bufferLength,
                    cancellationToken);

                if (read <= 0)
                    break;

                _bufferLength += read;
            }
        }

        private async ValueTask<int> ReadDirectAsync(
            long offset,
            Memory<byte> destination,
            CancellationToken cancellationToken)
        {
            int total = 0;

            while (total < destination.Length)
            {
                int read = await RandomAccess.ReadAsync(
                    _handle,
                    destination[total..],
                    offset + total,
                    cancellationToken);

                if (read <= 0)
                    break;

                total += read;
            }

            return total;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            byte[] buffer = _buffer;
            _buffer = Array.Empty<byte>();

            if (buffer.Length > 0)
                ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}