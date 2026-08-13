using System.Runtime.InteropServices;

namespace Mv2Enabler;

internal sealed class ClipboardTextScope : IDisposable
{
    private const uint UnicodeText = 13;
    private const uint MoveableZeroInit = 0x0042;
    private readonly string? _originalText;

    public ClipboardTextScope(string text)
    {
        _originalText = ReadText();
        WriteText(text);
    }

    public void Dispose()
    {
        if (_originalText is not null)
        {
            WriteText(_originalText);
        }
    }

    private static string? ReadText()
    {
        if (!NativeMethods.IsClipboardFormatAvailable(UnicodeText) || !TryOpenClipboard())
        {
            return null;
        }

        try
        {
            var handle = NativeMethods.GetClipboardData(UnicodeText);
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            var pointer = NativeMethods.GlobalLock(handle);
            if (pointer == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return Marshal.PtrToStringUni(pointer);
            }
            finally
            {
                _ = NativeMethods.GlobalUnlock(handle);
            }
        }
        finally
        {
            _ = NativeMethods.CloseClipboard();
        }
    }

    private static void WriteText(string text)
    {
        if (!TryOpenClipboard())
        {
            throw NativeMethods.Error("OpenClipboard failed");
        }

        try
        {
            if (!NativeMethods.EmptyClipboard())
            {
                throw NativeMethods.Error("EmptyClipboard failed");
            }

            var byteCount = checked((text.Length + 1) * sizeof(char));
            var handle = NativeMethods.GlobalAlloc(MoveableZeroInit, checked((nuint)byteCount));
            if (handle == IntPtr.Zero)
            {
                throw NativeMethods.Error("GlobalAlloc failed for clipboard text");
            }

            var pointer = NativeMethods.GlobalLock(handle);
            if (pointer == IntPtr.Zero)
            {
                throw NativeMethods.Error("GlobalLock failed for clipboard text");
            }

            try
            {
                Marshal.Copy(text.ToCharArray(), 0, pointer, text.Length);
                Marshal.WriteInt16(pointer, text.Length * sizeof(char), 0);
            }
            finally
            {
                _ = NativeMethods.GlobalUnlock(handle);
            }

            if (NativeMethods.SetClipboardData(UnicodeText, handle) == IntPtr.Zero)
            {
                throw NativeMethods.Error("SetClipboardData failed");
            }
        }
        finally
        {
            _ = NativeMethods.CloseClipboard();
        }
    }

    private static bool TryOpenClipboard()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (NativeMethods.OpenClipboard(IntPtr.Zero))
            {
                return true;
            }

            Thread.Sleep(25);
        }

        return false;
    }
}
