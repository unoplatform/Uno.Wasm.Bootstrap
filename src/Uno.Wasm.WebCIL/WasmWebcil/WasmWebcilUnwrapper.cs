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

using Uno.Wasm.WebCIL.Helpers;

namespace Uno.Wasm.WebCIL;

internal class WasmWebcilUnwrapper : IDisposable
{
    private readonly Stream _wasmStream;

    public WasmWebcilUnwrapper(Stream wasmStream)
    {
        _wasmStream = wasmStream;
    }

    public void WriteUnwrapped(Stream outputStream)
    {
        ValidateWasmPrefix();

        using var reader = new BinaryReader(_wasmStream, System.Text.Encoding.UTF8, leaveOpen: true);
        var bytes = ReadDataSection(reader);
        outputStream.Write(bytes);
    }

    private void ValidateWasmPrefix()
    {
        // Create a byte array matching the length of the prefix.
        var prefix = WebcilWasmWrapperHelper.GetPrefix();
        var buffer = new byte[prefix.Length];
        int bytesRead = _wasmStream.Read(buffer, 0, buffer.Length);
        if (bytesRead < buffer.Length)
        {
            throw new InvalidOperationException("Unable to read Wasm prefix.");
        }

        // Compare the read prefix with the expected one.
        if (!buffer.SequenceEqual(prefix))
        {
            throw new InvalidOperationException("Invalid Wasm prefix.");
        }
    }

    private static void SkipSection(BinaryReader reader)
    {
        var size = ULEB128Decode(reader);
        reader.BaseStream.Seek(size, SeekOrigin.Current);
    }

    private static byte[] ReadDataSection(BinaryReader reader)
    {
        // Skip until we find the data section, which contains the Webcil payload.
        byte[] buffer = new byte[1];
        while (true)
        {
            // Read the Data section
            var dataRead = reader.Read(buffer, 0, 1);
            if (dataRead == 0)
            {
                throw new InvalidOperationException("Unable to read Data Section.");
            }

            // Check the Data section (ID = 11)
            if (buffer[0] == 11)
            {
                break;
            }

            // Skip other sections by reading and ignoring their content.
            SkipSection(reader);
        }

        // Read and ignore the size of the data section.
        ULEB128Decode(reader);

        // Read the number of segments.
        int segmentsCount = (int)ULEB128Decode(reader);
        int lastSegment = segmentsCount - 1;
        for (int segmentIndex = 0; segmentIndex < segmentsCount; segmentIndex++)
        {
            // Ignore segmentType (1 = passive segment)
            var segmentType = reader.Read(buffer, 0, 1);
            if (segmentType != 1)
            {
                throw new InvalidOperationException($"Unexpected segment code for segment {segmentIndex}.");
            }

            // Read the segment size.
            var segmentSize = ULEB128Decode(reader);

            // The actual Webcil payload is expected to be in the last segment.
            if (segmentIndex == lastSegment)
            {
                return reader.ReadBytes((int)segmentSize);
            }

            // Skip other segments.
            reader.BaseStream.Seek(segmentSize, SeekOrigin.Current);
        }

        throw new Exception("Unable to read DataSection.");
    }

    /// <summary>
    /// Decodes a variable-length quantity (VLQ) encoded as unsigned LEB128.
    /// LEB128 (Little Endian Base 128) is used to encode integers in a variable number of bytes.
    /// The method reads bytes from the provided binary reader and decodes them into an unsigned integer.
    /// </summary>
    /// <param name="reader">The binary reader from which to read the ULEB128 encoded data.</param>
    /// <returns>The decoded unsigned integer from the ULEB128 encoded data.</returns>
    private static uint ULEB128Decode(BinaryReader reader)
    {
        uint result = 0;
        int shift = 0;
        byte byteValue;

        do
        {
            byteValue = reader.ReadByte();
            uint byteAsUInt = byteValue & 0x7Fu;
            result |= byteAsUInt << shift;
            shift += 7;
        } while ((byteValue & 0x80) != 0);

        return result;
    }

    public void Dispose()
    {
        _wasmStream.Dispose();
    }
}
