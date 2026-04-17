// Native compatibility shim for .NET 10+ WASM log profiler.
//
// Background: In .NET 10's Mono WASM, the log profiler's writer/dumper threads
// don't exist (single-threaded mode). Events are enqueued to internal queues
// but never dequeued to the output file. The function proflog_trigger_heapshot()
// is the only code path that drains both queues, but it was made 'static' in
// the .NET runtime and is not callable from external code.
//
// This shim provides the mono_profiler_flush_log() symbol that
// Uno.Wasm.LogProfiler's FlushProfile() calls via P/Invoke. It forwards to
// proflog_trigger_heapshot() which must be made non-static in
// libmono-profiler-log.a (see the PatchMonoProfilerLog build target).
//
// If proflog_trigger_heapshot is not available (original unpatched runtime),
// falls back to proflog_icall_TriggerHeapshot which only sets the heapshot
// flag without draining queues (partial functionality).

// Weak reference: use patched proflog_trigger_heapshot if available,
// otherwise fall back to the exported proflog_icall_TriggerHeapshot.
extern void proflog_icall_TriggerHeapshot(void);
extern void proflog_trigger_heapshot(void) __attribute__((weak));

void mono_profiler_flush_log(void)
{
    if (proflog_trigger_heapshot) {
        // Patched runtime: drains writer + dumper queues → fwrite to VFS
        proflog_trigger_heapshot();
    } else {
        // Unpatched runtime: only sets heapshot flag, queues not drained
        proflog_icall_TriggerHeapshot();
    }
}
