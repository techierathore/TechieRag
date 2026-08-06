/*
 * REQ-FN-035: Mac Catalyst native stub for ONNX Runtime's RegisterCustomOps.
 *
 * Microsoft.ML.OnnxRuntime 1.27.0 ships an EMPTY placeholder (`_._`) for net9.0-maccatalyst18.0, so
 * it contributes no build integration on Catalyst, while its managed assembly still declares a
 * P/Invoke to RegisterCustomOps. `RegisterCustomOps` is defined in NO shipped ONNX Runtime binary
 * (verified with nm against both the Catalyst xcframework slice and libonnxruntime.dylib) — it lives
 * in the separate onnxruntime-extensions package.
 *
 * In Release the managed linker trims the unused P/Invoke and the native link succeeds without this
 * file. In Debug the linker is disabled, so EVERY P/Invoke becomes a required native symbol and the
 * link fails. This stub exists so the desktop head can be built and debugged.
 *
 * Custom operators are an opt-in ONNX Runtime feature that the bundled BGE-M3 embedding model does
 * not use, so this is required to exist but never called. Returning NULL is ONNX Runtime's
 * convention for "OK" (a non-NULL OrtStatus* signals failure).
 *
 * WARNING: if a future model needs custom operators, replace this with a real
 * onnxruntime-extensions link — otherwise registration silently succeeds while registering nothing.
 */
void *RegisterCustomOps(void *options, const void *api)
{
    (void)options;
    (void)api;
    return 0;
}
