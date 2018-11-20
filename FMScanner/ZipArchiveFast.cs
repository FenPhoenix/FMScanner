//using System;
//using System.Collections.Generic;
//using System.IO;
//using System.IO.Compression;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;

//namespace FMScanner
//{
//    internal enum ZipVersionNeededValues : ushort
//    {
//        Default = 10,
//        ExplicitDirectory = 20,
//        Deflate = 20,
//        Deflate64 = 21,
//        Zip64 = 45
//    }

//    /// <summary>
//    /// The upper byte of the "version made by" flag in the central directory header of a zip file represents the
//    /// OS of the system on which the zip was created. Any zip created with an OS byte not equal to Windows (0)
//    /// or Unix (3) will be treated as equal to the current OS.
//    /// </summary>
//    /// <remarks>
//    /// The value of 0 more specifically corresponds to the FAT file system while NTFS is assigned a higher value. However
//    /// for historical and compatibility reasons, Windows is always assigned a 0 value regardless of file system.
//    /// </remarks>
//    internal enum ZipVersionMadeByPlatform : byte
//    {
//        Windows = 0,
//        Unix = 3
//    }

//    class ZipArchiveFast : ZipArchive
//    {
//        private List<ZipArchiveEntryFast> _entriesFast;

//        public ZipArchiveFast(Stream stream, ZipArchiveMode mode)
//            : base(stream, mode, leaveOpen: false, entryNameEncoding: null)
//        {

//        }
//    }

//    class ZipArchiveEntryFast : ZipArchiveEntry
//    {
//        [Flags]
//        private enum BitFlagValues : ushort { DataDescriptor = 0x8, UnicodeFileName = 0x800 }

//        internal enum CompressionMethodValues : ushort { Stored = 0x0, Deflate = 0x8, Deflate64 = 0x9, BZip2 = 0xC, LZMA = 0xE }

//        private const ushort DefaultVersionToExtract = 10;

//        // The maximum index of our buffers, from the maximum index of a byte array
//        private const int MaxSingleBufferSize = 0x7FFFFFC7;

//        private ZipArchive _archive;
//        private readonly bool _originallyInArchive;
//        private readonly int _diskNumberStart;
//        private readonly ZipVersionMadeByPlatform _versionMadeByPlatform;
//        private ZipVersionNeededValues _versionMadeBySpecification;
//        private ZipVersionNeededValues _versionToExtract;
//        private BitFlagValues _generalPurposeBitFlag;
//        private CompressionMethodValues _storedCompressionMethod;
//        private DateTimeOffset _lastModified;
//        private long _compressedSize;
//        private long _uncompressedSize;
//        private long _offsetOfLocalHeader;
//        private long? _storedOffsetOfCompressedData;
//        private uint _crc32;
//        // An array of buffers, each a maximum of MaxSingleBufferSize in size
//        private byte[][] _compressedBytes;
//        private MemoryStream _storedUncompressedData;
//        private bool _currentlyOpenForWrite;
//        private bool _everOpenedForWrite;
//        private Stream _outstandingWriteStream;
//        private uint _externalFileAttr;
//        private string _storedEntryName;
//        private byte[] _storedEntryNameBytes;
//        // only apply to update mode
//        private List<ZipGenericExtraField> _cdUnknownExtraFields;
//        private List<ZipGenericExtraField> _lhUnknownExtraFields;
//        private byte[] _fileComment;
//        private CompressionLevel? _compressionLevel;

//       // Initializes, attaches it to archive
//        internal ZipArchiveEntry(ZipArchive archive, ZipCentralDirectoryFileHeader cd)
//        {
//            _archive = archive;

//            _originallyInArchive = true;

//            _diskNumberStart = cd.DiskNumberStart;
//            _versionMadeByPlatform = (ZipVersionMadeByPlatform)cd.VersionMadeByCompatibility;
//            _versionMadeBySpecification = (ZipVersionNeededValues)cd.VersionMadeBySpecification;
//            _versionToExtract = (ZipVersionNeededValues)cd.VersionNeededToExtract;
//            _generalPurposeBitFlag = (BitFlagValues)cd.GeneralPurposeBitFlag;
//            CompressionMethod = (CompressionMethodValues)cd.CompressionMethod;
//            _lastModified = new DateTimeOffset(ZipHelper.DosTimeToDateTime(cd.LastModified));
//            _compressedSize = cd.CompressedSize;
//            _uncompressedSize = cd.UncompressedSize;
//            _externalFileAttr = cd.ExternalFileAttributes;
//            _offsetOfLocalHeader = cd.RelativeOffsetOfLocalHeader;
//            // we don't know this yet: should be _offsetOfLocalHeader + 30 + _storedEntryNameBytes.Length + extrafieldlength
//            // but entryname/extra length could be different in LH
//            _storedOffsetOfCompressedData = null;
//            _crc32 = cd.Crc32;

//            _compressedBytes = null;
//            _storedUncompressedData = null;
//            _currentlyOpenForWrite = false;
//            _everOpenedForWrite = false;
//            _outstandingWriteStream = null;

//            FullName = DecodeEntryName(cd.Filename);

//            _lhUnknownExtraFields = null;
//            // the cd should have these as null if we aren't in Update mode
//            _cdUnknownExtraFields = cd.ExtraFields;
//            _fileComment = cd.FileComment;

//            _compressionLevel = null;
//        }

//        // Initializes new entry
//        internal ZipArchiveEntry(ZipArchive archive, string entryName, CompressionLevel compressionLevel)
//            : this(archive, entryName)
//        {
//            _compressionLevel = compressionLevel;
//        }

//        // Initializes new entry
//        internal ZipArchiveEntry(ZipArchive archive, string entryName)
//        {
//            _archive = archive;

//            _originallyInArchive = false;

//            _diskNumberStart = 0;
//            _versionMadeByPlatform = CurrentZipPlatform;
//            _versionMadeBySpecification = ZipVersionNeededValues.Default;
//            _versionToExtract = ZipVersionNeededValues.Default; // this must happen before following two assignment
//            _generalPurposeBitFlag = 0;
//            CompressionMethod = CompressionMethodValues.Deflate;
//            _lastModified = DateTimeOffset.Now;

//            _compressedSize = 0; // we don't know these yet
//            _uncompressedSize = 0;
//            _externalFileAttr = 0;
//            _offsetOfLocalHeader = 0;
//            _storedOffsetOfCompressedData = null;
//            _crc32 = 0;

//            _compressedBytes = null;
//            _storedUncompressedData = null;
//            _currentlyOpenForWrite = false;
//            _everOpenedForWrite = false;
//            _outstandingWriteStream = null;

//            FullName = entryName;

//            _cdUnknownExtraFields = null;
//            _lhUnknownExtraFields = null;
//            _fileComment = null;

//            _compressionLevel = null;

//            if (_storedEntryNameBytes.Length > ushort.MaxValue)
//                throw new ArgumentException(SR.EntryNamesTooLong);

//            // grab the stream if we're in create mode
//            if (_archive.Mode == ZipArchiveMode.Create)
//            {
//                _archive.AcquireArchiveStream(this);
//            }
//        }
//    }
//}
