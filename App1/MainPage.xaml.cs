using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.FaceAnalysis;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Microsoft.ProjectOxford.Face;

// 空白ページの項目テンプレートについては、https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x411 を参照してください

namespace App1
{
    /// <summary>
    /// それ自体で使用できる空白ページまたはフレーム内に移動できる空白ページ。
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public DispatcherTimer Timer { get; set; } = new DispatcherTimer();
        public MediaCapture MediaCapture { get; set; } = new MediaCapture();

        public MainPage()
        {
            this.InitializeComponent();

            InitAsync();

            Timer.Interval = TimeSpan.FromSeconds(2);

            Timer.Tick += async (sender, o) =>
            {

                var properties = MediaCapture.VideoDeviceController.GetMediaStreamProperties(MediaStreamType.VideoPreview) as VideoEncodingProperties;
                if (properties == null) return;

                //Jpeg形式でガメラの最大解像度で取得する。
                var property = ImageEncodingProperties.CreateJpeg();
                property.Width = properties.Width;
                property.Height = properties.Height;
                
                using (var randomStream = new InMemoryRandomAccessStream())
                {
                    await MediaCapture.CapturePhotoToStreamAsync(property, randomStream);
                    randomStream.Seek(0);
                    var detectedFaces = await DetectFaceAsync(randomStream);
                    this.FaceResultText.Text = $"{detectedFaces.Count} 個。{GetCongestion(detectedFaces.Count)}";
                }

            };
        }

        private static async Task<IList<DetectedFace>> DetectFaceAsync(IRandomAccessStream randomStream)
        {
            if (randomStream == null) throw new ArgumentNullException(nameof(randomStream));
            var decoder = await BitmapDecoder.CreateAsync(randomStream);
            var transform = new BitmapTransform();
            const float sourceImageHeightLimit = 1280;

            if (decoder.PixelHeight > sourceImageHeightLimit)
            {
                var scalingFactor = (float)sourceImageHeightLimit / (float)decoder.PixelHeight;
                transform.ScaledWidth = (uint)Math.Floor(decoder.PixelWidth * scalingFactor);
                transform.ScaledHeight = (uint)Math.Floor(decoder.PixelHeight * scalingFactor);
            }

            var sourceBitmap = await decoder.GetSoftwareBitmapAsync(decoder.BitmapPixelFormat, BitmapAlphaMode.Premultiplied, transform, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);

            const BitmapPixelFormat faceDetectionPixelFormat = BitmapPixelFormat.Gray8;

            var convertedBitmap = sourceBitmap.BitmapPixelFormat != faceDetectionPixelFormat ? SoftwareBitmap.Convert(sourceBitmap, faceDetectionPixelFormat) : sourceBitmap;


            var faceDetector = await FaceDetector.CreateAsync();


            var detectedFaces = await faceDetector.DetectFacesAsync(convertedBitmap);
            return detectedFaces;
        }

        public async Task InitAsync()
        {
            await MediaCapture.InitializeAsync();
            captureElement.Source = MediaCapture;
            await MediaCapture.StartPreviewAsync();
            Timer.Start();
        }

        private string GetCongestion(int peopleCount)
        {
            if (peopleCount < 1)
            {
                return "人がいないよ";
            }else if (peopleCount < 2)
            {
                return "人が少しいるよ";
            }
            else
            {
                return "人が多いよ";
            }
        }
    }
}
