using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace NesLifter.Imaging;

/// <summary>
/// Replaces the icon of a Windows executable in place.
///
/// Windows reads an executable's icon from its PE resource section, so an icon
/// cannot simply be dropped next to the file: the <c>.ico</c> has to be
/// expanded into one <c>RT_ICON</c> resource per size plus one
/// <c>RT_GROUP_ICON</c> directory resource. That is exactly what the Win32
/// resource API does, so this uses <c>BeginUpdateResource</c> /
/// <c>UpdateResource</c> rather than hand-editing PE headers.
///
/// The existing icon resources are removed first, otherwise Explorer keeps
/// showing the old one because it resolves the lowest-numbered group.
/// </summary>
public static class PeIcon
{
    private const int RtIcon = 3;
    private const int RtGroupIcon = 14;

    private const uint LoadLibraryAsDatafile = 0x00000002;
    private const uint LoadLibraryAsImageResource = 0x00000020;

    /// <summary>
    /// Sets <paramref name="exePath"/>'s icon from an ICO file.
    /// Returns false when the executable cannot be updated (read-only, locked,
    /// not a PE image) instead of throwing, because packaging must not fail
    /// over a cosmetic detail.
    /// </summary>
    public static bool Apply(string exePath, string icoPath, out string error)
    {
        error = string.Empty;

        try
        {
            if (!File.Exists(exePath))
            {
                error = "Executable not found: " + exePath;
                return false;
            }

            if (!File.Exists(icoPath))
            {
                error = "Icon not found: " + icoPath;
                return false;
            }

            List<IcoImage> images = ReadIco(icoPath);

            if (images.Count == 0)
            {
                error = "Icon contains no images.";
                return false;
            }

            // Resource IDs must be unique; start above whatever is there.
            RemoveExistingIcons(exePath);

            IntPtr update = BeginUpdateResourceW(exePath, false);

            if (update == IntPtr.Zero)
            {
                error = "BeginUpdateResource failed (error " + Marshal.GetLastWin32Error() + ").";
                return false;
            }

            bool ok = true;
            int nextId = 1;

            try
            {
                foreach (IcoImage image in images)
                {
                    if (!UpdateResourceW(update, MakeIntResource(RtIcon), MakeIntResource(nextId),
                            MakeLangId(0, 0), image.Data, (uint)image.Data.Length))
                    {
                        ok = false;
                        error = "UpdateResource(RT_ICON " + nextId + ") failed (error " +
                                Marshal.GetLastWin32Error() + ").";
                        break;
                    }

                    nextId++;
                }

                if (ok)
                {
                    byte[] group = BuildGroupIcon(images);

                    if (!UpdateResourceW(update, MakeIntResource(RtGroupIcon), MakeIntResource(1),
                            MakeLangId(0, 0), group, (uint)group.Length))
                    {
                        ok = false;
                        error = "UpdateResource(RT_GROUP_ICON) failed (error " +
                                Marshal.GetLastWin32Error() + ").";
                    }
                }
            }
            finally
            {
                // Discarding on failure leaves the file untouched.
                if (!EndUpdateResourceW(update, !ok) && ok)
                {
                    ok = false;
                    error = "EndUpdateResource failed (error " + Marshal.GetLastWin32Error() + ").";
                }
            }

            return ok;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Deletes every existing icon resource so the new set is the only one
    /// Windows can find.
    /// </summary>
    private static void RemoveExistingIcons(string exePath)
    {
        IntPtr module = LoadLibraryExW(exePath, IntPtr.Zero, LoadLibraryAsDatafile | LoadLibraryAsImageResource);

        if (module == IntPtr.Zero)
            return;

        try
        {
            List<IntPtr> ids = new List<IntPtr>();

            foreach (int type in new[] { RtIcon, RtGroupIcon })
            {
                EnumResourceNamesW(module, MakeIntResource(type), (hModule, lpszType, lpszName, lParam) =>
                {
                    ids.Add(lpszName);
                    return true;
                }, IntPtr.Zero);
            }

            if (ids.Count == 0)
                return;

            IntPtr update = BeginUpdateResourceW(exePath, false);

            if (update == IntPtr.Zero)
                return;

            try
            {
                // Re-enumerate through the update handle: name handles from the
                // datafile module are not valid here, so walk by ordinal.
                for (int id = 1; id <= 64; id++)
                {
                    foreach (int type in new[] { RtIcon, RtGroupIcon })
                        UpdateResourceW(update, MakeIntResource(type), MakeIntResource(id),
                            MakeLangId(0, 0), IntPtr.Zero, 0);
                }
            }
            finally
            {
                EndUpdateResourceW(update, false);
            }
        }
        finally
        {
            FreeLibrary(module);
        }
    }

    private static byte[] BuildGroupIcon(List<IcoImage> images)
    {
        // GRPICONDIR: reserved(2) type(2) count(2), then GRPICONDIRENTRY[count]
        // Each entry is 14 bytes: w,h,colors,reserved,planes,bpp,size,id
        using MemoryStream ms = new MemoryStream();
        using BinaryWriter bw = new BinaryWriter(ms);

        bw.Write((ushort)0);
        bw.Write((ushort)1);
        bw.Write((ushort)images.Count);

        int id = 1;

        foreach (IcoImage image in images)
        {
            bw.Write((byte)(image.Width >= 256 ? 0 : image.Width));
            bw.Write((byte)(image.Height >= 256 ? 0 : image.Height));
            bw.Write((byte)0);
            bw.Write((byte)0);
            bw.Write((ushort)1);
            bw.Write((ushort)32);
            bw.Write(image.Data.Length);
            bw.Write((ushort)id);

            id++;
        }

        bw.Flush();
        return ms.ToArray();
    }

    private sealed class IcoImage
    {
        public int Width;
        public int Height;
        public byte[] Data = Array.Empty<byte>();
    }

    private static List<IcoImage> ReadIco(string path)
    {
        List<IcoImage> images = new List<IcoImage>();
        byte[] bytes = File.ReadAllBytes(path);

        if (bytes.Length < 6)
            return images;

        int count = bytes[4] | (bytes[5] << 8);

        for (int i = 0; i < count; i++)
        {
            int entry = 6 + i * 16;

            if (entry + 16 > bytes.Length)
                break;

            int width = bytes[entry] == 0 ? 256 : bytes[entry];
            int height = bytes[entry + 1] == 0 ? 256 : bytes[entry + 1];
            int size = BitConverter.ToInt32(bytes, entry + 8);
            int offset = BitConverter.ToInt32(bytes, entry + 12);

            if (size <= 0 || offset < 0 || offset + size > bytes.Length)
                continue;

            byte[] data = new byte[size];
            Array.Copy(bytes, offset, data, 0, size);

            images.Add(new IcoImage { Width = width, Height = height, Data = data });
        }

        return images;
    }

    private static IntPtr MakeIntResource(int id)
    {
        // MAKEINTRESOURCE: a pointer whose low word is the ordinal.
        return new IntPtr(id);
    }

    private static ushort MakeLangId(ushort primary, ushort sub)
    {
        return (ushort)(primary | (sub << 10));
    }

    private delegate bool EnumResNameProc(IntPtr hModule, IntPtr lpszType, IntPtr lpszName, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr BeginUpdateResourceW(string fileName, bool deleteExistingResources);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UpdateResourceW(
        IntPtr update, IntPtr type, IntPtr name, ushort language, byte[]? data, uint dataSize);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UpdateResourceW(
        IntPtr update, IntPtr type, IntPtr name, ushort language, IntPtr data, uint dataSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool EndUpdateResourceW(IntPtr update, bool discard);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryExW(string fileName, IntPtr file, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr module);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumResourceNamesW(
        IntPtr module, IntPtr type, EnumResNameProc callback, IntPtr param);
}
