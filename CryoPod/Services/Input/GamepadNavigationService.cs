using System;
using System.Linq;
using Microsoft.UI.Dispatching;
using Windows.Gaming.Input;

namespace CryoPod.Services.Input
{
    internal sealed class GamepadNavigationService : IDisposable
    {
        private const double ThumbstickDeadzone = 0.55;
        private static readonly TimeSpan PollingInterval = TimeSpan.FromMilliseconds(50);
        private static readonly TimeSpan InitialRepeatDelay = TimeSpan.FromMilliseconds(320);
        private static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(140);

        private readonly DispatcherQueueTimer _pollingTimer;
        private NavigationDirection _lastNavigationInput = NavigationDirection.None;
        private DateTimeOffset _nextAllowedNavigationTime = DateTimeOffset.MinValue;
        private bool _wasConfirmPressed;
        private bool _wasBackPressed;

        public GamepadNavigationService(DispatcherQueue dispatcherQueue)
        {
            _pollingTimer = dispatcherQueue.CreateTimer();
            _pollingTimer.Interval = PollingInterval;
            _pollingTimer.Tick += PollingTimer_Tick;
            _pollingTimer.Start();
        }

        public event EventHandler<NavigationDirection>? NavigationRequested;
        public event EventHandler? ConfirmRequested;
        public event EventHandler? BackRequested;

        public void Dispose()
        {
            _pollingTimer.Stop();
            _pollingTimer.Tick -= PollingTimer_Tick;
        }

        private void PollingTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            var connectedGamepad = Gamepad.Gamepads.FirstOrDefault();
            if (connectedGamepad is null)
            {
                ResetRepeat();
                ResetButtons();
                return;
            }

            var reading = connectedGamepad.GetCurrentReading();
            ProcessActionButtons(reading);

            var navigationInput = GetNavigationInput(reading);
            if (navigationInput == NavigationDirection.None)
            {
                ResetRepeat();
                return;
            }

            var now = DateTimeOffset.UtcNow;
            var isNewPress = navigationInput != _lastNavigationInput;
            if (!isNewPress && now < _nextAllowedNavigationTime)
            {
                return;
            }

            NavigationRequested?.Invoke(this, navigationInput);

            _lastNavigationInput = navigationInput;
            _nextAllowedNavigationTime = now + (isNewPress ? InitialRepeatDelay : RepeatInterval);
        }

        private void ResetRepeat()
        {
            _lastNavigationInput = NavigationDirection.None;
            _nextAllowedNavigationTime = DateTimeOffset.MinValue;
        }

        private void ResetButtons()
        {
            _wasConfirmPressed = false;
            _wasBackPressed = false;
        }

        private void ProcessActionButtons(GamepadReading reading)
        {
            var isConfirmPressed = reading.Buttons.HasFlag(GamepadButtons.A);
            var isBackPressed = reading.Buttons.HasFlag(GamepadButtons.B);

            if (isConfirmPressed && !_wasConfirmPressed)
            {
                ConfirmRequested?.Invoke(this, EventArgs.Empty);
            }

            if (isBackPressed && !_wasBackPressed)
            {
                BackRequested?.Invoke(this, EventArgs.Empty);
            }

            _wasConfirmPressed = isConfirmPressed;
            _wasBackPressed = isBackPressed;
        }

        private static NavigationDirection GetNavigationInput(GamepadReading reading)
        {
            if (reading.Buttons.HasFlag(GamepadButtons.DPadLeft) || reading.LeftThumbstickX <= -ThumbstickDeadzone)
            {
                return NavigationDirection.Left;
            }

            if (reading.Buttons.HasFlag(GamepadButtons.DPadRight) || reading.LeftThumbstickX >= ThumbstickDeadzone)
            {
                return NavigationDirection.Right;
            }

            if (reading.Buttons.HasFlag(GamepadButtons.DPadUp) || reading.LeftThumbstickY >= ThumbstickDeadzone)
            {
                return NavigationDirection.Up;
            }

            if (reading.Buttons.HasFlag(GamepadButtons.DPadDown) || reading.LeftThumbstickY <= -ThumbstickDeadzone)
            {
                return NavigationDirection.Down;
            }

            return NavigationDirection.None;
        }
    }
}
