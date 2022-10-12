@echo off

set UNO_EMSDK_VERSION=%2
set UNO_INTERMEDIATE_PATH=%1

echo "UNO_INTERMEDIATE_PATH: %UNO_INTERMEDIATE_PATH%"
echo "UNO_EMSDK_VERSION: %UNO_EMSDK_VERSION%"

set UNO_EMSDK_PATH=%UNO_INTERMEDIATE_PATH%\emsdk-%UNO_EMSDK_VERSION%
set UNO_EMSDK_PATH_MARKER="%UNO_EMSDK_PATH%\.uno-install-done"

echo "UNO_EMSDK_PATH: %UNO_EMSDK_PATH%"
echo "UNO_EMSDK_PATH_MARKER: %UNO_EMSDK_PATH_MARKER%"

IF NOT EXIST "%UNO_EMSDK_PATH%" (
	mkdir "%UNO_EMSDK_PATH%"
)

pushd "%UNO_EMSDK_PATH%"

python --version

IF ERRORLEVEL 1 ( 
	echo Python is not installed. You can install it from the store https://www.microsoft.com/store/productId/9P7QFQMJRFP7  1>&2
	EXIT /B 1
)

IF NOT EXIST "%UNO_EMSDK_PATH_MARKER%" (
<<<<<<< HEAD
	python --version

	IF ERRORLEVEL 1 ( 
		echo Python is not installed. You can install it from the store https://www.microsoft.com/store/productId/9P7QFQMJRFP7  1>&2
	) ELSE (
		echo "Installing emscripten %UNO_EMSDK_VERSION% in %UNO_EMSDK_PATH%"

		git clone --branch %UNO_EMSDK_VERSION% https://github.com/emscripten-core/emsdk 2>&1
		cd emsdk
		emsdk install %UNO_EMSDK_VERSION% 2>&1
		emsdk activate %UNO_EMSDK_VERSION% 2>&1

		echo "Writing %UNO_EMSDK_PATH_MARKER%"
		echo "installed" > "%UNO_EMSDK_PATH_MARKER%"
	)
=======
	echo "Installing emscripten %UNO_EMSDK_VERSION% in %UNO_EMSDK_PATH%"

	git clone --branch %UNO_EMSDK_VERSION% https://github.com/emscripten-core/emsdk 2>&1
	cd emsdk
	emsdk install %UNO_EMSDK_VERSION% 2>&1
	emsdk activate %UNO_EMSDK_VERSION% 2>&1

	echo "Writing %UNO_EMSDK_PATH_MARKER%"
	echo "installed" > "%UNO_EMSDK_PATH_MARKER%"
>>>>>>> 703e5e2 (fix: Adjust python validation on windows)
) ELSE (
	echo "Skipping installed emscripten %UNO_EMSDK_VERSION% in %UNO_EMSDK_PATH%"
)
