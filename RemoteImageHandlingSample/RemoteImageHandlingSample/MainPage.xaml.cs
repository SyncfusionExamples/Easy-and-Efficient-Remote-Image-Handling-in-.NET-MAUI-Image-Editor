using Microsoft.Maui.Graphics;
using SkiaSharp;
using System.IO;
using System.Net.Http.Json;

namespace RemoteImageHandlingSample
{
    /// <summary>
    /// Represents the main page of the application, providing the user interface and event handling for image selection and editing.
    /// </summary>
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Handles the click event to load an image from a remote URL and display it in the image editor.
        /// </summary>
        /// <param name="sender">The source of the event, typically the control that was clicked.</param>
        /// <param name="e">An object that contains the event data.</param>
        private async void OnLoadImageClicked(object? sender, EventArgs e)
        {
            try
            {
                Busy.IsRunning = true;
                var vm = BindingContext as ComboBoxViewModel;
                if (vm?.SelectedOption == null)
                {
                    await DisplayAlertAsync("No selection", "Please select an image category.", "OK");
                    return;
                }

                // Pick the first URL or random if you prefer
                var url = vm.SelectedOption.Urls[0];
                using var safe = await SafeImageIO.FromUrlAsync(url, CancellationToken.None);
                var bytes = (safe as MemoryStream)?.ToArray() ?? ReadAllBytes(safe);
                imageEditor.Source = ImageSource.FromStream(() => new MemoryStream(bytes, writable: false));
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Load failed", ex.Message, "OK");
            }
            finally
            {
                Busy.IsRunning = false;
            }
        }

        /// <summary>
        /// Reads all bytes from the specified stream and returns them as a byte array.
        /// </summary>
        /// <param name="stream">The input stream to read from. The stream must be readable.</param>
        /// <returns>A byte array containing all the bytes read from the stream. The array will be empty if the stream contains
        /// no data.</returns>
        private static byte[] ReadAllBytes(Stream stream)
        {
            using var memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);
            return memoryStream.ToArray();
        }
    }
}