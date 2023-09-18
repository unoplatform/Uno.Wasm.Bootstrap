#!/bin/bash
set -xe
emcc main.cpp -s MAIN_MODULE=1 -o hello.html -s WASM=1 -s EXPORT_ALL=1 --pre-js pre.js

buildSideModule(){
	mkdir -p `dirname $2`
	emcc $1 -std=c++17 -s LEGALIZE_JS_FFI=0 -r -o $2 -s WASM=1 -fwasm-exceptions -msimd128 -s USE_LIBPNG=1 -DCUSTOM_VERSION="\"$3\"" $5 -DCUSTOM_FUNCTION_NAME="$4_getCustomVersion"
}

buildSideModule "mysideModule.cpp" "../native/side.bc/1.2/side.bc" "1.2" "side" ""
buildSideModule "mysideModule.cpp" "../native/side.bc/1.2/st,simd/side.bc" "1.2" "side" "-msimd128"

buildSideModule "version_test.cpp" "../native/side2.bc/1.3/side2.bc" "1.3" "side2" ""
buildSideModule "version_test.cpp" "../native/side2.bc/1.2/st,simd/side2.bc" "1.2" "side2" "-msimd128"

buildSideModule "version_test.cpp" "../native/side3.bc/1.3/side3.bc" "1.3" "side3" ""
buildSideModule "version_test.cpp" "../native/side3.bc/1.4/side3.bc" "1.4" "side3" ""
buildSideModule "version_test.cpp" "../native/side3.bc/1.4/st,simd/side3.bc" "1.4" "side3" "-msimd128"

buildSideModule "version_test.cpp" "../native/side4.bc/1.3/side4.bc" "1.3" "side4" ""
buildSideModule "version_test.cpp" "../native/side4.bc/2.0/side4.bc" "2.0" "side4" ""
buildSideModule "version_test.cpp" "../native/side4.bc/3.1/side4.bc" "3.1" "side4" ""
buildSideModule "version_test.cpp" "../native/side4.bc/3.1/st,simd/side4.bc" "3.1" "side4" "-msimd128"
buildSideModule "version_test.cpp" "../native/side4.bc/5.0/side4.bc" "5.0" "side4" ""
