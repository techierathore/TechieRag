/*
 * TechieRag.Embedded (REQ-FN-055 / BRD-90): iOS and Mac Catalyst Debug stub for ONNX Runtime's
 * RegisterCustomOps. Compiled by TechieRag.Embedded.targets at build time; moved here from Sevak
 * (REQ-FN-035), where it was hand-written.
 *
 * Microsoft.ML.OnnxRuntime's managed assembly declares a P/Invoke to RegisterCustomOps, which is
 * defined in NO shipped ONNX Runtime binary (verified with nm against the Catalyst xcframework slice
 * and libonnxruntime.dylib) - it lives in the separate onnxruntime-extensions package.
 *
 * In Release the managed linker trims the unused P/Invoke and the native link succeeds without this
 * file. In Debug the linker is disabled, so EVERY P/Invoke becomes a required native symbol and the
 * link fails. This stub exists so a Debug head can be built.
 *
 * Custom operators are an opt-in ONNX Runtime feature that the embedding models TechieRag ships
 * (bge-m3, all-MiniLM-L6-v2) and the reranker do not use, so this is required to exist but never
 * called. Returning NULL is ONNX Runtime's convention for "OK" (a non-NULL OrtStatus* is a failure).
 *
 * WARNING: an app whose model needs custom operators sets TechieRagDisableOnnxCustomOpsStub=true and
 * links onnxruntime-extensions - otherwise registration silently succeeds while registering nothing.
 */
void *RegisterCustomOps(void *options, const void *api)
{
    (void)options;
    (void)api;
    return 0;
}
