// Lumora Setup - Windows interop: modern folder picker (IFileOpenDialog), shortcuts (IShellLink), DWM window look.
using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace Lumora.Setup
{
    internal static class Native
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        /// Rounded corners, dark frame and a border in the surface colour (Windows 11; ignored elsewhere).
        public static void StyleWindow(IntPtr hwnd)
        {
            try
            {
                int dark = 1;
                DwmSetWindowAttribute(hwnd, 20, ref dark, 4);
                int round = 2;
                DwmSetWindowAttribute(hwnd, 33, ref round, 4);
                int border = (T.Border.B << 16) | (T.Border.G << 8) | T.Border.R;
                DwmSetWindowAttribute(hwnd, 34, ref border, 4);
                int caption = (T.Bg.B << 16) | (T.Bg.G << 8) | T.Bg.R;
                DwmSetWindowAttribute(hwnd, 35, ref caption, 4);
            }
            catch (Exception)
            {
            }
        }

        // ---- folder picker

        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        private class FileOpenDialogRcw
        {
        }

        [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            [PreserveSig]
            int Show(IntPtr parent);
            void SetFileTypes(uint count, IntPtr types);
            void SetFileTypeIndex(uint index);
            void GetFileTypeIndex(out uint index);
            void Advise(IntPtr events, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint options);
            void GetOptions(out uint options);
            void SetDefaultFolder(IShellItem item);
            void SetFolder(IShellItem item);
            void GetFolder(out IShellItem item);
            void GetCurrentSelection(out IShellItem item);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IShellItem item);
            void AddPlace(IShellItem item, int place);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string ext);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr filter);
            void GetResults(out IntPtr items);
            void GetSelectedItems(out IntPtr items);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem item);
            void GetDisplayName(uint sigdn, [MarshalAs(UnmanagedType.LPWStr)] out string name);
            void GetAttributes(uint mask, out uint attributes);
            void Compare(IShellItem item, uint hint, out int order);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, out IShellItem item);

        /// Explorer-style folder dialog; null when cancelled.
        public static string PickFolder(IntPtr owner, string title, string start)
        {
            var dialog = (IFileOpenDialog)new FileOpenDialogRcw();
            try
            {
                dialog.SetOptions(0x20 | 0x40 | 0x800); // PICKFOLDERS | FORCEFILESYSTEM | PATHMUSTEXIST
                dialog.SetTitle(title);
                dialog.SetOkButtonLabel("Ordner wählen");
                string dir = start;
                while (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                {
                    dir = System.IO.Path.GetDirectoryName(dir);
                }
                if (!string.IsNullOrEmpty(dir))
                {
                    try
                    {
                        Guid iid = typeof(IShellItem).GUID;
                        IShellItem folder;
                        SHCreateItemFromParsingName(dir, IntPtr.Zero, ref iid, out folder);
                        dialog.SetFolder(folder);
                    }
                    catch (Exception)
                    {
                    }
                }
                if (dialog.Show(owner) != 0)
                {
                    return null;
                }
                IShellItem result;
                dialog.GetResult(out result);
                string path;
                result.GetDisplayName(0x80058000, out path); // SIGDN_FILESYSPATH
                return path;
            }
            finally
            {
                Marshal.ReleaseComObject(dialog);
            }
        }

        // ---- shortcuts

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLinkRcw
        {
        }

        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int max, IntPtr data, uint flags);
            void GetIDList(out IntPtr pidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int max);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int max);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int max);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
            void GetHotkey(out short key);
            void SetHotkey(short key);
            void GetShowCmd(out int cmd);
            void SetShowCmd(int cmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int max, out int index);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
            void Resolve(IntPtr hwnd, uint flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
        }

        public static void CreateShortcut(string lnk, string target, string description)
        {
            var link = (IShellLinkW)new ShellLinkRcw();
            try
            {
                link.SetPath(target);
                link.SetWorkingDirectory(System.IO.Path.GetDirectoryName(target));
                link.SetDescription(description);
                link.SetIconLocation(target, 0);
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(lnk));
                ((IPersistFile)link).Save(lnk, true);
            }
            finally
            {
                Marshal.ReleaseComObject(link);
            }
        }

        /// Target of a .lnk, or null.
        public static string ShortcutTarget(string lnk)
        {
            if (!System.IO.File.Exists(lnk))
            {
                return null;
            }
            var link = (IShellLinkW)new ShellLinkRcw();
            try
            {
                ((IPersistFile)link).Load(lnk, 0);
                var sb = new StringBuilder(1024);
                link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0x4); // SLGP_RAWPATH
                return sb.ToString();
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                Marshal.ReleaseComObject(link);
            }
        }

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

        /// Explorer refreshes icons (after creating or removing shortcuts).
        public static void NotifyShell()
        {
            try
            {
                SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); // SHCNE_ASSOCCHANGED
            }
            catch (Exception)
            {
            }
        }
    }
}
