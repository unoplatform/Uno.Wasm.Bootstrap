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
/// https://www.nirsoft.net/kernel_struct/vista/IMAGE_DOS_HEADER.html
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct IMAGE_DOS_HEADER
{
    public ushort MagicNumber;              // e_magic - Magic number (The value “MZ” are the initials of the PE designer Mark Zbikowski)
    public ushort BytesOnLastPageOfFile;    // e_cblp - Bytes on last page of file
    public ushort PagesInFile;              // e_cp - Pages in file
    public ushort Relocations;              // e_crlc - Relocations
    public ushort SizeOfHeaderInParagraphs; // e_cparhdr - Size of header in paragraphs
    public ushort MinimumExtraParagraphs;   // e_minalloc - Minimum extra paragraphs needed
    public ushort MaximumExtraParagraphs;   // e_maxalloc - Maximum extra paragraphs needed
    public ushort InitialSS;                // e_ss - Initial (relative) SS value
    public ushort InitialSP;                // e_sp - Initial SP value
    public ushort Checksum;                 // e_csum - Checksum
    public ushort InitialIP;                // e_ip - Initial IP value
    public ushort InitialCS;                // e_cs - Initial (relative) CS value
    public ushort AddressOfRelocationTable; // e_lfarlc - File address of relocation table
    public ushort OverlayNumber;            // e_ovno - Overlay number

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public ushort[] ReservedWords1;         // e_res - Reserved words

    public ushort OEMIdentifier;            // e_oemid - OEM identifier (for e_oeminfo)
    public ushort OEMInformation;           // e_oeminfo - OEM information; e_oemid specific

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
    public ushort[] ReservedWords2;         // e_res2 - Reserved words

    public int FileAddressOfNewExeHeader;   // e_lfanew - File address of new exe header
}
