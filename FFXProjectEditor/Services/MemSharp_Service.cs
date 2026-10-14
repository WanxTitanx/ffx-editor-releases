using Binarysharp.MSharp;
using Binarysharp.MSharp.Assembly.CallingConvention;
using FFXProjectEditor.Utils;
using System;
using System.Text;

namespace FFXProjectEditor.Services
{
    public class MemSharp_Service : SingletonBase<MemSharp_Service>
    {
        public MemorySharp MemSharp { get; set; }

        public bool IsAvailable()
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            if (!Process_Service.Instance.IsAlive)
            {
                return false;
            }

            // Opening/attaching can throw (process exited mid-call, access denied, bitness quirks).
            // Never let that bubble up into the read loop — callers treat false as "not attached".
            try
            {
                if (MemSharp == null || !MemSharp.IsRunning)
                {
                    MemSharp = new MemorySharp(Process_Service.Instance.GameProcess);
                }
                return MemSharp != null;
            }
            catch
            {
                MemSharp = null;
                return false;
            }
        }

        // True when the game process is found AND we hold a usable memory handle.
        public bool IsAttached => IsAvailable();

        private readonly Encoding _defaultEncoding = Encoding.UTF8;
        public T Read<T>(int offset, bool isRelative = true)
        {
            if (!IsAvailable()) return default(T);
            return MemSharp.Read<T>((IntPtr)offset, isRelative);
        }
        public T[] Read<T>(int offset, int count, bool isRelative = true)
        {
            if (!IsAvailable()) return default(T[]);
            return MemSharp.Read<T>((IntPtr)offset, count, isRelative);
        }
        public string ReadString(int offset, int strLength = 512, Encoding? enc = null, bool isRelative = true)
        {
            if (!IsAvailable()) return "";
            if (enc == null) enc = _defaultEncoding;
            return MemSharp.ReadString((IntPtr)offset, enc, isRelative, strLength);
        }

        public void Write(int offset, byte[] value, bool isRelative = true)
        {
            if (!IsAvailable()) return;
            MemSharp.Write((IntPtr)offset, value, isRelative);
        }
        public void Write<T>(int offset, T value, bool isRelative = true)
        {
            if (!IsAvailable()) return;
            MemSharp.Write((IntPtr)offset, value, isRelative);
        }
        public void Write<T>(int offset, T[] value, bool isRelative = true)
        {
            if (!IsAvailable()) return;
            MemSharp.Write((IntPtr)offset, value, isRelative);
        }
        public void WriteString(int offset, string text, Encoding? enc = null, bool isRelative = true)
        {
            if (!IsAvailable()) return;
            if (enc == null) enc = _defaultEncoding;
            MemSharp.WriteString((IntPtr)offset, text, enc, isRelative);
        }

        public T Execute<T>(int offset, CallingConventions callingConvention, bool isRelative = true, params object[] parameters)
        {
            if (!IsAvailable()) return default(T);
            IntPtr address = isRelative ? MemSharp.MakeAbsolute((IntPtr)offset) : (IntPtr)offset;
            return MemSharp.Assembly.Execute<T>(address, callingConvention, parameters);
        }

        public IntPtr Execute(int offset, CallingConventions callingConvention, bool isRelative = true, params object[] parameters)
        {
            if (!IsAvailable()) return IntPtr.Zero;
            IntPtr address = isRelative ? MemSharp.MakeAbsolute((IntPtr)offset) : (IntPtr)offset;
            return MemSharp.Assembly.Execute(address, callingConvention, parameters);
        }
    }
}
