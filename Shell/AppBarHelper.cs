using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AmbientSFXMachineGUI.Shell;

public enum AppBarEdge
{
    Float = -1,
    Left = 0,
    Top = 1,
    Right = 2,
    Bottom = 3
}

public sealed class AppBarHelper
{
    private const int ABM_NEW = 0x00000000;
    private const int ABM_REMOVE = 0x00000001;
    private const int ABM_QUERYPOS = 0x00000002;
    private const int ABM_SETPOS = 0x00000003;
    private const uint WM_APPBAR_CALLBACK = 0xA000;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public int uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    [DllImport("shell32.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern uint SHAppBarMessage(int dwMessage, ref APPBARDATA pData);

    private readonly Window _window;
    private bool _registered;
    private AppBarEdge _edge = AppBarEdge.Float;
    private double _floatWidth, _floatHeight, _floatLeft, _floatTop;

    public AppBarHelper(Window window)
    {
        _window = window;
    }

    public AppBarEdge Edge => _edge;

    public void SetEdge(AppBarEdge edge)
    {
        if (edge == _edge) return;

        if (_edge == AppBarEdge.Float)
        {
            _floatLeft = _window.Left;
            _floatTop = _window.Top;
            _floatWidth = _window.Width;
            _floatHeight = _window.Height;
        }

        if (_registered) Unregister();

        _edge = edge;

        if (edge == AppBarEdge.Float)
        {
            _window.Left = _floatLeft;
            _window.Top = _floatTop;
            _window.Width = _floatWidth > 0 ? _floatWidth : _window.Width;
            _window.Height = _floatHeight > 0 ? _floatHeight : _window.Height;
            return;
        }

        Register();
        Reposition();
    }

    private IntPtr Handle => new WindowInteropHelper(_window).Handle;

    private void Register()
    {
        var data = new APPBARDATA
        {
            cbSize = Marshal.SizeOf<APPBARDATA>(),
            hWnd = Handle,
            uCallbackMessage = WM_APPBAR_CALLBACK
        };
        SHAppBarMessage(ABM_NEW, ref data);
        _registered = true;
    }

    public void Unregister()
    {
        if (!_registered) return;
        var data = new APPBARDATA
        {
            cbSize = Marshal.SizeOf<APPBARDATA>(),
            hWnd = Handle
        };
        SHAppBarMessage(ABM_REMOVE, ref data);
        _registered = false;
    }

    private void Reposition()
    {
        var screen = SystemParameters.WorkArea;
        int thickness = _edge == AppBarEdge.Left || _edge == AppBarEdge.Right ? 72 : 48;

        var rc = new RECT();
        switch (_edge)
        {
            case AppBarEdge.Top:
                rc.left = (int)screen.Left; rc.right = (int)screen.Right;
                rc.top = (int)screen.Top; rc.bottom = (int)screen.Top + thickness;
                break;
            case AppBarEdge.Bottom:
                rc.left = (int)screen.Left; rc.right = (int)screen.Right;
                rc.top = (int)screen.Bottom - thickness; rc.bottom = (int)screen.Bottom;
                break;
            case AppBarEdge.Left:
                rc.top = (int)screen.Top; rc.bottom = (int)screen.Bottom;
                rc.left = (int)screen.Left; rc.right = (int)screen.Left + thickness;
                break;
            case AppBarEdge.Right:
                rc.top = (int)screen.Top; rc.bottom = (int)screen.Bottom;
                rc.left = (int)screen.Right - thickness; rc.right = (int)screen.Right;
                break;
        }

        var data = new APPBARDATA
        {
            cbSize = Marshal.SizeOf<APPBARDATA>(),
            hWnd = Handle,
            uEdge = (int)_edge,
            rc = rc
        };
        SHAppBarMessage(ABM_QUERYPOS, ref data);
        SHAppBarMessage(ABM_SETPOS, ref data);

        _window.Left = data.rc.left;
        _window.Top = data.rc.top;
        _window.Width = Math.Max(1, data.rc.right - data.rc.left);
        _window.Height = Math.Max(1, data.rc.bottom - data.rc.top);
    }
}
