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
/// https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-image_file_header
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct IMAGE_FILE_HEADER
{
    /// <summary>
    /// The architecture type of the computer.
    /// An image file can only be run on the specified computer or a system that emulates the specified computer.
    /// </summary>
    public ushort Machine;

    /// <summary>
    /// The number of sections.
    /// This indicates the size of the section table, which immediately follows the headers.
    /// Note that the Windows loader limits the number of sections to 96.
    /// </summary>
    public ushort NumberOfSections;

    /// <summary>
    /// The low 32 bits of the time stamp of the image.
    /// This represents the date and time the image was created by the linker.
    /// The value is represented in the number of seconds elapsed since midnight (00:00:00), January 1, 1970, Universal Coordinated Time, according to the system clock.
    /// </summary>
    public uint TimeDateStamp;

    public uint PointerToSymbolTable;

    public uint NumberOfSymbols;

    public ushort SizeOfOptionalHeader;

    public ushort Characteristics;
}
