using System;
using System.Runtime.InteropServices;

namespace YouTubeMp3
{
    internal sealed class Mp3Tags : IDisposable
    {
        private static readonly PropertyKey Title = GetKey("System.Title");
        private static readonly PropertyKey Artist = GetKey("System.Music.Artist");
        private static readonly PropertyKey Genre = GetKey("System.Music.Genre");
        private IPropertyStore store;

        internal Mp3Tags(string path, bool writable)
        {
            var propertyStoreId = typeof(IPropertyStore).GUID;
            SHGetPropertyStoreFromParsingName(path, IntPtr.Zero, writable ? 2u : 0u, ref propertyStoreId, out store);
        }

        internal string ReadTitle() { return Read(Title); }
        internal string ReadArtist() { return Read(Artist); }
        internal string ReadGenre() { return Read(Genre); }

        internal void Save(string title, string artist, string genre)
        {
            if (title != null) Write(Title, title, false);
            if (artist != null) Write(Artist, artist, true);
            if (!string.IsNullOrWhiteSpace(genre)) Write(Genre, genre, true);
            store.Commit();
        }

        private string Read(PropertyKey key)
        {
            PropVariant value;
            store.GetValue(ref key, out value);
            try
            {
                if (value.Type == 0) return string.Empty;
                var text = PropVariantToStringAlloc(ref value);
                try { return Marshal.PtrToStringUni(text) ?? string.Empty; }
                finally { Marshal.FreeCoTaskMem(text); }
            }
            finally { PropVariantClear(ref value); }
        }

        private void Write(PropertyKey key, string text, bool vector)
        {
            PropVariant value;
            if (text.Length == 0) value = new PropVariant();
            else if (vector) InitPropVariantFromStringVector(new[] { text }, 1, out value);
            else value = new PropVariant { Type = 31, Pointer = Marshal.StringToCoTaskMemUni(text) };
            try { store.SetValue(ref key, ref value); }
            finally { PropVariantClear(ref value); }
        }

        private static PropertyKey GetKey(string name)
        {
            PropertyKey key;
            PSGetPropertyKeyFromName(name, out key);
            return key;
        }

        public void Dispose()
        {
            if (store == null) return;
            Marshal.ReleaseComObject(store);
            store = null;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct PropertyKey
        {
            internal Guid Format;
            internal uint Id;
        }

        [StructLayout(LayoutKind.Explicit, Size = 24)]
        private struct PropVariant
        {
            [FieldOffset(0)] internal ushort Type;
            [FieldOffset(8)] internal IntPtr Pointer;
        }

        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            void GetCount(out uint count);
            void GetAt(uint index, out PropertyKey key);
            void GetValue(ref PropertyKey key, out PropVariant value);
            void SetValue(ref PropertyKey key, ref PropVariant value);
            void Commit();
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHGetPropertyStoreFromParsingName(string path, IntPtr bindContext, uint flags,
            ref Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

        [DllImport("propsys.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void PSGetPropertyKeyFromName(string canonicalName, out PropertyKey key);

        [DllImport("propsys.dll", PreserveSig = false)]
        private static extern void InitPropVariantFromStringVector(
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] strings,
            uint count, out PropVariant value);

        [DllImport("propsys.dll", PreserveSig = false)]
        private static extern IntPtr PropVariantToStringAlloc(ref PropVariant value);

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PropVariant value);
    }
}
