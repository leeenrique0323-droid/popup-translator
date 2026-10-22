using System.Runtime.InteropServices;

namespace PopupTranslator.Ocr;

public sealed class OcrException : Exception
{
    public OcrException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// 極簡 WinRT ABI 呼叫輔助（HSTRING、啟用工廠、vtable 呼叫）。
/// 不依賴 CsWinRT 投影組件，以保持執行檔體積極小。
/// vtable 位置：0-2 IUnknown，3-5 IInspectable，6 起為介面方法（依 .winmd 宣告順序）。
/// </summary>
internal static unsafe class WinRt
{
    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsCreateString(char* sourceString, uint length, IntPtr* hstring);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern char* WindowsGetStringRawBuffer(IntPtr hstring, uint* length);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, Guid* iid, IntPtr* factory);

    [DllImport("combase.dll", ExactSpelling = true)]
    public static extern int RoInitialize(int initType);

    [DllImport("combase.dll", ExactSpelling = true)]
    public static extern void RoUninitialize();

    public const int RO_INIT_MULTITHREADED = 1;

    public static void Check(int hr, string what)
    {
        if (hr < 0)
            throw new OcrException($"Windows OCR 呼叫失敗（{what}，0x{hr:X8}）", Marshal.GetExceptionForHR(hr));
    }

    public static IntPtr CreateHString(string s)
    {
        IntPtr h;
        fixed (char* p = s)
        {
            Check(WindowsCreateString(p, (uint)s.Length, &h), "WindowsCreateString");
        }
        return h;
    }

    public static void DeleteHString(IntPtr h)
    {
        if (h != IntPtr.Zero) WindowsDeleteString(h);
    }

    public static string HStringToString(IntPtr h)
    {
        if (h == IntPtr.Zero) return string.Empty;
        uint len;
        char* p = WindowsGetStringRawBuffer(h, &len);
        return p == null ? string.Empty : new string(p, 0, (int)len);
    }

    public static IntPtr GetActivationFactory(string runtimeClass, Guid iid)
    {
        IntPtr h = CreateHString(runtimeClass);
        try
        {
            IntPtr factory;
            int hr = RoGetActivationFactory(h, &iid, &factory);
            if (hr < 0)
                throw new OcrException(
                    $"無法使用 Windows 元件 {runtimeClass}（0x{hr:X8}）。此功能需要 Windows 10 以上版本。",
                    Marshal.GetExceptionForHR(hr));
            return factory;
        }
        finally
        {
            DeleteHString(h);
        }
    }

    private static void** Vtbl(IntPtr p) => *(void***)p;

    public static IntPtr QueryInterface(IntPtr p, Guid iid)
    {
        IntPtr result;
        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)Vtbl(p)[0])(p, &iid, &result);
        Check(hr, "QueryInterface");
        return result;
    }

    public static void Release(IntPtr p)
    {
        if (p != IntPtr.Zero)
            ((delegate* unmanaged[Stdcall]<IntPtr, uint>)Vtbl(p)[2])(p);
    }

    /// <summary>呼叫 HRESULT Method(out IInspectable*)（屬性 getter / 無參數方法）。</summary>
    public static IntPtr GetPtr(IntPtr obj, int slot, string what)
    {
        IntPtr r = IntPtr.Zero;
        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Vtbl(obj)[slot])(obj, &r);
        Check(hr, what);
        return r;
    }

    /// <summary>呼叫 HRESULT Method(IInspectable* arg, out IInspectable*)。</summary>
    public static int GetPtrWithArg(IntPtr obj, int slot, IntPtr arg, out IntPtr result)
    {
        IntPtr r = IntPtr.Zero;
        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)Vtbl(obj)[slot])(obj, arg, &r);
        result = r;
        return hr;
    }

    public static string GetString(IntPtr obj, int slot, string what)
    {
        IntPtr h = IntPtr.Zero;
        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Vtbl(obj)[slot])(obj, &h);
        Check(hr, what);
        try { return HStringToString(h); }
        finally { DeleteHString(h); }
    }

    public static uint GetUInt32(IntPtr obj, int slot, string what)
    {
        uint v = 0;
        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint*, int>)Vtbl(obj)[slot])(obj, &v);
        Check(hr, what);
        return v;
    }

    public static int GetInt32(IntPtr obj, int slot, string what)
    {
        int v = 0;
        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)Vtbl(obj)[slot])(obj, &v);
        Check(hr, what);
        return v;
    }

    public static int CallVoid(IntPtr obj, int slot) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, int>)Vtbl(obj)[slot])(obj);

    /// <summary>IVectorView&lt;T&gt;.GetAt（slot 6）。</summary>
    public static IntPtr VectorGetAt(IntPtr vector, uint index)
    {
        IntPtr r = IntPtr.Zero;
        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Vtbl(vector)[6])(vector, index, &r);
        Check(hr, "IVectorView.GetAt");
        return r;
    }

    /// <summary>IVectorView&lt;T&gt;.Size（slot 7）。</summary>
    public static uint VectorSize(IntPtr vector) => GetUInt32(vector, 7, "IVectorView.Size");

    /// <summary>ICryptographicBufferStatics.CreateFromByteArray（slot 9）。</summary>
    public static IntPtr CreateBufferFromBytes(IntPtr cryptoStatics, byte[] data)
    {
        IntPtr buffer = IntPtr.Zero;
        fixed (byte* p = data)
        {
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint, byte*, IntPtr*, int>)Vtbl(cryptoStatics)[9])(
                cryptoStatics, (uint)data.Length, p, &buffer);
            Check(hr, "CryptographicBuffer.CreateFromByteArray");
        }
        return buffer;
    }

    /// <summary>ISoftwareBitmapStatics.CreateCopyFromBuffer(IBuffer, BitmapPixelFormat, int, int)（slot 9）。</summary>
    public static IntPtr CreateSoftwareBitmap(IntPtr bitmapStatics, IntPtr buffer, int pixelFormat, int width, int height)
    {
        IntPtr bmp = IntPtr.Zero;
        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, int, int, IntPtr*, int>)Vtbl(bitmapStatics)[9])(
            bitmapStatics, buffer, pixelFormat, width, height, &bmp);
        Check(hr, "SoftwareBitmap.CreateCopyFromBuffer");
        return bmp;
    }

    /// <summary>IOcrEngineStatics.IsLanguageSupported(Language)（slot 8）。</summary>
    public static bool IsLanguageSupported(IntPtr ocrStatics, IntPtr language)
    {
        byte b = 0;
        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, byte*, int>)Vtbl(ocrStatics)[8])(ocrStatics, language, &b);
        Check(hr, "OcrEngine.IsLanguageSupported");
        return b != 0;
    }

    /// <summary>ILanguageFactory.CreateLanguage(HSTRING)（slot 6）。</summary>
    public static int CreateLanguage(IntPtr languageFactory, string tag, out IntPtr language)
    {
        IntPtr h = CreateHString(tag);
        try
        {
            IntPtr r = IntPtr.Zero;
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)Vtbl(languageFactory)[6])(languageFactory, h, &r);
            language = r;
            return hr;
        }
        finally
        {
            DeleteHString(h);
        }
    }
}
