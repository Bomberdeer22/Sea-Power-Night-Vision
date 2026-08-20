using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace SeaPowerNightVision
{
    /// <summary>
    /// Reads keyboard state in a way that works whichever input backend the game was built with.
    /// <para>
    /// Unity's legacy <see cref="Input"/> API throws <see cref="InvalidOperationException"/> when a
    /// project is configured for "Input System Package (New)" only. In that case this bridge falls
    /// back to reflecting over <c>UnityEngine.InputSystem.Keyboard</c>, so hotkeys keep working
    /// without the mod having to reference the package.
    /// </para>
    /// </summary>
    internal static class InputBridge
    {
        private enum Backend
        {
            Unknown,
            Legacy,
            InputSystem,
            Unavailable
        }

        private static Backend _backend = Backend.Unknown;

        // Reflected Input System members.
        private static PropertyInfo _keyboardCurrent;
        private static MethodInfo _keyboardIndexer;
        private static Type _keyEnumType;
        private static PropertyInfo _wasPressedThisFrame;
        private static PropertyInfo _isPressed;
        private static readonly Dictionary<KeyCode, object> KeyCache = new Dictionary<KeyCode, object>();

        /// <summary>Human-readable name of the active backend, for the startup log.</summary>
        public static string BackendName
        {
            get
            {
                EnsureBackend();
                return _backend.ToString();
            }
        }

        public static bool GetKeyDown(KeyCode key)
        {
            EnsureBackend();

            switch (_backend)
            {
                case Backend.Legacy:
                    return Input.GetKeyDown(key);
                case Backend.InputSystem:
                    return ReadKey(key, pressedThisFrame: true);
                default:
                    return false;
            }
        }

        public static bool GetKey(KeyCode key)
        {
            EnsureBackend();

            switch (_backend)
            {
                case Backend.Legacy:
                    return Input.GetKey(key);
                case Backend.InputSystem:
                    return ReadKey(key, pressedThisFrame: false);
                default:
                    return false;
            }
        }

        private static void EnsureBackend()
        {
            if (_backend != Backend.Unknown)
            {
                return;
            }

            try
            {
                // Throws if the legacy input manager is disabled for this build.
                Input.GetKeyDown(KeyCode.None);
                _backend = Backend.Legacy;
                return;
            }
            catch (Exception)
            {
                NightVisionPlugin.Log.LogWarning(
                    "Legacy Unity input is unavailable; falling back to the Input System package.");
            }

            _backend = TrySetUpInputSystem() ? Backend.InputSystem : Backend.Unavailable;

            if (_backend == Backend.Unavailable)
            {
                NightVisionPlugin.Log.LogError(
                    "No usable keyboard backend was found, so night vision hotkeys will not respond. " +
                    "Set EnabledOnStart=true (or AutoEnableAtNight=true) in the config as a workaround.");
            }
        }

        private static bool TrySetUpInputSystem()
        {
            try
            {
                var keyboardType = Type.GetType("UnityEngine.InputSystem.Keyboard, Unity.InputSystem", false);
                _keyEnumType = Type.GetType("UnityEngine.InputSystem.Key, Unity.InputSystem", false);

                if (keyboardType == null || _keyEnumType == null)
                {
                    return false;
                }

                _keyboardCurrent = keyboardType.GetProperty("current", BindingFlags.Public | BindingFlags.Static);
                _keyboardIndexer = keyboardType.GetMethod("get_Item", new[] { _keyEnumType });

                if (_keyboardCurrent == null || _keyboardIndexer == null)
                {
                    return false;
                }

                var buttonControl = Type.GetType("UnityEngine.InputSystem.Controls.ButtonControl, Unity.InputSystem", false);
                if (buttonControl == null)
                {
                    return false;
                }

                _wasPressedThisFrame = buttonControl.GetProperty("wasPressedThisFrame");
                _isPressed = buttonControl.GetProperty("isPressed");

                return _wasPressedThisFrame != null && _isPressed != null;
            }
            catch (Exception e)
            {
                NightVisionPlugin.Log.LogWarning("Could not set up the Input System fallback: " + e.Message);
                return false;
            }
        }

        private static bool ReadKey(KeyCode key, bool pressedThisFrame)
        {
            try
            {
                var keyboard = _keyboardCurrent.GetValue(null, null);
                if (keyboard == null)
                {
                    return false;
                }

                if (!KeyCache.TryGetValue(key, out var keyValue))
                {
                    keyValue = TranslateKey(key);
                    KeyCache[key] = keyValue;
                }

                if (keyValue == null)
                {
                    return false;
                }

                var control = _keyboardIndexer.Invoke(keyboard, new[] { keyValue });
                if (control == null)
                {
                    return false;
                }

                var property = pressedThisFrame ? _wasPressedThisFrame : _isPressed;
                return (bool)property.GetValue(control, null);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Maps a legacy <see cref="KeyCode"/> onto the Input System's <c>Key</c> enum.</summary>
        private static object TranslateKey(KeyCode key)
        {
            var name = key.ToString();

            // The two enums mostly agree; these are the names that differ.
            switch (key)
            {
                case KeyCode.LeftControl: name = "LeftCtrl"; break;
                case KeyCode.RightControl: name = "RightCtrl"; break;
                case KeyCode.Return: name = "Enter"; break;
                case KeyCode.KeypadEnter: name = "NumpadEnter"; break;
                case KeyCode.CapsLock: name = "CapsLock"; break;
                case KeyCode.Print: name = "PrintScreen"; break;
                case KeyCode.BackQuote: name = "Backquote"; break;
                case KeyCode.LeftApple: name = "LeftMeta"; break;
                case KeyCode.RightApple: name = "RightMeta"; break;
            }

            if (name.StartsWith("Alpha", StringComparison.Ordinal))
            {
                name = "Digit" + name.Substring(5);
            }
            else if (name.StartsWith("Keypad", StringComparison.Ordinal))
            {
                name = "Numpad" + name.Substring(6);
            }

            try
            {
                return Enum.Parse(_keyEnumType, name, true);
            }
            catch (Exception)
            {
                NightVisionPlugin.Log.LogWarning($"Key '{key}' has no Input System equivalent; that hotkey will not work.");
                return null;
            }
        }
    }
}
