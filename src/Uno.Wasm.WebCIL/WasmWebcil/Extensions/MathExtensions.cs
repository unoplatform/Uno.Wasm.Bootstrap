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

namespace Uno.Wasm.WebCIL.Extensions;

internal static class MathExtensions
{
    //internal static int RoundToNearestV1(this int number, int nearest = 512)
    //{
    //    var divided = 1.0 * number / nearest;
    //    var rounded = (int)Math.Round(divided, MidpointRounding.AwayFromZero);
    //    return rounded * nearest;
    //}

    //internal static uint RoundToNearestV1(this uint number, uint nearest = 512)
    //{
    //    var divided = 1.0 * number / nearest;
    //    var rounded = (uint)Math.Round(divided, MidpointRounding.AwayFromZero);
    //    return rounded * nearest;
    //}

    internal static int RoundToNearest(this int number, int nearest = 512)
    {
        int remainder = number % nearest;
        int halfNearest = nearest / 2;
        return remainder >= halfNearest ? number + nearest - remainder : number - remainder;
    }

    internal static uint RoundToNearest(this uint number, uint nearest = 512)
    {
        uint remainder = number % nearest;
        uint halfNearest = nearest / 2;
        return remainder >= halfNearest ? number + nearest - remainder : number - remainder;
    }
}
