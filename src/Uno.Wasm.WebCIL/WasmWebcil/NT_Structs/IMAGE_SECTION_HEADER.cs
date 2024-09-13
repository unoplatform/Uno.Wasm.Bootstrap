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
/// https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-image_section_header
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct IMAGE_SECTION_HEADER
{
    /// <summary>
    /// An 8-byte, null-padded UTF-8 string.
    /// There is no terminating null character if the string is exactly eight characters long.
    /// For longer names, this member contains a forward slash (/) followed by an ASCII representation of a decimal number that is an offset into the string table.
    /// Executable images do not use a string table and do not support section names longer than eight characters.
    /// </summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    public byte[] Name;

    public UnionType Misc;

    /// <summary>
    /// The address of the first byte of the section when loaded into memory, relative to the image base.
    /// For object files, this is the address of the first byte before relocation is applied.
    /// </summary>
    public uint VirtualAddress;

    /// <summary>
    /// The size of the initialized data on disk, in bytes.
    /// This value must be a multiple of the FileAlignment member of the IMAGE_OPTIONAL_HEADER structure.
    /// If this value is less than the VirtualSize member, the remainder of the section is filled with zeroes.
    /// If the section contains only uninitialized data, the member is zero.
    /// </summary>
    public uint SizeOfRawData;

    /// <summary>
    /// A file pointer to the first page within the COFF file.
    /// This value must be a multiple of the FileAlignment member of the IMAGE_OPTIONAL_HEADER structure.
    /// If a section contains only uninitialized data, set this member is zero.
    /// </summary>
    public uint PointerToRawData;

    public uint PointerToRelocations;

    public uint PointerToLinenumbers;

    public ushort NumberOfRelocations;

    public ushort NumberOfLinenumbers;

    public uint Characteristics;

    [StructLayout(LayoutKind.Explicit)]
    public struct UnionType
    {
        [FieldOffset(0)]
        public uint PhysicalAddress;

        /// <summary>
        /// The total size of the section when loaded into memory, in bytes. If this value is greater than the SizeOfRawData member, the section is filled with zeroes.
        /// This field is valid only for executable images and should be set to 0 for object files.
        /// </summary>
        [FieldOffset(0)]
        public uint VirtualSize;
    }
}
