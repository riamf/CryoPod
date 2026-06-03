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
    internal sealed class GameLibraryItemInvokedEventArgs(object? item) : EventArgs
    {
        public object? Item { get; } = item;
    }

    internal sealed class GameLibraryNavigationController : IDisposable
    {
        private readonly UIElement _inputRoot;
        private readonly GridView _gridView;
        private readonly Button _detailsBackButton;
        private readonly ListView _detailsScreenshotsListView;
        private readonly Button _exitYesButton;
        private readonly Button _exitNoButton;
        private readonly Func<bool>? _canProcessInput;
        private readonly GamepadNavigationService _gamepadNavigationService;
        private bool _isExitPromptActive;
        private bool _isDetailsViewActive;
        private int _lastFocusedItemIndex;
        private int _focusedDetailsScreenshotIndex = -1;

        public GameLibraryNavigationController(
            UIElement inputRoot,
            GridView gridView,
            Button detailsBackButton,
            ListView detailsScreenshotsListView,
            Button exitYesButton,
            Button exitNoButton,
            DispatcherQueue dispatcherQueue,
            Func<bool>? canProcessInput = null)
        {
            _inputRoot = inputRoot;
            _gridView = gridView;
            _detailsBackButton = detailsBackButton;
            _detailsScreenshotsListView = detailsScreenshotsListView;
            _exitYesButton = exitYesButton;
            _exitNoButton = exitNoButton;
            _canProcessInput = canProcessInput;
            _gamepadNavigationService = new GamepadNavigationService(dispatcherQueue);
            _gamepadNavigationService.NavigationRequested += GamepadNavigationService_NavigationRequested;
            _gamepadNavigationService.ConfirmRequested += GamepadNavigationService_ConfirmRequested;
            _gamepadNavigationService.BackRequested += GamepadNavigationService_BackRequested;
            _inputRoot.PreviewKeyDown += InputRoot_PreviewKeyDown;
            _lastFocusedItemIndex = 0;
        }

        public event EventHandler? ExitRequested;
        public event EventHandler? ExitConfirmed;
        public event EventHandler? ExitCanceled;
        public event EventHandler<GameLibraryItemInvokedEventArgs>? DetailsRequested;
        public event EventHandler? DetailsClosed;

        public async Task FocusFirstItemAsync()
        {
            await FocusGridItemAsync(0);
        }

        public async Task ActivateExitPromptAsync()
        {
            CaptureFocusedGridItemIndex();
            _isExitPromptActive = true;
            await FocusExitPromptButtonAsync(_exitNoButton);
        }

        public async Task DeactivateExitPromptAsync()
        {
            _isExitPromptActive = false;
            await RestoreGridFocusAsync();
        }

        public Task ActivateDetailsAsync()
        {
            CaptureFocusedGridItemIndex();
            _isDetailsViewActive = true;
            _focusedDetailsScreenshotIndex = -1;
            return FocusDetailsBackButtonAsync();
        }

        public async Task DeactivateDetailsAsync()
        {
            _isDetailsViewActive = false;
            _focusedDetailsScreenshotIndex = -1;
            _detailsScreenshotsListView.SelectedIndex = -1;
            await RestoreGridFocusAsync();
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

            if ((_isExitPromptActive && e.Key is VirtualKey.Enter or VirtualKey.Space)
                || (!_isExitPromptActive && e.Key is VirtualKey.Enter))
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

            if (_isDetailsViewActive)
            {
                HandleDetailsNavigation(direction);
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
            if (_isExitPromptActive)
            {
                var focusedElement = FocusManager.GetFocusedElement(_gridView.XamlRoot);
                if (ReferenceEquals(focusedElement, _exitYesButton))
                {
                    ExitConfirmed?.Invoke(this, EventArgs.Empty);
                    return;
                }

                ExitCanceled?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (_isDetailsViewActive)
            {
                if (ReferenceEquals(FocusManager.GetFocusedElement(_gridView.XamlRoot), _detailsBackButton))
                {
                    DetailsClosed?.Invoke(this, EventArgs.Empty);
                }

                return;
            }

            if (TryGetFocusedGridItem(out var item))
            {
                DetailsRequested?.Invoke(this, new GameLibraryItemInvokedEventArgs(item));
            }
        }

        private void HandleBackRequested()
        {
            if (_isDetailsViewActive)
            {
                DetailsClosed?.Invoke(this, EventArgs.Empty);
                return;
            }

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

        private async Task FocusDetailsBackButtonAsync()
        {
            _focusedDetailsScreenshotIndex = -1;
            _detailsScreenshotsListView.SelectedIndex = -1;
            await Task.Yield();
            _detailsBackButton.Focus(FocusState.Programmatic);
        }

        private void HandleDetailsNavigation(NavigationDirection direction)
        {
            if (_focusedDetailsScreenshotIndex < 0)
            {
                if (direction == NavigationDirection.Down && _detailsScreenshotsListView.Items.Count > 0)
                {
                    _ = FocusDetailsScreenshotAsync(0);
                }

                return;
            }

            if (_focusedDetailsScreenshotIndex >= 0)
            {
                switch (direction)
                {
                    case NavigationDirection.Left when _focusedDetailsScreenshotIndex > 0:
                        _ = FocusDetailsScreenshotAsync(_focusedDetailsScreenshotIndex - 1);
                        break;
                    case NavigationDirection.Right when _focusedDetailsScreenshotIndex < _detailsScreenshotsListView.Items.Count - 1:
                        _ = FocusDetailsScreenshotAsync(_focusedDetailsScreenshotIndex + 1);
                        break;
                    case NavigationDirection.Up:
                        _ = FocusDetailsBackButtonAsync();
                        break;
                }

                return;
            }

            _ = FocusDetailsBackButtonAsync();
        }

        private async Task FocusDetailsScreenshotAsync(int index)
        {
            if (_detailsScreenshotsListView.Items.Count == 0)
            {
                return;
            }

            var clampedIndex = Math.Clamp(index, 0, _detailsScreenshotsListView.Items.Count - 1);
            _focusedDetailsScreenshotIndex = clampedIndex;
            _detailsScreenshotsListView.SelectedIndex = clampedIndex;
            _detailsScreenshotsListView.UpdateLayout();
            _detailsScreenshotsListView.ScrollIntoView(_detailsScreenshotsListView.Items[clampedIndex]);

            await Task.Yield();

            if (_detailsScreenshotsListView.ContainerFromIndex(clampedIndex) is ListViewItem screenshotItem)
            {
                screenshotItem.Focus(FocusState.Programmatic);
                return;
            }

            _detailsScreenshotsListView.Focus(FocusState.Programmatic);
        }

        private async Task RestoreGridFocusAsync()
        {
            await FocusGridItemAsync(_lastFocusedItemIndex);
        }

        private async Task FocusGridItemAsync(int index)
        {
            if (_gridView.Items.Count == 0)
            {
                return;
            }

            var clampedIndex = Math.Clamp(index, 0, _gridView.Items.Count - 1);
            _lastFocusedItemIndex = clampedIndex;

            _gridView.UpdateLayout();
            _gridView.ScrollIntoView(_gridView.Items[clampedIndex]);

            await Task.Yield();

            if (_gridView.ContainerFromIndex(clampedIndex) is GridViewItem item)
            {
                item.Focus(FocusState.Programmatic);
                return;
            }

            _gridView.Focus(FocusState.Programmatic);
        }

        private void CaptureFocusedGridItemIndex()
        {
            if (FocusManager.GetFocusedElement(_gridView.XamlRoot) is GridViewItem itemContainer)
            {
                var index = _gridView.IndexFromContainer(itemContainer);
                if (index >= 0)
                {
                    _lastFocusedItemIndex = index;
                }
            }
        }

        private bool TryGetFocusedGridItem(out object? item)
        {
            item = null;

            if (FocusManager.GetFocusedElement(_gridView.XamlRoot) is not GridViewItem itemContainer)
            {
                return false;
            }

            var index = _gridView.IndexFromContainer(itemContainer);
            if (index < 0 || index >= _gridView.Items.Count)
            {
                return false;
            }

            _lastFocusedItemIndex = index;
            item = _gridView.Items[index];
            return true;
        }

        private bool CanProcessInput()
        {
            return _canProcessInput?.Invoke() != false;
        }
    }
}
