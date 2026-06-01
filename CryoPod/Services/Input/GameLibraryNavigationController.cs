using System;
using System;
using System.Threading.Tasks;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using VirtualKey = Windows.System.VirtualKey;

namespace CryoPod.Services.Input
{
    internal sealed class GameLibraryNavigationController : IDisposable
    {
        private readonly UIElement _inputRoot;
        private readonly GridView _gridView;
        private readonly Button _exitYesButton;
        private readonly Button _exitNoButton;
        private readonly Func<bool>? _canProcessInput;
        private readonly GamepadNavigationService _gamepadNavigationService;
        private bool _isExitPromptActive;

        public GameLibraryNavigationController(
            UIElement inputRoot,
            GridView gridView,
            Button exitYesButton,
            Button exitNoButton,
            DispatcherQueue dispatcherQueue,
            Func<bool>? canProcessInput = null)
        {
            _inputRoot = inputRoot;
            _gridView = gridView;
            _exitYesButton = exitYesButton;
            _exitNoButton = exitNoButton;
            _canProcessInput = canProcessInput;
            _gamepadNavigationService = new GamepadNavigationService(dispatcherQueue);
            _gamepadNavigationService.NavigationRequested += GamepadNavigationService_NavigationRequested;
            _gamepadNavigationService.ConfirmRequested += GamepadNavigationService_ConfirmRequested;
            _gamepadNavigationService.BackRequested += GamepadNavigationService_BackRequested;
            _inputRoot.PreviewKeyDown += InputRoot_PreviewKeyDown;
        }

        public event EventHandler? ExitRequested;
        public event EventHandler? ExitConfirmed;
        public event EventHandler? ExitCanceled;

        public async Task FocusFirstItemAsync()
        {
            if (_gridView.Items.Count == 0)
            {
                return;
            }

            _gridView.UpdateLayout();
            _gridView.ScrollIntoView(_gridView.Items[0]);

            await Task.Yield();

            if (_gridView.ContainerFromIndex(0) is GridViewItem firstItem)
            {
                firstItem.Focus(FocusState.Programmatic);
                return;
            }

            _gridView.Focus(FocusState.Programmatic);
        }

        public async Task ActivateExitPromptAsync()
        {
            _isExitPromptActive = true;
            await FocusExitPromptButtonAsync(_exitNoButton);
        }

        public async Task DeactivateExitPromptAsync()
        {
            _isExitPromptActive = false;
            await FocusFirstItemAsync();
        }

        public void Dispose()
        {
            _inputRoot.PreviewKeyDown -= InputRoot_PreviewKeyDown;
            _gamepadNavigationService.NavigationRequested -= GamepadNavigationService_NavigationRequested;
            _gamepadNavigationService.ConfirmRequested -= GamepadNavigationService_ConfirmRequested;
            _gamepadNavigationService.BackRequested -= GamepadNavigationService_BackRequested;
            _gamepadNavigationService.Dispose();
        }

        private void InputRoot_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (!CanProcessInput())
            {
                return;
            }

            if (e.Key is VirtualKey.Escape)
            {
                e.Handled = true;
                HandleBackRequested();
                return;
            }

            if (_isExitPromptActive && e.Key is VirtualKey.Enter or VirtualKey.Space)
            {
                e.Handled = true;
                HandleConfirmRequested();
                return;
            }

            var direction = e.Key switch
            {
                VirtualKey.Left => NavigationDirection.Left,
                VirtualKey.Right => NavigationDirection.Right,
                VirtualKey.Up => NavigationDirection.Up,
                VirtualKey.Down => NavigationDirection.Down,
                _ => NavigationDirection.None,
            };

            if (direction == NavigationDirection.None)
            {
                return;
            }

            e.Handled = true;
            HandleNavigation(direction);
        }

        private void GamepadNavigationService_NavigationRequested(object? sender, NavigationDirection direction)
        {
            if (!CanProcessInput())
            {
                return;
            }

            HandleNavigation(direction);
        }

        private void GamepadNavigationService_ConfirmRequested(object? sender, EventArgs e)
        {
            if (!CanProcessInput())
            {
                return;
            }

            HandleConfirmRequested();
        }

        private void GamepadNavigationService_BackRequested(object? sender, EventArgs e)
        {
            if (!CanProcessInput())
            {
                return;
            }

            HandleBackRequested();
        }

        private void HandleNavigation(NavigationDirection direction)
        {
            if (_isExitPromptActive)
            {
                HandleExitPromptNavigation(direction);
                return;
            }

            if (_gridView.Items.Count == 0)
            {
                return;
            }

            if (FocusManager.GetFocusedElement(_gridView.XamlRoot) is not GridViewItem)
            {
                _ = FocusFirstItemAsync();
                return;
            }

            var focusDirection = direction switch
            {
                NavigationDirection.Left => FocusNavigationDirection.Left,
                NavigationDirection.Right => FocusNavigationDirection.Right,
                NavigationDirection.Up => FocusNavigationDirection.Up,
                NavigationDirection.Down => FocusNavigationDirection.Down,
                _ => (FocusNavigationDirection?)null,
            };

            if (focusDirection is null)
            {
                return;
            }

            FocusManager.TryMoveFocus(focusDirection.Value, new FindNextElementOptions
            {
                SearchRoot = _gridView,
            });
        }

        private void HandleExitPromptNavigation(NavigationDirection direction)
        {
            var targetButton = direction switch
            {
                NavigationDirection.Left => _exitYesButton,
                NavigationDirection.Up => _exitYesButton,
                NavigationDirection.Right => _exitNoButton,
                NavigationDirection.Down => _exitNoButton,
                _ => _exitNoButton,
            };

            _ = FocusExitPromptButtonAsync(targetButton);
        }

        private void HandleConfirmRequested()
        {
            if (!_isExitPromptActive)
            {
                return;
            }

            var focusedElement = FocusManager.GetFocusedElement(_gridView.XamlRoot);
            if (ReferenceEquals(focusedElement, _exitYesButton))
            {
                ExitConfirmed?.Invoke(this, EventArgs.Empty);
                return;
            }

            ExitCanceled?.Invoke(this, EventArgs.Empty);
        }

        private void HandleBackRequested()
        {
            if (_isExitPromptActive)
            {
                ExitCanceled?.Invoke(this, EventArgs.Empty);
                return;
            }

            ExitRequested?.Invoke(this, EventArgs.Empty);
        }

        private async Task FocusExitPromptButtonAsync(Button button)
        {
            await Task.Yield();
            button.Focus(FocusState.Programmatic);
        }

        private bool CanProcessInput()
        {
            return _canProcessInput?.Invoke() != false;
        }
    }
}
