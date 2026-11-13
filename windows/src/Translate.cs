// Offline translation for Windows: Opus-MT (MarianMT) models run on ONNX Runtime, entirely on this PC.
// onnxruntime.dll comes with the translation pack and is driven through its C API at run time,
// so LiteCap.exe still builds with the plain .NET Framework compiler and stays a single file.
// Pure logic only (no UI), so it can also be tested outside Windows.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace LiteCap
{
    // ------------------------------------------------------------------
    // Native library loading (LoadLibrary on Windows, dlopen elsewhere for tests)
    // ------------------------------------------------------------------
    static class NativeLib
    {
        public static bool IsWindows { get { return Environment.OSVersion.Platform == PlatformID.Win32NT; } }

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr LoadLibraryExW(string path, IntPtr reserved, uint flags);
        [DllImport("kernel32", CharSet = CharSet.Ansi, BestFitMapping = false)]
        static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("libdl.so.2", EntryPoint = "dlopen")]
        static extern IntPtr DlOpen(string path, int flags);
        [DllImport("libdl.so.2", EntryPoint = "dlsym")]
        static extern IntPtr DlSym(IntPtr handle, string name);

        public static IntPtr Load(string path)
        {
            IntPtr h;
            if (IsWindows)
            {
                h = LoadLibraryExW(path, IntPtr.Zero, 0x8);   // LOAD_WITH_ALTERED_SEARCH_PATH: dependencies next to the dll
                if (h == IntPtr.Zero) throw new TranslateException("Cannot load " + Path.GetFileName(path) + " (error " + Marshal.GetLastWin32Error() + ")");
            }
            else
            {
                h = DlOpen(path, 2);   // RTLD_NOW
                if (h == IntPtr.Zero) throw new TranslateException("Cannot load " + path);
            }
            return h;
        }

        public static IntPtr Symbol(IntPtr module, string name)
        {
            IntPtr p = IsWindows ? GetProcAddress(module, name) : DlSym(module, name);
            if (p == IntPtr.Zero) throw new TranslateException("Missing symbol " + name);
            return p;
        }
    }

    class TranslateException : Exception
    {
        public TranslateException(string message) : base(message) { }
    }

    // ------------------------------------------------------------------
    // Minimal ONNX Runtime C API binding (only what Marian inference needs)
    // ------------------------------------------------------------------
    sealed class Ort
    {
        const uint ApiVersion = 16;   // functions are only ever appended, so newer runtimes serve this table too

        // Indices into the OrtApi function table (onnxruntime_c_api.h).
        const int I_GetErrorMessage = 2, I_CreateEnv = 3, I_CreateSession = 7, I_Run = 9, I_CreateSessionOptions = 10,
            I_SetGraphOptimizationLevel = 23, I_SetIntraOpNumThreads = 24, I_SessionGetInputCount = 30, I_SessionGetOutputCount = 31,
            I_SessionGetInputName = 36, I_SessionGetOutputName = 37, I_CreateTensorWithDataAsOrtValue = 49, I_GetTensorMutableData = 51,
            I_GetDimensionsCount = 61, I_GetDimensions = 62, I_GetTensorTypeAndShape = 65, I_CreateCpuMemoryInfo = 69,
            I_AllocatorFree = 76, I_GetAllocatorWithDefaultOptions = 78, I_ReleaseEnv = 92, I_ReleaseStatus = 93,
            I_ReleaseMemoryInfo = 94, I_ReleaseSession = 95, I_ReleaseValue = 96, I_ReleaseTensorTypeAndShapeInfo = 99,
            I_ReleaseSessionOptions = 100;

        public const int TypeFloat = 1, TypeInt64 = 7, TypeBool = 9;

        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate IntPtr GetApiFn(uint version);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate IntPtr GetErrorMessageFn(IntPtr status);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] public delegate void ReleaseFn(IntPtr handle);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate IntPtr CreateEnvFn(int level, [MarshalAs(UnmanagedType.LPStr)] string logId, out IntPtr env);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate IntPtr CreateSessionOptionsFn(out IntPtr options);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate IntPtr SetIntFn(IntPtr options, int value);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate IntPtr CreateSessionFn(IntPtr env, IntPtr path, IntPtr options, out IntPtr session);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate IntPtr CreateCpuMemoryInfoFn(int allocator, int memType, out IntPtr info);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate IntPtr CreateTensorFn(IntPtr info, IntPtr data, UIntPtr bytes, long[] shape, UIntPtr shapeLen, int type, out IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate IntPtr GetPtrFn(IntPtr handle, out IntPtr result);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate IntPtr GetCountFn(IntPtr handle, out UIntPtr count);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate IntPtr GetDimensionsFn(IntPtr info, [Out] long[] dims, UIntPtr len);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate IntPtr GetNameFn(IntPtr session, UIntPtr index, IntPtr allocator, out IntPtr name);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate IntPtr GetAllocatorFn(out IntPtr allocator);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate IntPtr AllocatorFreeFn(IntPtr allocator, IntPtr p);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        delegate IntPtr RunFn(IntPtr session, IntPtr runOptions, IntPtr[] inNames, IntPtr[] inValues, UIntPtr inCount,
                              IntPtr[] outNames, UIntPtr outCount, [In, Out] IntPtr[] outValues);

        GetErrorMessageFn getErrorMessage;
        ReleaseFn releaseEnv, releaseStatus, releaseMemoryInfo, releaseSessionOptions, releaseTypeInfo;
        public ReleaseFn ReleaseSession, ReleaseValue;
        CreateEnvFn createEnv;
        CreateSessionOptionsFn createSessionOptions;
        SetIntFn setGraphOptimizationLevel, setIntraOpNumThreads;
        CreateSessionFn createSession;
        CreateCpuMemoryInfoFn createCpuMemoryInfo;
        CreateTensorFn createTensor;
        GetPtrFn getTensorMutableData, getTensorTypeAndShape;
        GetCountFn sessionGetInputCount, sessionGetOutputCount, getDimensionsCount;
        GetDimensionsFn getDimensions;
        GetNameFn sessionGetInputName, sessionGetOutputName;
        GetAllocatorFn getAllocator;
        AllocatorFreeFn allocatorFree;
        RunFn run;

        IntPtr env, memInfo, allocator;

        static T Fn<T>(IntPtr api, int index) where T : class
        {
            IntPtr p = Marshal.ReadIntPtr(api, index * IntPtr.Size);
            if (p == IntPtr.Zero) throw new TranslateException("ONNX Runtime function " + index + " missing");
            return (T)(object)Marshal.GetDelegateForFunctionPointer(p, typeof(T));
        }

        public static Ort Load(string libraryPath)
        {
            IntPtr module = NativeLib.Load(libraryPath);
            IntPtr baseApi = NativeLib.Symbol(module, "OrtGetApiBase");
            GetApiBaseFn getBase = (GetApiBaseFn)Marshal.GetDelegateForFunctionPointer(baseApi, typeof(GetApiBaseFn));
            IntPtr apiBase = getBase();
            GetApiFn getApi = (GetApiFn)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(apiBase, 0), typeof(GetApiFn));
            IntPtr api = getApi(ApiVersion);
            if (api == IntPtr.Zero) throw new TranslateException("ONNX Runtime is too old");

            Ort o = new Ort();
            o.getErrorMessage = Fn<GetErrorMessageFn>(api, I_GetErrorMessage);
            o.releaseEnv = Fn<ReleaseFn>(api, I_ReleaseEnv);
            o.releaseStatus = Fn<ReleaseFn>(api, I_ReleaseStatus);
            o.releaseMemoryInfo = Fn<ReleaseFn>(api, I_ReleaseMemoryInfo);
            o.releaseSessionOptions = Fn<ReleaseFn>(api, I_ReleaseSessionOptions);
            o.releaseTypeInfo = Fn<ReleaseFn>(api, I_ReleaseTensorTypeAndShapeInfo);
            o.ReleaseSession = Fn<ReleaseFn>(api, I_ReleaseSession);
            o.ReleaseValue = Fn<ReleaseFn>(api, I_ReleaseValue);
            o.createEnv = Fn<CreateEnvFn>(api, I_CreateEnv);
            o.createSessionOptions = Fn<CreateSessionOptionsFn>(api, I_CreateSessionOptions);
            o.setGraphOptimizationLevel = Fn<SetIntFn>(api, I_SetGraphOptimizationLevel);
            o.setIntraOpNumThreads = Fn<SetIntFn>(api, I_SetIntraOpNumThreads);
            o.createSession = Fn<CreateSessionFn>(api, I_CreateSession);
            o.createCpuMemoryInfo = Fn<CreateCpuMemoryInfoFn>(api, I_CreateCpuMemoryInfo);
            o.createTensor = Fn<CreateTensorFn>(api, I_CreateTensorWithDataAsOrtValue);
            o.getTensorMutableData = Fn<GetPtrFn>(api, I_GetTensorMutableData);
            o.getTensorTypeAndShape = Fn<GetPtrFn>(api, I_GetTensorTypeAndShape);
            o.sessionGetInputCount = Fn<GetCountFn>(api, I_SessionGetInputCount);
            o.sessionGetOutputCount = Fn<GetCountFn>(api, I_SessionGetOutputCount);
            o.getDimensionsCount = Fn<GetCountFn>(api, I_GetDimensionsCount);
            o.getDimensions = Fn<GetDimensionsFn>(api, I_GetDimensions);
            o.sessionGetInputName = Fn<GetNameFn>(api, I_SessionGetInputName);
            o.sessionGetOutputName = Fn<GetNameFn>(api, I_SessionGetOutputName);
            o.getAllocator = Fn<GetAllocatorFn>(api, I_GetAllocatorWithDefaultOptions);
            o.allocatorFree = Fn<AllocatorFreeFn>(api, I_AllocatorFree);
            o.run = Fn<RunFn>(api, I_Run);

            o.Check(o.createEnv(3, "LiteCap", out o.env));                 // ORT_LOGGING_LEVEL_ERROR
            o.Check(o.createCpuMemoryInfo(0, 0, out o.memInfo));           // OrtDeviceAllocator, OrtMemTypeDefault
            o.Check(o.getAllocator(out o.allocator));
            return o;
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate IntPtr GetApiBaseFn();

        public void Check(IntPtr status)
        {
            if (status == IntPtr.Zero) return;
            string msg = Marshal.PtrToStringAnsi(getErrorMessage(status));
            releaseStatus(status);
            throw new TranslateException("ONNX Runtime: " + msg);
        }

        public IntPtr CreateSession(string modelPath, int threads)
        {
            IntPtr options;
            Check(createSessionOptions(out options));
            IntPtr path = IntPtr.Zero;
            try
            {
                Check(setGraphOptimizationLevel(options, 99));   // ORT_ENABLE_ALL
                Check(setIntraOpNumThreads(options, threads));
                if (NativeLib.IsWindows) path = Marshal.StringToHGlobalUni(modelPath);   // ORTCHAR_T is wchar_t on Windows
                else
                {
                    byte[] b = Encoding.UTF8.GetBytes(modelPath + "\0");
                    path = Marshal.AllocHGlobal(b.Length);
                    Marshal.Copy(b, 0, path, b.Length);
                }
                IntPtr session;
                Check(createSession(env, path, options, out session));
                return session;
            }
            finally
            {
                if (path != IntPtr.Zero) Marshal.FreeHGlobal(path);
                releaseSessionOptions(options);
            }
        }

        public List<string> Names(IntPtr session, bool inputs)
        {
            UIntPtr count;
            Check((inputs ? sessionGetInputCount : sessionGetOutputCount)(session, out count));
            List<string> names = new List<string>();
            for (ulong i = 0; i < count.ToUInt64(); i++)
            {
                IntPtr p;
                Check((inputs ? sessionGetInputName : sessionGetOutputName)(session, new UIntPtr(i), allocator, out p));
                names.Add(Marshal.PtrToStringAnsi(p));
                Check(allocatorFree(allocator, p));
            }
            return names;
        }

        public IntPtr CreateTensor(IntPtr data, long bytes, long[] shape, int type)
        {
            IntPtr value;
            Check(createTensor(memInfo, data, new UIntPtr((ulong)bytes), shape, new UIntPtr((ulong)shape.Length), type, out value));
            return value;
        }

        public void Run(IntPtr session, IntPtr[] inNames, IntPtr[] inValues, IntPtr[] outNames, IntPtr[] outValues)
        {
            Check(run(session, IntPtr.Zero, inNames, inValues, new UIntPtr((ulong)inNames.Length), outNames, new UIntPtr((ulong)outNames.Length), outValues));
        }

        public long[] Shape(IntPtr value)
        {
            IntPtr info;
            Check(getTensorTypeAndShape(value, out info));
            try
            {
                UIntPtr n;
                Check(getDimensionsCount(info, out n));
                long[] dims = new long[(int)n.ToUInt64()];
                Check(getDimensions(info, dims, n));
                return dims;
            }
            finally { releaseTypeInfo(info); }
        }

        public IntPtr Data(IntPtr value)
        {
            IntPtr p;
            Check(getTensorMutableData(value, out p));
            return p;
        }
    }

    // A pinned managed array wrapped as an OrtValue; the array must outlive the run.
    sealed class OrtInput : IDisposable
    {
        readonly Ort ort;
        GCHandle pin;
        public readonly IntPtr Value;

        OrtInput(Ort ort, Array data, long bytes, long[] shape, int type)
        {
            this.ort = ort;
            pin = GCHandle.Alloc(data, GCHandleType.Pinned);
            Value = ort.CreateTensor(pin.AddrOfPinnedObject(), bytes, shape, type);
        }

        static long Count(long[] shape) { long n = 1; foreach (long d in shape) n *= d; return n; }

        public static OrtInput Floats(Ort ort, float[] data, params long[] shape)
        {
            long n = Count(shape);
            return new OrtInput(ort, data.Length == 0 ? new float[1] : data, n * 4, shape, Ort.TypeFloat);
        }

        public static OrtInput Longs(Ort ort, long[] data, params long[] shape)
        {
            return new OrtInput(ort, data, Count(shape) * 8, shape, Ort.TypeInt64);
        }

        public static OrtInput Bool(Ort ort, bool value)
        {
            return new OrtInput(ort, new byte[] { (byte)(value ? 1 : 0) }, 1, new long[] { 1 }, Ort.TypeBool);
        }

        public void Dispose()
        {
            ort.ReleaseValue(Value);
            if (pin.IsAllocated) pin.Free();
        }
    }

    sealed class OrtModel : IDisposable
    {
        readonly Ort ort;
        IntPtr session;
        public readonly List<string> Inputs, Outputs;
        readonly Dictionary<string, IntPtr> nameCache = new Dictionary<string, IntPtr>();

        public OrtModel(Ort ort, string path, int threads)
        {
            this.ort = ort;
            session = ort.CreateSession(path, threads);
            Inputs = ort.Names(session, true);
            Outputs = ort.Names(session, false);
        }

        IntPtr Name(string s)
        {
            IntPtr p;
            if (!nameCache.TryGetValue(s, out p)) { p = Marshal.StringToHGlobalAnsi(s); nameCache[s] = p; }
            return p;
        }

        // Runs with inputs supplied by name; returns OrtValues the caller must release (ort.ReleaseValue).
        public IntPtr[] Run(Dictionary<string, IntPtr> inputs, List<string> outputs)
        {
            IntPtr[] inNames = new IntPtr[Inputs.Count], inValues = new IntPtr[Inputs.Count];
            for (int i = 0; i < Inputs.Count; i++)
            {
                IntPtr v;
                if (!inputs.TryGetValue(Inputs[i], out v)) throw new TranslateException("No value for model input " + Inputs[i]);
                inNames[i] = Name(Inputs[i]);
                inValues[i] = v;
            }
            IntPtr[] outNames = new IntPtr[outputs.Count], outValues = new IntPtr[outputs.Count];
            for (int i = 0; i < outputs.Count; i++) outNames[i] = Name(outputs[i]);
            ort.Run(session, inNames, inValues, outNames, outValues);
            return outValues;
        }

        public void Dispose()
        {
            if (session != IntPtr.Zero) { ort.ReleaseSession(session); session = IntPtr.Zero; }
            foreach (IntPtr p in nameCache.Values) Marshal.FreeHGlobal(p);
            nameCache.Clear();
        }
    }

    // ------------------------------------------------------------------
    // SentencePiece (unigram) tokenizer read straight from the .spm protobuf
    // ------------------------------------------------------------------
    sealed class SentencePiece
    {
        readonly Dictionary<string, float> scores = new Dictionary<string, float>();
        readonly HashSet<string> userDefined = new HashSet<string>();
        readonly int maxLen;
        readonly float unkScore;
        public const string Space = "▁";

        public SentencePiece(string path)
        {
            byte[] b = File.ReadAllBytes(path);
            int pos = 0;
            float min = 0;
            int longest = 1;
            while (pos < b.Length)
            {
                ulong key = Varint(b, ref pos);
                int field = (int)(key >> 3), wire = (int)(key & 7);
                if (field == 1 && wire == 2)
                {
                    int len = (int)Varint(b, ref pos), end = pos + len;
                    string piece = null;
                    float score = 0;
                    int type = 1;
                    while (pos < end)
                    {
                        ulong k2 = Varint(b, ref pos);
                        int f2 = (int)(k2 >> 3), w2 = (int)(k2 & 7);
                        if (f2 == 1 && w2 == 2) { int l = (int)Varint(b, ref pos); piece = Encoding.UTF8.GetString(b, pos, l); pos += l; }
                        else if (f2 == 2 && w2 == 5) { score = BitConverter.ToSingle(b, pos); pos += 4; }
                        else if (f2 == 3 && w2 == 0) type = (int)Varint(b, ref pos);
                        else Skip(b, ref pos, w2);
                    }
                    pos = end;
                    // 1 normal, 4 user-defined are matchable; 2 unknown, 3 control, 5 unused, 6 byte are not
                    if (piece == null || (type != 1 && type != 4)) continue;
                    scores[piece] = score;
                    if (type == 4) userDefined.Add(piece);
                    if (score < min) min = score;
                    longest = Math.Max(longest, piece.Length);
                }
                else Skip(b, ref pos, wire);
            }
            maxLen = longest;
            unkScore = min - 10f;
        }

        static ulong Varint(byte[] b, ref int pos)
        {
            ulong v = 0;
            int shift = 0;
            while (true)
            {
                byte x = b[pos++];
                v |= (ulong)(x & 0x7f) << shift;
                if ((x & 0x80) == 0) return v;
                shift += 7;
            }
        }

        static void Skip(byte[] b, ref int pos, int wire)
        {
            if (wire == 0) Varint(b, ref pos);
            else if (wire == 1) pos += 8;
            else if (wire == 2) { int l = (int)Varint(b, ref pos); pos += l; }
            else if (wire == 5) pos += 4;
            else throw new TranslateException("Bad tokenizer file");
        }

        // nmt_nfkc-style normalization: NFKC, whitespace collapsed, dummy prefix, spaces become U+2581.
        public static string Normalize(string text)
        {
            string s = text.Normalize(NormalizationForm.FormKC);
            s = Regex.Replace(s, @"\s+", " ").Trim();
            return Space + s.Replace(" ", Space);
        }

        // Viterbi segmentation maximizing the summed piece scores; unknown characters become single pieces
        // (consecutive unknowns are merged, as SentencePiece does).
        public List<string> Encode(string text)
        {
            string s = Normalize(text);
            int n = s.Length;
            double[] best = new double[n + 1];
            int[] from = new int[n + 1];
            bool[] unk = new bool[n + 1];
            for (int i = 1; i <= n; i++) best[i] = double.NegativeInfinity;
            for (int end = 1; end <= n; end++)
            {
                for (int len = 1; len <= maxLen && len <= end; len++)
                {
                    int start = end - len;
                    if (double.IsNegativeInfinity(best[start])) continue;
                    float sc;
                    string piece = s.Substring(start, len);
                    if (!scores.TryGetValue(piece, out sc)) continue;
                    if (userDefined.Contains(piece)) sc = 0;
                    double v = best[start] + sc;
                    if (v > best[end]) { best[end] = v; from[end] = start; unk[end] = false; }
                }
                if (double.IsNegativeInfinity(best[end]))
                {
                    // Unknown character (keep surrogate pairs whole).
                    int start = end - 1;
                    if (start > 0 && char.IsLowSurrogate(s[start]) && char.IsHighSurrogate(s[start - 1])) start--;
                    if (!double.IsNegativeInfinity(best[start]))
                    {
                        best[end] = best[start] + unkScore;
                        from[end] = start;
                        unk[end] = true;
                    }
                }
            }
            List<string> pieces = new List<string>();
            List<bool> unknown = new List<bool>();
            for (int e = n; e > 0; e = from[e])
            {
                pieces.Add(s.Substring(from[e], e - from[e]));
                unknown.Add(unk[e]);
            }
            pieces.Reverse();
            unknown.Reverse();
            List<string> merged = new List<string>();
            for (int i = 0; i < pieces.Count; i++)
            {
                if (i > 0 && unknown[i] && unknown[i - 1]) merged[merged.Count - 1] += pieces[i];
                else merged.Add(pieces[i]);
            }
            return merged;
        }

        public static string Decode(IEnumerable<string> pieces)
        {
            StringBuilder sb = new StringBuilder();
            foreach (string p in pieces) sb.Append(p);
            string s = sb.ToString().Replace(Space, " ");
            return s.StartsWith(" ") ? s.Substring(1) : s;
        }
    }

    // ------------------------------------------------------------------
    // One translation direction: encoder + merged decoder (KV cache), beam search
    // ------------------------------------------------------------------
    sealed class MarianModel : IDisposable
    {
        readonly Ort ort;
        readonly OrtModel encoder, decoder;
        readonly SentencePiece source;
        readonly Dictionary<string, int> vocab;
        readonly string[] idToPiece;
        readonly int layers, heads, headDim, dModel, vocabSize, padId, eosId, unkId, startId;
        readonly string prefixToken;
        readonly List<string> decoderOutputs;
        public int Beams = 4;
        public int MaxNew;   // 0 = derived from the source length

        public MarianModel(Ort ort, string dir, string prefixToken, int threads)
        {
            this.ort = ort;
            this.prefixToken = prefixToken;
            string cfg = File.ReadAllText(Path.Combine(dir, "config.json"));
            layers = CfgInt(cfg, "decoder_layers");
            heads = CfgInt(cfg, "decoder_attention_heads");
            dModel = CfgInt(cfg, "d_model");
            headDim = dModel / heads;
            padId = CfgInt(cfg, "pad_token_id");
            eosId = CfgInt(cfg, "eos_token_id");
            startId = CfgIntOr(cfg, "decoder_start_token_id", padId);

            vocab = Json.ReadStringIntMap(File.ReadAllText(Path.Combine(dir, "vocab.json"), Encoding.UTF8));
            vocabSize = 0;
            foreach (int v in vocab.Values) vocabSize = Math.Max(vocabSize, v + 1);
            vocabSize = Math.Max(vocabSize, CfgInt(cfg, "vocab_size"));
            idToPiece = new string[vocabSize];
            foreach (KeyValuePair<string, int> kv in vocab) idToPiece[kv.Value] = kv.Key;
            if (!vocab.TryGetValue("<unk>", out unkId)) unkId = 1;

            source = new SentencePiece(Path.Combine(dir, "source.spm"));
            encoder = new OrtModel(ort, Path.Combine(dir, "encoder.onnx"), threads);
            decoder = new OrtModel(ort, Path.Combine(dir, "decoder.onnx"), threads);
            decoderOutputs = new List<string>();
            decoderOutputs.Add("logits");
            for (int l = 0; l < layers; l++)
            {
                decoderOutputs.Add("present." + l + ".decoder.key");
                decoderOutputs.Add("present." + l + ".decoder.value");
                decoderOutputs.Add("present." + l + ".encoder.key");
                decoderOutputs.Add("present." + l + ".encoder.value");
            }
        }

        static int CfgInt(string json, string key)
        {
            Match m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*(-?\\d+)");
            if (!m.Success) throw new TranslateException("config.json has no " + key);
            return int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        static int CfgIntOr(string json, string key, int fallback)
        {
            Match m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*(-?\\d+)");
            return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : fallback;
        }

        public List<int> Tokenize(string text)
        {
            List<int> ids = new List<int>();
            if (prefixToken != null && vocab.ContainsKey(prefixToken)) ids.Add(vocab[prefixToken]);
            foreach (string p in source.Encode(text))
            {
                int id;
                ids.Add(vocab.TryGetValue(p, out id) ? id : unkId);
            }
            ids.Add(eosId);
            return ids;
        }

        public string Detokenize(List<int> ids)
        {
            List<string> pieces = new List<string>();
            foreach (int id in ids)
            {
                if (id == eosId || id == padId || id == unkId || id < 0 || id >= idToPiece.Length || idToPiece[id] == null) continue;
                if (idToPiece[id].StartsWith(">>") && idToPiece[id].EndsWith("<<")) continue;
                pieces.Add(idToPiece[id]);
            }
            return SentencePiece.Decode(pieces);
        }

        float[] ReadFloats(IntPtr value, out long[] shape)
        {
            shape = ort.Shape(value);
            long n = 1;
            foreach (long d in shape) n *= d;
            float[] a = new float[n];
            if (n > 0) Marshal.Copy(ort.Data(value), a, 0, (int)n);
            return a;
        }

        public string Translate(string text, Func<bool> cancelled)
        {
            return Detokenize(Generate(Tokenize(text), cancelled));
        }

        class Hyp { public List<int> Tokens; public double Score; }

        // Beam search over the merged decoder, reusing its key/value cache between steps.
        public List<int> Generate(List<int> src, Func<bool> cancelled)
        {
            int n = src.Count, k = Math.Max(1, Beams);
            int maxNew = MaxNew > 0 ? MaxNew : Math.Min(400, n * 3 + 10);

            // Encoder (batch 1), then repeat its output for every beam.
            long[] srcIds = new long[n], srcMask = new long[n];
            for (int i = 0; i < n; i++) { srcIds[i] = src[i]; srcMask[i] = 1; }
            float[] enc;
            using (OrtInput ids = OrtInput.Longs(ort, srcIds, 1, n))
            using (OrtInput mask = OrtInput.Longs(ort, srcMask, 1, n))
            {
                Dictionary<string, IntPtr> inputs = new Dictionary<string, IntPtr>();
                inputs["input_ids"] = ids.Value;
                inputs["attention_mask"] = mask.Value;
                List<string> outs = new List<string>();
                outs.Add("last_hidden_state");
                IntPtr[] r = encoder.Run(inputs, outs);
                long[] shape;
                try { enc = ReadFloats(r[0], out shape); }
                finally { ort.ReleaseValue(r[0]); }
            }
            float[] encK = new float[k * enc.Length];
            for (int b = 0; b < k; b++) Array.Copy(enc, 0, encK, b * enc.Length, enc.Length);
            long[] maskK = new long[k * n];
            for (int i = 0; i < maskK.Length; i++) maskK[i] = 1;

            List<int>[] seqs = new List<int>[k];
            double[] scores = new double[k];
            for (int b = 0; b < k; b++) { seqs[b] = new List<int>(); scores[b] = b == 0 ? 0 : double.NegativeInfinity; }
            int[] last = new int[k];
            for (int b = 0; b < k; b++) last[b] = startId;

            float[][] pastDec = new float[layers * 2][];
            float[][] pastEnc = new float[layers * 2][];
            for (int i = 0; i < layers * 2; i++) { pastDec[i] = new float[0]; pastEnc[i] = new float[0]; }
            int pastLen = 0;
            List<Hyp> done = new List<Hyp>();
            bool finished = false;

            for (int step = 0; step < maxNew; step++)
            {
                if (cancelled != null && cancelled()) throw new OperationCanceledException();
                bool useCache = step > 0;
                List<OrtInput> owned = new List<OrtInput>();
                IntPtr[] outs = null;
                float[] logits;
                long[] lshape;
                try
                {
                    long[] inIds = new long[k];
                    for (int b = 0; b < k; b++) inIds[b] = last[b];
                    Dictionary<string, IntPtr> inputs = new Dictionary<string, IntPtr>();
                    OrtInput x;
                    x = OrtInput.Longs(ort, maskK, k, n); owned.Add(x); inputs["encoder_attention_mask"] = x.Value;
                    x = OrtInput.Longs(ort, inIds, k, 1); owned.Add(x); inputs["input_ids"] = x.Value;
                    x = OrtInput.Floats(ort, encK, k, n, dModel); owned.Add(x); inputs["encoder_hidden_states"] = x.Value;
                    for (int l = 0; l < layers; l++)
                    {
                        x = OrtInput.Floats(ort, pastDec[l * 2], k, heads, pastLen, headDim); owned.Add(x); inputs["past_key_values." + l + ".decoder.key"] = x.Value;
                        x = OrtInput.Floats(ort, pastDec[l * 2 + 1], k, heads, pastLen, headDim); owned.Add(x); inputs["past_key_values." + l + ".decoder.value"] = x.Value;
                        int encLen = useCache ? n : 0;
                        x = OrtInput.Floats(ort, pastEnc[l * 2], k, heads, encLen, headDim); owned.Add(x); inputs["past_key_values." + l + ".encoder.key"] = x.Value;
                        x = OrtInput.Floats(ort, pastEnc[l * 2 + 1], k, heads, encLen, headDim); owned.Add(x); inputs["past_key_values." + l + ".encoder.value"] = x.Value;
                    }
                    x = OrtInput.Bool(ort, useCache); owned.Add(x); inputs["use_cache_branch"] = x.Value;

                    outs = decoder.Run(inputs, decoderOutputs);
                    logits = ReadFloats(outs[0], out lshape);
                    float[][] presentDec = new float[layers * 2][];
                    for (int l = 0; l < layers; l++)
                    {
                        long[] s;
                        presentDec[l * 2] = ReadFloats(outs[1 + l * 4], out s);
                        presentDec[l * 2 + 1] = ReadFloats(outs[2 + l * 4], out s);
                        if (!useCache)
                        {
                            pastEnc[l * 2] = ReadFloats(outs[3 + l * 4], out s);
                            pastEnc[l * 2 + 1] = ReadFloats(outs[4 + l * 4], out s);
                        }
                    }
                    pastDec = presentDec;
                }
                finally
                {
                    if (outs != null) foreach (IntPtr v in outs) if (v != IntPtr.Zero) ort.ReleaseValue(v);
                    foreach (OrtInput o in owned) o.Dispose();
                }
                pastLen++;

                // Candidates: top 2k continuations across all live beams.
                int V = (int)lshape[2];
                List<KeyValuePair<double, long>> cand = new List<KeyValuePair<double, long>>();
                for (int b = 0; b < k; b++)
                {
                    if (double.IsNegativeInfinity(scores[b])) continue;
                    int off = b * V;   // logits are [k, 1, V]
                    logits[off + padId] = float.NegativeInfinity;
                    double max = double.NegativeInfinity;
                    for (int v = 0; v < V; v++) if (logits[off + v] > max) max = logits[off + v];
                    double sum = 0;
                    for (int v = 0; v < V; v++) sum += Math.Exp(logits[off + v] - max);
                    double lse = max + Math.Log(sum);
                    // keep the best 2k of this beam
                    List<int> top = TopK(logits, off, V, 2 * k);
                    foreach (int v in top) cand.Add(new KeyValuePair<double, long>(scores[b] + logits[off + v] - lse, (long)b * V + v));
                }
                cand.Sort(delegate (KeyValuePair<double, long> a, KeyValuePair<double, long> c) { return c.Key.CompareTo(a.Key); });

                List<int> fromBeam = new List<int>(), token = new List<int>();
                List<double> newScores = new List<double>();
                for (int r = 0; r < cand.Count && newScores.Count < k; r++)
                {
                    int b = (int)(cand[r].Value / V), v = (int)(cand[r].Value % V);
                    if (v == eosId)
                    {
                        if (r < k)
                        {
                            Hyp h = new Hyp();
                            h.Tokens = new List<int>(seqs[b]);
                            h.Score = cand[r].Key / (seqs[b].Count + 1);
                            done.Add(h);
                        }
                        continue;
                    }
                    fromBeam.Add(b); token.Add(v); newScores.Add(cand[r].Key);
                }
                if (newScores.Count == 0) break;

                // Finished once k hypotheses ended and no live beam can still beat the worst of them.
                if (done.Count >= k)
                {
                    done.Sort(delegate (Hyp a, Hyp c) { return c.Score.CompareTo(a.Score); });
                    if (done.Count > k) done.RemoveRange(k, done.Count - k);
                    double bestLive = cand[0].Key / (seqs[0].Count + 1);
                    if (done[k - 1].Score >= bestLive) { finished = true; break; }
                }

                // Reorder beams and their decoder cache.
                List<int>[] nextSeqs = new List<int>[k];
                double[] nextScores = new double[k];
                int block = heads * pastLen * headDim;
                float[][] reordered = new float[layers * 2][];
                for (int i = 0; i < layers * 2; i++) reordered[i] = new float[k * block];
                for (int b = 0; b < k; b++)
                {
                    int src2 = b < fromBeam.Count ? fromBeam[b] : fromBeam[0];
                    nextSeqs[b] = new List<int>(seqs[src2]);
                    if (b < fromBeam.Count) { nextSeqs[b].Add(token[b]); nextScores[b] = newScores[b]; last[b] = token[b]; }
                    else { nextSeqs[b].Add(token[0]); nextScores[b] = double.NegativeInfinity; last[b] = token[0]; }
                    for (int i = 0; i < layers * 2; i++) Array.Copy(pastDec[i], src2 * block, reordered[i], b * block, block);
                }
                seqs = nextSeqs;
                scores = nextScores;
                pastDec = reordered;
            }

            // Length limit reached: live beams compete with the finished ones.
            for (int b = 0; b < k && !finished; b++)
            {
                if (double.IsNegativeInfinity(scores[b])) continue;
                Hyp h = new Hyp();
                h.Tokens = seqs[b];
                h.Score = scores[b] / Math.Max(1, seqs[b].Count);
                done.Add(h);
            }
            if (done.Count == 0) return seqs[0];
            done.Sort(delegate (Hyp a, Hyp c) { return c.Score.CompareTo(a.Score); });
            return done[0].Tokens;
        }

        static List<int> TopK(float[] a, int off, int len, int count)
        {
            List<int> idx = new List<int>();
            for (int v = 0; v < len; v++)
            {
                float x = a[off + v];
                if (float.IsNegativeInfinity(x)) continue;
                if (idx.Count < count) { idx.Add(v); if (idx.Count == count) idx.Sort(delegate (int p, int q) { return a[off + q].CompareTo(a[off + p]); }); continue; }
                if (x <= a[off + idx[count - 1]]) continue;
                int pos = count - 1;
                while (pos > 0 && a[off + idx[pos - 1]] < x) pos--;
                idx.Insert(pos, v);
                idx.RemoveAt(count);
            }
            if (idx.Count < count) idx.Sort(delegate (int p, int q) { return a[off + q].CompareTo(a[off + p]); });
            return idx;
        }

        public void Dispose()
        {
            encoder.Dispose();
            decoder.Dispose();
        }
    }

    // Tiny JSON reader for vocab.json ({"piece": id, ...}).
    static class Json
    {
        public static Dictionary<string, int> ReadStringIntMap(string s)
        {
            Dictionary<string, int> map = new Dictionary<string, int>();
            int i = s.IndexOf('{') + 1;
            while (true)
            {
                while (i < s.Length && (char.IsWhiteSpace(s[i]) || s[i] == ',')) i++;
                if (i >= s.Length || s[i] == '}') break;
                string key = ReadString(s, ref i);
                while (s[i] != ':') i++;
                i++;
                while (char.IsWhiteSpace(s[i])) i++;
                int start = i;
                while (i < s.Length && (s[i] == '-' || char.IsDigit(s[i]))) i++;
                map[key] = int.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
            }
            return map;
        }

        static string ReadString(string s, ref int i)
        {
            if (s[i] != '"') throw new TranslateException("Bad vocab.json");
            i++;
            StringBuilder sb = new StringBuilder();
            while (s[i] != '"')
            {
                char c = s[i++];
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
            i++;
            return sb.ToString();
        }
    }

    // ------------------------------------------------------------------
    // Public entry: pack location, download, direction detection, text-level translation
    // ------------------------------------------------------------------
    static class OfflineTranslator
    {
        public const int PackVersion = 1;
        public const int PackMB = 170;   // download size shown to the user
        public const string PackUrl = "https://github.com/LorraineWong/LiteCap/releases/download/translate-pack-1/LiteCap-Translate-1.zip";
        public const string PackPage = Ui.RepoUrl + "/releases/tag/translate-pack-1";

        static readonly object gate = new object();
        static Ort ort;
        static readonly Dictionary<string, MarianModel> models = new Dictionary<string, MarianModel>();
        static string overrideRoot;

        public static string Root
        {
            get
            {
                if (overrideRoot != null) return overrideRoot;
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiteCap", "translate");
            }
            set { overrideRoot = value; }
        }

        static readonly string[] ModelFiles = { "config.json", "vocab.json", "source.spm", "encoder.onnx", "decoder.onnx" };

        public static bool Installed
        {
            get
            {
                try
                {
                    if (!File.Exists(OrtPath)) return false;
                    foreach (string dir in new string[] { "zh-en", "en-zh" })
                        foreach (string f in ModelFiles)
                            if (!File.Exists(Path.Combine(Root, dir, f))) return false;
                    return true;
                }
                catch { return false; }
            }
        }

        static string OrtPath
        {
            get
            {
                if (!NativeLib.IsWindows) return Path.Combine(Root, "ort", "libonnxruntime.so");
                string arch = (Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432") ?? Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") ?? "").ToUpperInvariant();
                return Path.Combine(Root, "ort", arch == "ARM64" ? "win-arm64" : "win-x64", "onnxruntime.dll");
            }
        }

        // "zh-en" when the text is mostly Chinese, otherwise "en-zh".
        public static string DetectDirection(string text)
        {
            int cjk = 0, latin = 0;
            foreach (char c in text)
            {
                if ((c >= 0x3400 && c <= 0x9fff) || (c >= 0xf900 && c <= 0xfaff)) cjk++;
                else if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')) latin++;
            }
            // A Chinese character carries about as much as a 3-4 letter word.
            return cjk * 3 >= latin ? "zh-en" : "en-zh";
        }

        static MarianModel Model(string direction)
        {
            MarianModel m;
            if (models.TryGetValue(direction, out m)) return m;
            if (ort == null) ort = Ort.Load(OrtPath);
            int threads = Math.Max(1, Math.Min(4, Environment.ProcessorCount));
            m = new MarianModel(ort, Path.Combine(Root, direction), direction == "en-zh" ? ">>cmn_Hans<<" : null, threads);
            models[direction] = m;
            return m;
        }

        // Releases the models (they hold a few hundred MB). onnxruntime itself stays loaded.
        public static void Unload()
        {
            lock (gate)
            {
                foreach (MarianModel m in models.Values) m.Dispose();
                models.Clear();
            }
        }

        static readonly Regex ZhSentence = new Regex(@"(?<=[。！？；!?;])");
        static readonly Regex EnSentence = new Regex(@"(?<=[.!?;])\s+(?=\S)");

        public static List<string> SplitSentences(string paragraph, bool chinese)
        {
            List<string> parts = new List<string>();
            foreach (string raw in (chinese ? ZhSentence : EnSentence).Split(paragraph))
            {
                string s = raw.Trim();
                if (s.Length == 0) continue;
                // Very long sentences translate poorly; cut them at commas.
                while (s.Length > 220)
                {
                    int cut = s.LastIndexOfAny(new char[] { '，', ',', '、', ':', '：' }, 200);
                    if (cut < 40) cut = 200;
                    parts.Add(s.Substring(0, cut + 1).Trim());
                    s = s.Substring(cut + 1).Trim();
                }
                if (s.Length > 0) parts.Add(s);
            }
            return parts;
        }

        // Translates paragraph by paragraph (lines are kept), sentence by sentence.
        public static string Translate(string text, string direction, Func<bool> cancelled, Action<int, int> progress)
        {
            lock (gate)
            {
                MarianModel model = Model(direction);
                bool fromZh = direction == "zh-en";
                string[] paragraphs = text.Replace("\r\n", "\n").Split('\n');
                List<List<string>> work = new List<List<string>>();
                int total = 0;
                foreach (string p in paragraphs)
                {
                    List<string> s = SplitSentences(p, fromZh);
                    work.Add(s);
                    total += s.Count;
                }
                StringBuilder sb = new StringBuilder();
                int doneCount = 0;
                for (int i = 0; i < work.Count; i++)
                {
                    if (i > 0) sb.Append("\r\n");
                    for (int j = 0; j < work[i].Count; j++)
                    {
                        if (progress != null) progress(doneCount, total);
                        string t = model.Translate(work[i][j], cancelled).Trim();
                        if (j > 0 && fromZh) sb.Append(' ');
                        sb.Append(t);
                        doneCount++;
                    }
                }
                if (progress != null) progress(total, total);
                return sb.ToString();
            }
        }

        // Downloads and unpacks the translation pack. progress(bytesSoFar, totalBytes or -1).
        public static void Download(Action<long, long> progress, Func<bool> cancelled)
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }   // TLS 1.2 on older .NET defaults
            string root = Root;
            Directory.CreateDirectory(root);
            string zip = Path.Combine(root, "pack.download");
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(PackUrl);
            req.UserAgent = "LiteCap";
            req.AllowAutoRedirect = true;
            req.Timeout = 30000;
            req.ReadWriteTimeout = 60000;
            try { req.Proxy = WebRequest.GetSystemWebProxy(); req.Proxy.Credentials = CredentialCache.DefaultCredentials; } catch { }
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (Stream input = resp.GetResponseStream())
            using (FileStream output = new FileStream(zip, FileMode.Create, FileAccess.Write))
            {
                long total = resp.ContentLength, got = 0;
                byte[] buf = new byte[1 << 16];
                int r;
                while ((r = input.Read(buf, 0, buf.Length)) > 0)
                {
                    if (cancelled != null && cancelled()) throw new OperationCanceledException();
                    output.Write(buf, 0, r);
                    got += r;
                    if (progress != null) progress(got, total);
                }
            }
            Install(zip, true);
        }

        // Unpacks a downloaded (or manually saved) pack zip into the translation folder.
        public static void Install(string zipPath, bool deleteZip)
        {
            Unload();
            string root = Root;
            string staging = Path.Combine(root, "staging");
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, staging);
            // The zip may contain a top-level folder; find the one holding "ort".
            string content = staging;
            if (!Directory.Exists(Path.Combine(content, "ort")))
                foreach (string d in Directory.GetDirectories(staging))
                    if (Directory.Exists(Path.Combine(d, "ort"))) content = d;
            foreach (string name in new string[] { "ort", "zh-en", "en-zh" })
            {
                string from = Path.Combine(content, name), to = Path.Combine(root, name);
                if (!Directory.Exists(from)) throw new TranslateException("The translation pack is incomplete (" + name + ")");
                if (Directory.Exists(to))
                {
                    if (ort != null && name == "ort") continue;   // in use; same files
                    Directory.Delete(to, true);
                }
                Directory.Move(from, to);
            }
            Directory.Delete(staging, true);
            if (deleteZip) { try { File.Delete(zipPath); } catch { } }
        }
    }

    // "LiteCap.exe --translate-selftest <pack.zip | pack folder> <result.txt>": used by CI on a real Windows machine.
    static class TranslateSelfTest
    {
        public static int Run(string pack, string resultFile)
        {
            StringBuilder log = new StringBuilder();
            int code = 0;
            try
            {
                if (File.Exists(pack))
                {
                    OfflineTranslator.Root = Path.Combine(Path.GetTempPath(), "LiteCap-selftest");
                    if (Directory.Exists(OfflineTranslator.Root)) Directory.Delete(OfflineTranslator.Root, true);
                    OfflineTranslator.Install(pack, false);
                }
                else OfflineTranslator.Root = pack;
                log.AppendLine("installed=" + OfflineTranslator.Installed);
                string[] samples = {
                    "今天天气很好，我们去公园散步。",
                    "服务器支持大模型推理。数据中心的算力需求正在快速增长。",
                    "请把这份报告翻译成英文！",
                    "The screenshot tool can recognize text and translate it offline.",
                    "Compute demand in data centers is growing fast. Customers care about cost."
                };
                foreach (string s in samples)
                {
                    string dir = OfflineTranslator.DetectDirection(s);
                    System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                    string t = OfflineTranslator.Translate(s, dir, null, null);
                    log.AppendLine(dir + "\t" + sw.ElapsedMilliseconds + "ms\t" + s + "\t=>\t" + t);
                    if (t.Trim().Length == 0) code = 2;
                }
                OfflineTranslator.Unload();
            }
            catch (Exception ex)
            {
                log.AppendLine("ERROR " + ex);
                code = 1;
            }
            File.WriteAllText(resultFile, log.ToString(), new UTF8Encoding(false));
            return code;
        }
    }
}
