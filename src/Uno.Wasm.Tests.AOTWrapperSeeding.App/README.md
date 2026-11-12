# AOT wrapper seeding test app

A profile-guided AOT app that aborts at startup without AOT wrapper seeding (see "Preventing crashes in profile-guided AOT builds" in `doc/runtime-execution-modes.md`). It's published and run by `src/Uno.Wasm.Tests.AOTWrapperSeeding/test-aot-wrapper-seeding.sh`.

`Program.Main` calls `IService.GetResult` from AOT-compiled code. `IService.GetRecords` has 9 parameters, never runs while profiling, and hashes to the same IMT slot as `GetResult`. Without the seeding, the AOT compiler doesn't emit its `interp_in` wrapper, and the runtime aborts with an `interp.c` assertion when the first call to `GetResult` builds the slot.

`Program.Main` then does the same through `IMerger<string>`, whose 9-parameter `Aggregate` has `T` in its signature. `Merger<string>` only has reference type arguments, so its code is shared, but the runtime enters the interpreter for `Aggregate` with the signature of the instance, and the seeding has to cover it too. Only generic code, `MergerFactory.Create<T>`, creates the `Merger<string>`, so the seeding has to find it there or through the interface instance that the call site names.

`INamingSamples` has signatures whose wrapper names are easy to get wrong: a type with a non-ASCII name, an inline array, `Box<Packed>`, which the runtime re-inflates with the shared type of `Packed` before it maps its fields, value types whose packing raises their alignment, leaves the alignment of a reference alone, rounds up their size, or comes with an explicit size, and function pointers. Its methods never run, but the seeding expects their wrappers, so the build fails with `UNOWASM006` if the names the bootstrapper expects aren't the ones the AOT compiler emits.

`Uno.Wasm.Tests.AOTWrapperSeeding.TwinA` and `TwinB` define types of the same names, `Twins.Value` and `Twins.Service`, which the runtime tells apart. Their `Take` methods need two `interp_in` wrappers of the same name, one per `Value` type, and each `Service` class has an interface method of a signature no other assembly has. The test script checks that the seeding expects all of these wrappers, and the build checks that the AOT compiler emitted them.

`AttributedParameters.Describe` has more than 8 parameters and a struct parameter with a default value, which the runtime can't enter from AOT-compiled code: the test script checks that the build warns about it with `UNOWASM010`.

`aot.profile` was recorded with the bootstrapper's AOT profiler:

1. Delete `aot.profile` (the project only uses it when it exists).
2. Publish with `dotnet publish -c Release -p:WasmShellGenerateAOTProfile=true`, serve the published `wwwroot` folder and open it in a browser.
3. Once the page shows `AOTWrapperSeeding:42:42`, press `Alt+Shift+P` and save the downloaded `aot.profile` next to the project.

If the code of the app changes, record the profile again: it has to list `Program.CallGetResult`, `Service.GetResult`, `Program.CallMergerGetResult` and `Merger<string>.GetResult`, and not `Service.GetRecords` or `Merger<string>.Aggregate`. The IMT slots also depend on the names of the types and methods, and the slot of `Aggregate` on the metadata token of `T`. They were worked out with the runtime of .NET 10 (that of .NET 11 RC2 hashes the same way), so check them again when the app moves to another target framework: the test script's first publish, without the seeding, must still abort.
