// LinderaWasmHost.cs

using System;
using System.IO;
using System.Text;
using Wasmtime;

namespace jpn_lang_dbm_desktop_app;

public sealed class LinderaWasmHost : IDisposable
{
    private readonly Engine _engine;
    private readonly Module _module;
    private readonly Linker _linker;
    private readonly Store _store;
    private readonly Instance _instance;
    private readonly Memory _memory;

    private readonly Func<int, int> _wasmAlloc;
    private readonly Action<int, int> _wasmDealloc;
    private readonly Func<int> _parseContextJson;
    private readonly Func<int, int, int> _linderaParseJson;
    private readonly Func<int> _lastResultPtr;
    private readonly Func<int> _lastResultLen;
    private readonly Func<int> _lastErrorPtr;
    private readonly Func<int> _lastErrorLen;

    public LinderaWasmHost(string wasmPath)
    {
        if (!File.Exists(wasmPath))
            throw new FileNotFoundException("WASM file not found.", wasmPath);

        _engine = new Engine();
        _module = Module.FromFile(_engine, wasmPath);
        _linker = new Linker(_engine);
        _store = new Store(_engine);
        _instance = _linker.Instantiate(_store, _module);

        _memory = _instance.GetMemory("memory")
            ?? throw new Exception("WASM module did not export memory.");

        _wasmAlloc = _instance.GetFunction<int, int>("wasm_alloc")
            ?? throw new Exception("WASM export 'wasm_alloc' not found.");

        _wasmDealloc = _instance.GetAction<int, int>("wasm_dealloc")
            ?? throw new Exception("WASM export 'wasm_dealloc' not found.");

        _parseContextJson = _instance.GetFunction<int>("parse_context_json")
            ?? throw new Exception("WASM export 'parse_context_json' not found.");

        _linderaParseJson = _instance.GetFunction<int, int, int>("lindera_parse_json")
            ?? throw new Exception("WASM export 'lindera_parse_json' not found.");

        _lastResultPtr = _instance.GetFunction<int>("last_result_ptr")
            ?? throw new Exception("WASM export 'last_result_ptr' not found.");

        _lastResultLen = _instance.GetFunction<int>("last_result_len")
            ?? throw new Exception("WASM export 'last_result_len' not found.");

        _lastErrorPtr = _instance.GetFunction<int>("last_error_ptr")
            ?? throw new Exception("WASM export 'last_error_ptr' not found.");

        _lastErrorLen = _instance.GetFunction<int>("last_error_len")
            ?? throw new Exception("WASM export 'last_error_len' not found.");
    }

    public string GetParseContextJson()
    {
        var status = _parseContextJson();

        if (status != 0)
            throw new Exception(ReadLastError());

        return ReadLastResult();
    }

    public string LinderaParseJson(string requestJson)
    {
        var requestByteCount = Encoding.UTF8.GetByteCount(requestJson);
        var requestPtr = _wasmAlloc(requestByteCount);

        try
        {
            _memory.WriteString(requestPtr, requestJson);

            var status = _linderaParseJson(requestPtr, requestByteCount);

            if (status != 0)
                throw new Exception(ReadLastError());

            return ReadLastResult();
        }
        finally
        {
            _wasmDealloc(requestPtr, requestByteCount);
        }
    }

    private string ReadLastResult()
    {
        var ptr = _lastResultPtr();
        var len = _lastResultLen();

        if (ptr < 0)
            throw new Exception("WASM returned a negative result pointer.");

        if (len < 0)
            throw new Exception("WASM returned a negative result length.");

        return _memory.ReadString(ptr, len);
    }

    private string ReadLastError()
    {
        var ptr = _lastErrorPtr();
        var len = _lastErrorLen();

        if (ptr < 0)
            throw new Exception("WASM returned a negative error pointer.");

        if (len < 0)
            throw new Exception("WASM returned a negative error length.");

        return _memory.ReadString(ptr, len);
    }

    public void Dispose()
    {
        _store.Dispose();
        _linker.Dispose();
        _module.Dispose();
        _engine.Dispose();
    }
}
