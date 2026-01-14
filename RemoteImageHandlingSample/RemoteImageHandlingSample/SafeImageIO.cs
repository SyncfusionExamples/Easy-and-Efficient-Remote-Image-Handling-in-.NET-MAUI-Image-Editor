using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Text;

namespace RemoteImageHandlingSample
{
    // Safe image loading and sanitization
    public static class SafeImageIO
    {
        /// <summary>
        /// Represents the maximum allowed size, in bytes, for content or data operations.
        /// </summary>
        public const long MaxBytes = 20 * 1024 * 1024; // 20 MB
        /// <summary>
        /// Specifies the maximum number of pixels supported for image processing operations.
        /// </summary>
        public const int MaxPixels = 24_000_000;       // ~24 MP
        /// <summary>
        /// Specifies the maximum allowed width and height, in pixels, for images.
        /// </summary>
        public const int MaxW = 4096, MaxH = 4096;

        /// <summary>
        /// Represents the set of MIME types that are permitted for image uploads.
        /// </summary>
        private static readonly HashSet<string> AllowedMime = new(StringComparer.OrdinalIgnoreCase)
        { "image/jpeg","image/png" };

        /// <summary>
        /// Asynchronously downloads content from the specified HTTPS URL and returns a sanitized stream of the response data.
        /// </summary>
        /// <param name="httpsUrl">The https Url.</param>
        /// <param name="cancelToken">A cancellation token.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains a stream with the sanitized
        /// content from the specified URL.</returns>
        public static async Task<Stream> FromUrlAsync(string httpsUrl, CancellationToken cancelToken = default)
        {
            var uri = new Uri(httpsUrl);
            if (uri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException("HTTPS required");

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancelToken);
            res.EnsureSuccessStatusCode();

            var contentType = res.Content.Headers.ContentType?.MediaType;

            await using var net = await res.Content.ReadAsStreamAsync(cancelToken);
            return await ImageSanitizing(net, contentType, cancelToken);
        }

        /// <summary>
        /// Processes and sanitizes an image stream by validating its format, removing metadata, correcting orientation, and resizing as needed.
        /// </summary>
        /// <param name="source">The source.</param>
        /// <param name="contentType">The content type.</param>
        /// <param name="cancelToken">A cancellation token.</param>
        /// <returns>A stream containing the sanitized image data in the original or a compatible format. The returned stream is
        /// positioned at the beginning and must be disposed by the caller.</returns>
        public static async Task<Stream> ImageSanitizing(Stream source, string? contentType, CancellationToken cancelToken)
        {
            // Copy to bounded memory
            using var ms = new MemoryStream();
            await CopyWithLimitedBytes(source, ms, MaxBytes, cancelToken);
            var data = ms.ToArray();

            if (!ValidatingFileSignatures(data) || (contentType != null && !AllowedMime.Contains(contentType)))
                throw new InvalidDataException("Unsupported image format");

            // Decode bounds and EXIF orientation
            using var codec = SKCodec.Create(new SKMemoryStream(data)) ?? throw new InvalidDataException("Invalid image");
            var info = codec.Info;
            if ((long)info.Width * info.Height > MaxPixels)
                throw new InvalidDataException("Too many pixels");

            var origin = codec.EncodedOrigin;

            // Decode -> orient -> downscale
            using var original = SKBitmap.Decode(data) ?? throw new InvalidDataException("Decode failed");
            using var oriented = ApplyOrientation(original, origin);
            using var resized = ImageResizing(oriented, MaxW, MaxH);

            // Re-encode without EXIF (privacy + consistency)
            using var image = SKImage.FromBitmap(resized);
            var format = FormatValidation(data);
            using var encoded = image.Encode(format, quality: 90);
            var outStream = new MemoryStream();
            encoded.SaveTo(outStream);
            outStream.Position = 0;
            return outStream;
        }

        /// <summary>
        /// Asynchronously copies data from the source stream to the destination stream up to the specified byte limit.
        /// </summary>
        /// <param name="source">The stream source.</param>
        /// <param name="destination">The stream to write data to.</param>
        /// <param name="limit">The maximum number of bytes to copy from the source stream.</param>
        /// <param name="cancelToken">A cancellation token.</param>
        /// <returns>A task that represents the asynchronous copy operation.</returns>
        private static async Task CopyWithLimitedBytes(Stream source, Stream destination, long limit, CancellationToken cancelToken)
        {
            var buffer = new byte[64 * 1024];
            long total = 0;

            while (total < limit)
            {
                // Read no more than what remains up to the limit
                int toRead = (int)Math.Min(buffer.Length, limit - total);
                int read = await source.ReadAsync(buffer.AsMemory(0, toRead), cancelToken).ConfigureAwait(false);
                if (read == 0)
                {
                    // End of source
                    break;
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancelToken).ConfigureAwait(false);
                total += read;
            }

            // Only rewind if the destination supports seeking (optional for MemoryStream).
            if (destination.CanSeek)
            {
                destination.Position = 0;
            }
        }

        /// <summary>
        /// Determines whether the specified byte sequence appears to represent a supported image format.
        /// </summary>
        /// <param name="bytes">A read-only span of bytes.</param>
        /// <returns>true if the data appears to be a JPEG or PNG image based on its initial bytes; otherwise, false.</returns>
        private static bool ValidatingFileSignatures(ReadOnlySpan<byte> bytes)
        {
            bool jpg = bytes.Length > 2 && bytes[0] == 0xFF && bytes[1] == 0xD8;
            bool png = bytes.Length > 4 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;
            return jpg || png;
        }

        /// <summary>
        /// Attempts to determine the image format of the provided byte array based on its header bytes.
        /// </summary>
        /// <param name="bytes">The byte array containing the image data to analyze.</param>
        /// <returns>An SKEncodedImageFormat value indicating the guessed image format. Returns SKEncodedImageFormat.Jpeg if the
        /// format cannot be determined.</returns>
        private static SKEncodedImageFormat FormatValidation(byte[] bytes)
            => bytes.Length > 2 && bytes[0] == 0xFF && bytes[1] == 0xD8 ? SKEncodedImageFormat.Jpeg :
               bytes.Length > 4 && bytes[0] == 0x89 && bytes[1] == 0x50 ? SKEncodedImageFormat.Png :
               SKEncodedImageFormat.Jpeg;

        /// <summary>
        /// Returns a new bitmap with the specified orientation applied to the source image.
        /// </summary>
        /// <param name="source">The source <see cref="SKBitmap"/> to which the orientation will be applied.</param>
        /// <param name="origin">The orientation to apply, as specified by the <see cref="SKEncodedOrigin"/> enumeration.</param>
        /// <returns>A new <see cref="SKBitmap"/> instance with the orientation specified by <paramref name="origin"/> applied. The
        /// returned bitmap has the same pixel data as <paramref name="source"/>, transformed according to the orientation.</returns>
        private static SKBitmap ApplyOrientation(SKBitmap source, SKEncodedOrigin origin)
        {
            if (origin == SKEncodedOrigin.TopLeft) return source.Copy();
            bool swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.LeftBottom or SKEncodedOrigin.RightBottom;
            int width = swap ? source.Height : source.Width, height = swap ? source.Width : source.Height;

            var stream = new SKBitmap(width, height, source.ColorType, source.AlphaType);
            using var canvas = new SKCanvas(stream);

            switch (origin)
            {
                case SKEncodedOrigin.TopRight: canvas.Scale(-1, 1); canvas.Translate(-source.Width, 0); break;
                case SKEncodedOrigin.BottomRight: canvas.RotateDegrees(180, source.Width / 2f, source.Height / 2f); canvas.Translate(-source.Width, -source.Height); break;
                case SKEncodedOrigin.BottomLeft: canvas.Scale(1, -1); canvas.Translate(0, -source.Height); break;
                case SKEncodedOrigin.RightTop: canvas.Translate(width, 0); canvas.RotateDegrees(90); canvas.Scale(-1, 1); break;
                case SKEncodedOrigin.RightBottom: canvas.Translate(width, 0); canvas.RotateDegrees(90); break;
                case SKEncodedOrigin.LeftBottom: canvas.Translate(0, height); canvas.RotateDegrees(-90); canvas.Scale(-1, 1); break;
                case SKEncodedOrigin.LeftTop: canvas.Translate(0, height); canvas.RotateDegrees(-90); break;
            }

            canvas.DrawBitmap(source, 0, 0);
            canvas.Flush();
            return stream;
        }

        /// <summary>
        /// Resizes the specified bitmap to fit within the given maximum width and height, preserving the aspect ratio.
        /// </summary>
        /// <param name="source">The source <see cref="SKBitmap"/> to resize. Cannot be null.</param>
        /// <param name="maxWidth">The maximum allowed width, in pixels, for the resulting bitmap.</param>
        /// <param name="maxHeight">The maximum allowed height, in pixels, for the resulting bitmap.</param>
        /// <returns>A new <see cref="SKBitmap"/> that fits within the specified dimensions. The returned bitmap will have the
        /// same aspect ratio as the source. If the source bitmap already fits within the specified dimensions, a copy
        /// of the original bitmap is returned.</returns>
        private static SKBitmap ImageResizing(SKBitmap source, int maxWidth, int maxHeight)
        {
            if (source.Width <= maxWidth && source.Height <= maxHeight) return source.Copy();
            float scale = Math.Min((float)maxWidth / source.Width, (float)maxHeight / source.Height);
            int width = Math.Max(1, (int)(source.Width * scale)), height = Math.Max(1, (int)(source.Height * scale));
            var stream = new SKBitmap(width, height, source.ColorType, source.AlphaType);
            using var canvas = new SKCanvas(stream);
            var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
            using var image = SKImage.FromBitmap(source);
            canvas.DrawImage(image, new SKRect(0, 0, width, height), sampling);
            return stream;
        }
    }
}