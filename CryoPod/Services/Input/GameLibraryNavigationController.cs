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
        private readonly GridView _gridView;
        private readonly Func<bool>? _canNavigate;
        private readonly GamepadNavigationService _gamepadNavigationService;

        public GameLibraryNavigationController(
            GridView gridView,
            DispatcherQueue dispatcherQueue,
            Func<bool>? canNavigate = null)
        {
            _gridView = gridView;
            _canNavigate = canNavigate;
            _gamepadNavigationService = new GamepadNavigationService(dispatcherQueue);
            _gamepadNavigationService.NavigationRequested += GamepadNavigationService_NavigationRequested;
            _gridView.PreviewKeyDown += GridView_PreviewKeyDown;
        }

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

        public void Dispose()
        {
            _gridView.PreviewKeyDown -= GridView_PreviewKeyDown;
            _gamepadNavigationService.NavigationRequested -= GamepadNavigationService_NavigationRequested;
            _gamepadNavigationService.Dispose();
        }

        private void GridView_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (!CanNavigate())
            {
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
            if (!CanNavigate())
            {
                return;
            }

            HandleNavigation(direction);
        }

        private void HandleNavigation(NavigationDirection direction)
        {
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

        private bool CanNavigate()
        {
            return _canNavigate?.Invoke() != false;
        }
    }
}
