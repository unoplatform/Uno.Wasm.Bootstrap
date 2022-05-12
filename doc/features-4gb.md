### 4GB memory support

The support for 4GB memory space is available by adding the following configuration:
```xml
<ItemGroup>
	<WasmShellExtraEmccFlags Include="-s MAXIMUM_MEMORY=4GB"/>
</ItemGroup>
```
The configuration can also be detected at runtime using the `UNO_BOOTSTRAP_EMSCRIPTEN_MAXIMUM_MEMORY` environment variable, which will be set to `4GB` once set.

