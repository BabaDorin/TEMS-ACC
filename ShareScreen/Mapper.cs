using CommunicationLibrary.Models;
using HelpersLibrary.Helpers;
using System.Windows.Input;

namespace ShareScreen
{
    public static class Mapper
    {
        // Из MouseClickComm в MouseClick
        public static MouseClick Map(this MouseClickComm comm)
        {
            return comm switch
            {
                MouseClickComm.Left => MouseClick.Left,
                MouseClickComm.Middle => MouseClick.Middle,
                MouseClickComm.Right => MouseClick.Right,
                MouseClickComm.None => MouseClick.None,
                _ => MouseClick.None
            };
        }

        // Из KeyboardKeyComm в KeyboardKey
        public static KeyboardKey Map(this KeyboardKeyComm comm)
        {
            // Прямое преобразование enum по значению
            return (KeyboardKey)(int)comm;
        }

        // Из WPF MouseButton в MouseClickComm (для ScreenSharingWindow)
        public static MouseClickComm Map(this MouseButton button)
        {
            return button switch
            {
                MouseButton.Left => MouseClickComm.Left,
                MouseButton.Middle => MouseClickComm.Middle,
                MouseButton.Right => MouseClickComm.Right,
                _ => MouseClickComm.None
            };
        }

        // Из WPF Key в KeyboardKeyComm (для ScreenSharingWindow)
        public static KeyboardKeyComm Map(this Key key)
        {
            return key switch
            {
                Key.None => KeyboardKeyComm.NONAME,
                Key.Cancel => KeyboardKeyComm.CANCEL,
                Key.Back => KeyboardKeyComm.BACK,
                Key.Tab => KeyboardKeyComm.TAB,
                Key.Clear => KeyboardKeyComm.CLEAR,
                Key.Enter => KeyboardKeyComm.RETURN,
                Key.Pause => KeyboardKeyComm.PAUSE,
                Key.CapsLock => KeyboardKeyComm.CAPITAL,
                Key.Escape => KeyboardKeyComm.ESCAPE,
                Key.Space => KeyboardKeyComm.SPACE,
                Key.PageUp => KeyboardKeyComm.PRIOR,
                Key.PageDown => KeyboardKeyComm.NEXT,
                Key.End => KeyboardKeyComm.END,
                Key.Home => KeyboardKeyComm.HOME,
                Key.Left => KeyboardKeyComm.LEFT,
                Key.Up => KeyboardKeyComm.UP,
                Key.Right => KeyboardKeyComm.RIGHT,
                Key.Down => KeyboardKeyComm.DOWN,
                Key.Select => KeyboardKeyComm.SELECT,
                Key.Print => KeyboardKeyComm.PRINT,
                Key.Execute => KeyboardKeyComm.EXECUTE,
                Key.PrintScreen => KeyboardKeyComm.SNAPSHOT,
                Key.Insert => KeyboardKeyComm.INSERT,
                Key.Delete => KeyboardKeyComm.DELETE,
                Key.Help => KeyboardKeyComm.HELP,
                Key.D0 => KeyboardKeyComm.VK_0,
                Key.D1 => KeyboardKeyComm.VK_1,
                Key.D2 => KeyboardKeyComm.VK_2,
                Key.D3 => KeyboardKeyComm.VK_3,
                Key.D4 => KeyboardKeyComm.VK_4,
                Key.D5 => KeyboardKeyComm.VK_5,
                Key.D6 => KeyboardKeyComm.VK_6,
                Key.D7 => KeyboardKeyComm.VK_7,
                Key.D8 => KeyboardKeyComm.VK_8,
                Key.D9 => KeyboardKeyComm.VK_9,
                Key.A => KeyboardKeyComm.VK_A,
                Key.B => KeyboardKeyComm.VK_B,
                Key.C => KeyboardKeyComm.VK_C,
                Key.D => KeyboardKeyComm.VK_D,
                Key.E => KeyboardKeyComm.VK_E,
                Key.F => KeyboardKeyComm.VK_F,
                Key.G => KeyboardKeyComm.VK_G,
                Key.H => KeyboardKeyComm.VK_H,
                Key.I => KeyboardKeyComm.VK_I,
                Key.J => KeyboardKeyComm.VK_J,
                Key.K => KeyboardKeyComm.VK_K,
                Key.L => KeyboardKeyComm.VK_L,
                Key.M => KeyboardKeyComm.VK_M,
                Key.N => KeyboardKeyComm.VK_N,
                Key.O => KeyboardKeyComm.VK_O,
                Key.P => KeyboardKeyComm.VK_P,
                Key.Q => KeyboardKeyComm.VK_Q,
                Key.R => KeyboardKeyComm.VK_R,
                Key.S => KeyboardKeyComm.VK_S,
                Key.T => KeyboardKeyComm.VK_T,
                Key.U => KeyboardKeyComm.VK_U,
                Key.V => KeyboardKeyComm.VK_V,
                Key.W => KeyboardKeyComm.VK_W,
                Key.X => KeyboardKeyComm.VK_X,
                Key.Y => KeyboardKeyComm.VK_Y,
                Key.Z => KeyboardKeyComm.VK_Z,
                Key.LWin => KeyboardKeyComm.LWIN,
                Key.RWin => KeyboardKeyComm.RWIN,
                Key.Apps => KeyboardKeyComm.APPS,
                Key.Sleep => KeyboardKeyComm.SLEEP,
                Key.NumPad0 => KeyboardKeyComm.NUMPAD0,
                Key.NumPad1 => KeyboardKeyComm.NUMPAD1,
                Key.NumPad2 => KeyboardKeyComm.NUMPAD2,
                Key.NumPad3 => KeyboardKeyComm.NUMPAD3,
                Key.NumPad4 => KeyboardKeyComm.NUMPAD4,
                Key.NumPad5 => KeyboardKeyComm.NUMPAD5,
                Key.NumPad6 => KeyboardKeyComm.NUMPAD6,
                Key.NumPad7 => KeyboardKeyComm.NUMPAD7,
                Key.NumPad8 => KeyboardKeyComm.NUMPAD8,
                Key.NumPad9 => KeyboardKeyComm.NUMPAD9,
                Key.Multiply => KeyboardKeyComm.MULTIPLY,
                Key.Add => KeyboardKeyComm.ADD,
                Key.Separator => KeyboardKeyComm.SEPARATOR,
                Key.Subtract => KeyboardKeyComm.SUBTRACT,
                Key.Decimal => KeyboardKeyComm.DECIMAL,
                Key.Divide => KeyboardKeyComm.DIVIDE,
                Key.F1 => KeyboardKeyComm.F1,
                Key.F2 => KeyboardKeyComm.F2,
                Key.F3 => KeyboardKeyComm.F3,
                Key.F4 => KeyboardKeyComm.F4,
                Key.F5 => KeyboardKeyComm.F5,
                Key.F6 => KeyboardKeyComm.F6,
                Key.F7 => KeyboardKeyComm.F7,
                Key.F8 => KeyboardKeyComm.F8,
                Key.F9 => KeyboardKeyComm.F9,
                Key.F10 => KeyboardKeyComm.F10,
                Key.F11 => KeyboardKeyComm.F11,
                Key.F12 => KeyboardKeyComm.F12,
                Key.F13 => KeyboardKeyComm.F13,
                Key.F14 => KeyboardKeyComm.F14,
                Key.F15 => KeyboardKeyComm.F15,
                Key.F16 => KeyboardKeyComm.F16,
                Key.F17 => KeyboardKeyComm.F17,
                Key.F18 => KeyboardKeyComm.F18,
                Key.F19 => KeyboardKeyComm.F19,
                Key.F20 => KeyboardKeyComm.F20,
                Key.F21 => KeyboardKeyComm.F21,
                Key.F22 => KeyboardKeyComm.F22,
                Key.F23 => KeyboardKeyComm.F23,
                Key.F24 => KeyboardKeyComm.F24,
                Key.NumLock => KeyboardKeyComm.NUMLOCK,
                Key.Scroll => KeyboardKeyComm.SCROLL,
                Key.LeftShift => KeyboardKeyComm.LSHIFT,
                Key.RightShift => KeyboardKeyComm.RSHIFT,
                Key.LeftCtrl => KeyboardKeyComm.LCONTROL,
                Key.RightCtrl => KeyboardKeyComm.RCONTROL,
                Key.LeftAlt => KeyboardKeyComm.LMENU,
                Key.RightAlt => KeyboardKeyComm.RMENU,
                Key.BrowserBack => KeyboardKeyComm.BROWSER_BACK,
                Key.BrowserForward => KeyboardKeyComm.BROWSER_FORWARD,
                Key.BrowserRefresh => KeyboardKeyComm.BROWSER_REFRESH,
                Key.BrowserStop => KeyboardKeyComm.BROWSER_STOP,
                Key.BrowserSearch => KeyboardKeyComm.BROWSER_SEARCH,
                Key.BrowserFavorites => KeyboardKeyComm.BROWSER_FAVORITES,
                Key.BrowserHome => KeyboardKeyComm.BROWSER_HOME,
                Key.VolumeMute => KeyboardKeyComm.VOLUME_MUTE,
                Key.VolumeDown => KeyboardKeyComm.VOLUME_DOWN,
                Key.VolumeUp => KeyboardKeyComm.VOLUME_UP,
                Key.MediaNextTrack => KeyboardKeyComm.MEDIA_NEXT_TRACK,
                Key.MediaPreviousTrack => KeyboardKeyComm.MEDIA_PREV_TRACK,
                Key.MediaStop => KeyboardKeyComm.MEDIA_STOP,
                Key.MediaPlayPause => KeyboardKeyComm.MEDIA_PLAY_PAUSE,
                Key.LaunchMail => KeyboardKeyComm.LAUNCH_MAIL,
                Key.SelectMedia => KeyboardKeyComm.LAUNCH_MEDIA_SELECT,
                Key.LaunchApplication1 => KeyboardKeyComm.LAUNCH_APP1,
                Key.LaunchApplication2 => KeyboardKeyComm.LAUNCH_APP2,
                Key.OemSemicolon => KeyboardKeyComm.OEM_1,
                Key.OemPlus => KeyboardKeyComm.OEM_PLUS,
                Key.OemComma => KeyboardKeyComm.OEM_COMMA,
                Key.OemMinus => KeyboardKeyComm.OEM_MINUS,
                Key.OemPeriod => KeyboardKeyComm.OEM_PERIOD,
                Key.OemQuestion => KeyboardKeyComm.OEM_2,
                Key.OemTilde => KeyboardKeyComm.OEM_3,
                Key.OemOpenBrackets => KeyboardKeyComm.OEM_4,
                Key.OemPipe => KeyboardKeyComm.OEM_5,
                Key.OemCloseBrackets => KeyboardKeyComm.OEM_6,
                Key.OemQuotes => KeyboardKeyComm.OEM_7,
                Key.Oem8 => KeyboardKeyComm.OEM_8,
                Key.OemBackslash => KeyboardKeyComm.OEM_102,
                Key.ImeProcessed => KeyboardKeyComm.PROCESSKEY,
                Key.System => KeyboardKeyComm.LWIN,
                Key.Play => KeyboardKeyComm.PLAY,
                Key.Zoom => KeyboardKeyComm.ZOOM,
                Key.NoName => KeyboardKeyComm.NONAME,
                Key.Pa1 => KeyboardKeyComm.PA1,
                Key.OemClear => KeyboardKeyComm.OEM_CLEAR,
                _ => KeyboardKeyComm.NONAME
            };
        }
    }
}