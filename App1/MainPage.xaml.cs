using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.FaceAnalysis;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Microsoft.Cognitive.CustomVision.Prediction;
using Microsoft.Cognitive.CustomVision.Prediction.Models;
using Microsoft.Cognitive.CustomVision.Training;
using Microsoft.ProjectOxford.Face;
using Microsoft.WindowsAzure.Storage;
using Newtonsoft.Json;
using Image = Microsoft.Cognitive.CustomVision.Training.Models.Image;

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
        public IFaceServiceClient FaceServiceClient { get; set; } = new FaceServiceClient("e32173e705514cc2ac2ef2b615ed7131", "https://southcentralus.api.cognitive.microsoft.com/face/v1.0");

        public MainPage()
        {
            this.InitializeComponent();

            InitAsync();

            Timer.Interval = TimeSpan.FromSeconds(3);

            Timer.Tick += async (sender, o) =>
            {
                if (!(MediaCapture.VideoDeviceController.GetMediaStreamProperties(MediaStreamType.VideoPreview) is VideoEncodingProperties properties)) return;

                //Jpeg形式でガメラの最大解像度で取得する。
                var property = ImageEncodingProperties.CreateJpeg();
                property.Width = properties.Width;
                property.Height = properties.Height;

                using (var randomStream = new InMemoryRandomAccessStream())
                {
                    await MediaCapture.CapturePhotoToStreamAsync(property, randomStream);
                    randomStream.Seek(0);

                    var peoplePhotoUrl = await UploadToBlob(randomStream);
                    this.FaceResultText.Text = GetCongestionText((await PredictionWithCustomVisionService(peoplePhotoUrl)));

                    //var detectedFaces = await DetectFaceAsync(randomStream);
                    //this.FaceResultText.Text = $"{detectedFaces.Count} 個。{GetCongestionText(detectedFaces.Count)}";
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
                var scalingFactor = sourceImageHeightLimit / decoder.PixelHeight;
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

        private async Task<string> UploadToBlob(IRandomAccessStream stream)
        {
            var cloudStorageAccount = CloudStorageAccount.Parse("DefaultEndpointsProtocol=https;AccountName=shibuyakuhackstorage;AccountKey=p0oSJ8Krr9rlkBl+4SOdh7S2OWz7N2PS9wO1kVWbRUu3V8Bv0bUl2v256bd/EH2Vdw/qqkUEb+Cqw3Oq+qWJjg==;EndpointSuffix=core.windows.net");
            var cloudBlobClient = cloudStorageAccount.CreateCloudBlobClient();
            var cloudBlobContainer = cloudBlobClient.GetContainerReference("peoplephoto");
            await cloudBlobContainer.CreateIfNotExistsAsync();
            var blockBlobReference = cloudBlobContainer.GetBlockBlobReference(Guid.NewGuid().ToString() + ".jpg");
            await blockBlobReference.UploadFromStreamAsync(stream.AsStream());
            return blockBlobReference.StorageUri.PrimaryUri.ToString();
        }

        public async Task InitAsync()
        {
            await MediaCapture.InitializeAsync();
            captureElement.Source = MediaCapture;
            await MediaCapture.StartPreviewAsync();
            Timer.Start();
        }

        

        private string GetCongestionText(int peopleCount)
        {
            if (peopleCount == 0)
            {
                return "人が多いよ！((+_+))";
            }
            else if (peopleCount == 1)
            {
                return "人がチョットいるよ-!(*^。^*)";
            }
            else
            {
                return "人がいないかも！(∩´∀｀)∩";
            }
        }


        private async Task<int> PredictionWithCustomVisionService(string imagePath)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    var content = new StringContent(JsonConvert.SerializeObject(new { Url = imagePath }), Encoding.UTF8, "application/json");
                    const string PredictionKey = "9e4e3a31811b4366b20cc7888121df8b";
                    client.DefaultRequestHeaders.Add("Prediction-Key", PredictionKey);

                    const string RequestUri =
                        "https://southcentralus.api.cognitive.microsoft.com/customvision/v1.1/Prediction/99e85c85-11f0-46b9-aec4-9d0157196f14/url";
                    var PredictionResult = await client.PostAsync(RequestUri, content);
                    var PrefictionResultJson = await PredictionResult.Content.ReadAsStringAsync();
                    var customVisonPredictionResult = JsonConvert.DeserializeObject<CustomVisonPredictionResult>(PrefictionResultJson);
                    return (int)customVisonPredictionResult.Predictions.FirstOrDefault()?.Tag;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        private async Task PostCongestionScore(int CongestionScore, int PlaceId)
        {
            using (var client = new HttpClient())
            {
                var result = await client.PostAsync(
                    "https://prod-16.eastus.logic.azure.com/workflows/cb9b8e4144e0411bbc5df5e3be9f9d40/triggers/manual/paths/invoke?api-version=2016-10-01&sp=%2Ftriggers%2Fmanual%2Frun&sv=1.0&sig=VttriapABVvMSY01qOuVzEPHg3u_s0kBUjS6oGE89to",
                    new StringContent(
                        JsonConvert.SerializeObject(new { Congestion = CongestionScore, PlaceId = PlaceId })));
            }
        }

        public class CustomVisonPredictionResult
        {
            public string Id { get; set; }
            public string Project { get; set; }
            public string Iteration { get; set; }
            public DateTime Created { get; set; }
            public Prediction[] Predictions { get; set; }
        }

        public class Prediction
        {
            public string TagId { get; set; }
            public int Tag { get; set; }
            public float Probability { get; set; }
        }

    }
}
