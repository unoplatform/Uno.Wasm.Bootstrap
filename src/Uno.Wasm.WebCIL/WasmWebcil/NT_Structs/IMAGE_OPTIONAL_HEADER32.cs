//MIT License

//Copyright(c) 2023 Stef Heyenrath

//Permission is hereby granted, free of charge, to any person obtaining a copy
//of this software and associated documentation files (the "Software"), to deal
//in the Software without restriction, including without limitation the rights
//to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//copies of the Software, and to permit persons to whom the Software is
//furnished to do so, subject to the following conditions:

//The above copyright notice and this permission notice shall be included in all
//copies or substantial portions of the Software.

//THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
//SOFTWARE.

// Imported from https://github.com/StefH/ProtoBufJsonConverter/tree/dcacaf0ac07fe79c3f91c92746492b5c22b136cf/src-webcil/MetadataReferenceService.BlazorWasm

using System.Runtime.InteropServices;

namespace Uno.Wasm.WebCIL.NT_Structs;

/// <summary>
/// https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-image_optional_header32
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct IMAGE_OPTIONAL_HEADER32
{
    public ushort Magic;
    public byte MajorLinkerVersion;
    public byte MinorLinkerVersion;
    public uint SizeOfCode;
    public uint SizeOfInitializedData;
    public uint SizeOfUninitializedData;

    /// <summary>
    /// A pointer to the entry point function, relative to the image base address.
    /// For executable files, this is the starting address.
    /// For device drivers, this is the address of the initialization function.
    /// The entry point function is optional for DLLs.
    /// When no entry point is present, this member is zero.
    /// </summary>
    public uint AddressOfEntryPoint;

    /// <summary>
    /// A pointer to the beginning of the code section, relative to the image base.
    /// </summary>
    public uint BaseOfCode;

    /// <summary>
    /// A pointer to the beginning of the data section, relative to the image base.
    /// </summary>
    public uint BaseOfData;

    /// <summary>
    /// The preferred address of the first byte of the image when it is loaded in memory.
    /// This value is a multiple of 64K bytes.
    /// The default value for DLLs is 0x10000000.
    /// The default value for applications is 0x00400000, except on Windows CE where it is 0x00010000.
    /// </summary>
    public uint ImageBase;

    public uint SectionAlignment;

    public uint FileAlignment;

    public ushort MajorOperatingSystemVersion;
    public ushort MinorOperatingSystemVersion;
    public ushort MajorImageVersion;
    public ushort MinorImageVersion;
    public ushort MajorSubsystemVersion;
    public ushort MinorSubsystemVersion;
    public uint Win32VersionValue;

    /// <summary>
    /// The size of the image, in bytes, including all headers. Must be a multiple of SectionAlignment.
    /// </summary>
    public uint SizeOfImage;

    /// <summary>
    /// The combined size of the following items, rounded to a multiple of the value specified in the FileAlignment member.
    /// - e_lfanew member of IMAGE_DOS_HEADER
    /// - 4 byte signature
    /// - size of IMAGE_FILE_HEADER
    /// - size of optional header
    /// - size of all section headers
    /// </summary>
    public uint SizeOfHeaders;
    public uint CheckSum;
    public ushort Subsystem;
    public ushort DllCharacteristics;
    public uint SizeOfStackReserve;
    public uint SizeOfStackCommit;
    public uint SizeOfHeapReserve;
    public uint SizeOfHeapCommit;
    public uint LoaderFlags;

    /// <summary>
    /// The number of directory entries in the remainder of the optional header. Each entry describes a location and size.
    /// </summary>
    public uint NumberOfRvaAndSizes;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 0x10)]
    public IMAGE_DATA_DIRECTORY[] DataDirectory;
}
