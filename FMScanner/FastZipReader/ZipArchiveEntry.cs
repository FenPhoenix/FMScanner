// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Text;
using FastZipReader.DeflateManaged;

namespace FastZipReader
{
    // The disposable fields that this class owns get disposed when the ZipArchive it belongs to gets disposed
    [SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable")]
    internal class ZipArchiveEntry
    {
        private readonly int _diskNumberStart;
        private readonly ZipVersionMadeByPlatform _versionMadeByPlatform;
        private ZipVersionNeededValues _versionMadeBySpecification;
        private ZipVersionNeededValues _versionToExtract;
        private BitFlagValues _generalPurposeBitFlag;
        private CompressionMethodValues _storedCompressionMethod;
        private readonly long _offsetOfLocalHeader;
        private long? _storedOffsetOfCompressedData;
        private uint _externalFileAttr;
        private string _storedEntryFullName;

        // Initializes, attaches it to archive
        internal ZipArchiveEntry(ZipArchive archive, ZipCentralDirectoryFileHeader cd)
        {
            Archive = archive;

            _diskNumberStart = cd.DiskNumberStart;
            _versionMadeByPlatform = (ZipVersionMadeByPlatform)cd.VersionMadeByCompatibility;
            _versionMadeBySpecification = (ZipVersionNeededValues)cd.VersionMadeBySpecification;
            _versionToExtract = (ZipVersionNeededValues)cd.VersionNeededToExtract;
            _generalPurposeBitFlag = (BitFlagValues)cd.GeneralPurposeBitFlag;
            CompressionMethod = (CompressionMethodValues)cd.CompressionMethod;

            // Leave this as a uint and let the caller convert it if it wants (perf optimization)
            LastWriteTime = cd.LastModified;

            CompressedLength = cd.CompressedSize;
            Length = cd.UncompressedSize;
            _externalFileAttr = cd.ExternalFileAttributes;
            _offsetOfLocalHeader = cd.RelativeOffsetOfLocalHeader;

            // we don't know this yet: should be _offsetOfLocalHeader + 30 + _storedEntryNameBytes.Length + extrafieldlength
            // but entryname/extra length could be different in LH
            _storedOffsetOfCompressedData = null;
            Crc32 = cd.Crc32;

            FullName = DecodeEntryName(cd.Filename);
        }

        /// <summary>
        /// The ZipArchive that this entry belongs to. If this entry has been deleted, this will return null.
        /// </summary>
        internal ZipArchive Archive { get; }

        [CLSCompliant(false)]
        internal uint Crc32 { get; }

        /// <summary>
        /// The compressed size of the entry. If the archive that the entry belongs to is in Create mode, attempts to get this property will always throw an exception. If the archive that the entry belongs to is in update mode, this property will only be valid if the entry has not been opened.
        /// </summary>
        /// <exception cref="InvalidOperationException">This property is not available because the entry has been written to or modified.</exception>
        internal long CompressedLength { get; }

        internal int ExternalAttributes
        {
            get => (int)_externalFileAttr;
            set
            {
                ThrowIfInvalidArchive();
                _externalFileAttr = (uint)value;
            }
        }

        /// <summary>
        /// The last write time of the entry as stored in the Zip archive. When setting this property, the DateTime will be converted to the
        /// Zip timestamp format, which supports a resolution of two seconds. If the data in the last write time field is not a valid Zip timestamp,
        /// an indicator value of 1980 January 1 at midnight will be returned.
        /// </summary>
        /// <exception cref="NotSupportedException">An attempt to set this property was made, but the ZipArchive that this entry belongs to was
        /// opened in read-only mode.</exception>
        /// <exception cref="ArgumentOutOfRangeException">An attempt was made to set this property to a value that cannot be represented in the
        /// Zip timestamp format. The earliest date/time that can be represented is 1980 January 1 0:00:00 (midnight), and the last date/time
        /// that can be represented is 2107 December 31 23:59:58 (one second before midnight).</exception>
        internal uint LastWriteTime { get; }

        /// <summary>
        /// The uncompressed size of the entry. This property is not valid in Create mode, and it is only valid in Update mode if the entry has not been opened.
        /// </summary>
        /// <exception cref="InvalidOperationException">This property is not available because the entry has been written to or modified.</exception>
        internal long Length { get; }

        /// <summary>
        /// The filename of the entry. This is equivalent to the substring of Fullname that follows the final directory separator character.
        /// </summary>
        //internal string Name => ParseFileName(FullName, _versionMadeByPlatform);
        internal string Name { get; private set; }

        /// <summary>
        /// The relative path of the entry as stored in the Zip archive. Note that Zip archives allow any string to be the path of the entry, including invalid and absolute paths.
        /// </summary>
        internal string FullName
        {
            get => _storedEntryFullName;

            private set
            {
                if (value == null) throw new ArgumentNullException(nameof(FullName));

                var isUTF8 = EncodeEntryName(value);
                _storedEntryFullName = value;

                if (isUTF8)
                {
                    _generalPurposeBitFlag |= BitFlagValues.UnicodeFileName;
                }
                else
                {
                    _generalPurposeBitFlag &= ~BitFlagValues.UnicodeFileName;
                }

                Name = ParseFileName(value, _versionMadeByPlatform);
                if (Name == "")
                {
                    VersionToExtractAtLeast(ZipVersionNeededValues.ExplicitDirectory);
                }
            }
        }

        /// <summary>
        /// Opens the entry. If the archive that the entry belongs to was opened in Read mode, the returned stream will be readable, and it may or may not be seekable. If Create mode, the returned stream will be writable and not seekable. If Update mode, the returned stream will be readable, writable, seekable, and support SetLength.
        /// </summary>
        /// <returns>A Stream that represents the contents of the entry.</returns>
        /// <exception cref="IOException">The entry is already currently open for writing. -or- The entry has been deleted from the archive. -or- The archive that this entry belongs to was opened in ZipArchiveMode.Create, and this entry has already been written to once.</exception>
        /// <exception cref="InvalidDataException">The entry is missing from the archive or is corrupt and cannot be read. -or- The entry has been compressed using a compression method that is not supported.</exception>
        /// <exception cref="ObjectDisposedException">The ZipArchive that this entry belongs to has been disposed.</exception>
        internal Stream Open()
        {
            ThrowIfInvalidArchive();

            return OpenInReadMode();
        }

        /// <summary>
        /// Returns the FullName of the entry.
        /// </summary>
        /// <returns>FullName of the entry</returns>
        public override string ToString() => FullName;

        // Only allow opening ZipArchives with large ZipArchiveEntries in update mode when running in a 64-bit process.
        // This is for compatibility with old behavior that threw an exception for all process bitnesses, because this
        // will not work in a 32-bit process.

        private long OffsetOfCompressedData
        {
            get
            {
                if (_storedOffsetOfCompressedData == null)
                {
                    Archive.ArchiveStream.Seek(_offsetOfLocalHeader, SeekOrigin.Begin);
                    // by calling this, we are using local header _storedEntryNameBytes.Length and extraFieldLength
                    // to find start of data, but still using central directory size information
                    if (!ZipLocalFileHeader.TrySkipBlock(Archive.ArchiveReader))
                        throw new InvalidDataException(SR.LocalFileHeaderCorrupt);
                    _storedOffsetOfCompressedData = Archive.ArchiveStream.Position;
                }
                return _storedOffsetOfCompressedData.Value;
            }
        }

        private CompressionMethodValues CompressionMethod
        {
            get => _storedCompressionMethod;
            set
            {
                switch (value)
                {
                    case CompressionMethodValues.Deflate:
                        VersionToExtractAtLeast(ZipVersionNeededValues.Deflate);
                        break;
                    case CompressionMethodValues.Deflate64:
                        VersionToExtractAtLeast(ZipVersionNeededValues.Deflate64);
                        break;
                }

                _storedCompressionMethod = value;
            }
        }

        private string DecodeEntryName(byte[] entryNameBytes)
        {
            Debug.Assert(entryNameBytes != null);

            Encoding readEntryNameEncoding;
            if ((_generalPurposeBitFlag & BitFlagValues.UnicodeFileName) == 0)
            {
                #region Original corefx
                //readEntryNameEncoding = _archive == null ?
                //    Encoding.UTF8 :
                //    _archive.EntryNameEncoding ?? Encoding.UTF8;
                #endregion

                // This is what .NET Framework 4.7.2 seems to do (at least I get the same result with this)
                readEntryNameEncoding = Archive == null ?
                    Encoding.UTF8 :
                    Archive.EntryNameEncoding ?? Encoding.Default;
            }
            else
            {
                readEntryNameEncoding = Encoding.UTF8;
            }

            return readEntryNameEncoding.GetString(entryNameBytes);
        }

        private bool EncodeEntryName(string entryName)
        {
            Debug.Assert(entryName != null);

            var writeEntryNameEncoding =
                Archive?.EntryNameEncoding ?? (ZipHelper.RequiresUnicode(entryName)
                    ? Encoding.UTF8
                    : Encoding.ASCII);

            return writeEntryNameEncoding.Equals(Encoding.UTF8);
        }

        internal void ThrowIfNotOpenable()
        {
            if (!IsOpenable(out var message)) throw new InvalidDataException(message);
        }

        private Stream GetDataDecompressor(Stream compressedStreamToRead)
        {
            Stream uncompressedStream;
            switch (CompressionMethod)
            {
                case CompressionMethodValues.Deflate:
                    uncompressedStream = new DeflateStream(compressedStreamToRead, CompressionMode.Decompress);
                    break;
                case CompressionMethodValues.Deflate64:
                    uncompressedStream = new Deflate64ManagedStream(compressedStreamToRead);
                    break;
                case CompressionMethodValues.Stored:
                default:
                    // we can assume that only deflate/deflate64/stored are allowed because we assume that
                    // IsOpenable is checked before this function is called
                    Debug.Assert(CompressionMethod == CompressionMethodValues.Stored);

                    uncompressedStream = compressedStreamToRead;
                    break;
            }

            return uncompressedStream;
        }

        private Stream OpenInReadMode()
        {
            ThrowIfNotOpenable();

            Stream compressedStream = new SubReadStream(Archive.ArchiveStream, OffsetOfCompressedData, CompressedLength);
            return GetDataDecompressor(compressedStream);
        }

        private bool IsOpenable(out string message)
        {
            message = null;

            if (CompressionMethod != CompressionMethodValues.Stored &&
                CompressionMethod != CompressionMethodValues.Deflate &&
                CompressionMethod != CompressionMethodValues.Deflate64)
            {
                switch (CompressionMethod)
                {
                    case CompressionMethodValues.BZip2:
                    case CompressionMethodValues.LZMA:
                        message = SR.Format(SR.UnsupportedCompressionMethod, CompressionMethod.ToString());
                        break;
                    default:
                        message = SR.UnsupportedCompression;
                        break;
                }

                return false;
            }

            if (_diskNumberStart != Archive.NumberOfThisDisk)
            {
                message = SR.SplitSpanned;
                return false;
            }

            if (_offsetOfLocalHeader > Archive.ArchiveStream.Length)
            {
                message = SR.LocalFileHeaderCorrupt;
                return false;
            }

            Archive.ArchiveStream.Seek(_offsetOfLocalHeader, SeekOrigin.Begin);
            if (!ZipLocalFileHeader.TrySkipBlock(Archive.ArchiveReader))
            {
                message = SR.LocalFileHeaderCorrupt;
                return false;
            }

            // when this property gets called, some duplicated work
            if (OffsetOfCompressedData + CompressedLength > Archive.ArchiveStream.Length)
            {
                message = SR.LocalFileHeaderCorrupt;
                return false;
            }

            return true;
        }

        private void VersionToExtractAtLeast(ZipVersionNeededValues value)
        {
            if (_versionToExtract < value) _versionToExtract = value;
            if (_versionMadeBySpecification < value) _versionMadeBySpecification = value;
        }

        private void ThrowIfInvalidArchive()
        {
            if (Archive == null)
                throw new InvalidOperationException(SR.DeletedEntry);
            Archive.ThrowIfDisposed();
        }

        /// <summary>
        /// Gets the file name of the path based on Windows path separator characters
        /// </summary>
        private static string GetFileName_Windows(string path)
        {
            int length = path.Length;
            for (int i = length; --i >= 0;)
            {
                char ch = path[i];
                if (ch == '\\' || ch == '/' || ch == ':')
                    return path.Substring(i + 1);
            }
            return path;
        }

        /// <summary>
        /// Gets the file name of the path based on Unix path separator characters
        /// </summary>
        private static string GetFileName_Unix(string path)
        {
            int length = path.Length;
            for (int i = length; --i >= 0;)
                if (path[i] == '/')
                    return path.Substring(i + 1);
            return path;
        }

        /// <summary>
        /// To get the file name of a ZipArchiveEntry, we should be parsing the FullName based
        /// on the path specifications and requirements of the OS that ZipArchive was created on.
        /// This method takes in a FullName and the platform of the ZipArchiveEntry and returns
        /// the platform-correct file name.
        /// </summary>
        /// <remarks>This method ensures no validation on the paths. Invalid characters are allowed.</remarks>
        internal static string ParseFileName(string path, ZipVersionMadeByPlatform madeByPlatform)
        {
            return madeByPlatform == ZipVersionMadeByPlatform.Windows
                ? GetFileName_Windows(path)
                : GetFileName_Unix(path);
        }

        [Flags]
        private enum BitFlagValues : ushort { DataDescriptor = 0x8, UnicodeFileName = 0x800 }

        internal enum CompressionMethodValues : ushort { Stored = 0x0, Deflate = 0x8, Deflate64 = 0x9, BZip2 = 0xC, LZMA = 0xE }
    }
}
