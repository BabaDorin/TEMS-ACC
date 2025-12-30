using System;
using System.Runtime.InteropServices;
using WindowsInput;
using WindowsInput.Native;

namespace HelpersLibrary.Helpers
{
    public class WindowsInputHelper
    {
        private static readonly InputSimulator simulator = new InputSimulator();

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, int dwExtraInfo);

        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;

        public static void SwitchToInputDesktop()
        {
            var s = PInvoke.OpenInputDesktop(0, false, PInvoke.ACCESS_MASK.MAXIMUM_ALLOWED);
            PInvoke.SetThreadDesktop(s);
            PInvoke.CloseDesktop(s);
        }

        public static void MouseMove(int x, int y)
        {
            SwitchToInputDesktop();
            System.Windows.Forms.Cursor.Position = new System.Drawing.Point(x, y);
        }

        // ПЕРЕИМЕНОВАНО!
        public static void PerformMouseClick(MouseClick button)
        {
            SwitchToInputDesktop();
            switch (button)
            {
                case MouseClick.Left:
                    simulator.Mouse.LeftButtonClick();
                    break;
                case MouseClick.Middle:
                    mouse_event(MOUSEEVENTF_MIDDLEDOWN, 0, 0, 0, 0);
                    mouse_event(MOUSEEVENTF_MIDDLEUP, 0, 0, 0, 0);
                    break;
                case MouseClick.Right:
                    simulator.Mouse.RightButtonClick();
                    break;
            }
        }

        public static void MouseDown(MouseClick button)
        {
            SwitchToInputDesktop();
            switch (button)
            {
                case MouseClick.Left:
                    simulator.Mouse.LeftButtonDown();
                    break;
                case MouseClick.Middle:
                    mouse_event(MOUSEEVENTF_MIDDLEDOWN, 0, 0, 0, 0);
                    break;
                case MouseClick.Right:
                    simulator.Mouse.RightButtonDown();
                    break;
            }
        }

        public static void MouseUp(MouseClick button)
        {
            SwitchToInputDesktop();
            switch (button)
            {
                case MouseClick.Left:
                    simulator.Mouse.LeftButtonUp();
                    break;
                case MouseClick.Middle:
                    mouse_event(MOUSEEVENTF_MIDDLEUP, 0, 0, 0, 0);
                    break;
                case MouseClick.Right:
                    simulator.Mouse.RightButtonUp();
                    break;
            }
        }

        public static void MouseDoubleClick(MouseClick button)
        {
            SwitchToInputDesktop();
            switch (button)
            {
                case MouseClick.Left:
                    simulator.Mouse.LeftButtonDoubleClick();
                    break;
                case MouseClick.Middle:
                    mouse_event(MOUSEEVENTF_MIDDLEDOWN, 0, 0, 0, 0);
                    mouse_event(MOUSEEVENTF_MIDDLEUP, 0, 0, 0, 0);
                    mouse_event(MOUSEEVENTF_MIDDLEDOWN, 0, 0, 0, 0);
                    mouse_event(MOUSEEVENTF_MIDDLEUP, 0, 0, 0, 0);
                    break;
                case MouseClick.Right:
                    simulator.Mouse.RightButtonDoubleClick();
                    break;
            }
        }

        public static void KeyDown(KeyboardKey key)
        {
            SwitchToInputDesktop();
            simulator.Keyboard.KeyDown(key.Map());
        }

        public static void KeyPress(KeyboardKey key)
        {
            SwitchToInputDesktop();
            simulator.Keyboard.KeyPress(key.Map());
        }

        public static void KeyUp(KeyboardKey key)
        {
            SwitchToInputDesktop();
            simulator.Keyboard.KeyUp(key.Map());
        }
    }
}