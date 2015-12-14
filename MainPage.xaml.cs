using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Media;
using Windows.Media.Capture;
using Windows.Media.Devices;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using ZXing;

//“空白页”项模板在 http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409 上有介绍

namespace QrcodeDemo
{
    /// <summary>
    /// 可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class MainPage : Page
    { 
        public MainPage()
        {
            this.InitializeComponent();
#if DEBUG
            tbkResult.Visibility = Visibility.Visible;
#endif
        }
        private Result _result;
        private readonly MediaCapture _mediaCapture = new MediaCapture();

        protected async override void OnNavigatedTo(NavigationEventArgs e)
        { 
            await InitVideoCaptureAsync();
        } 


        static Guid DecoderIdFromFileExtension(string strExtension)
        {
            Guid encoderId;
            switch (strExtension.ToLower())
            {
                case ".jpg":
                case ".jpeg":
                    encoderId = BitmapDecoder.JpegDecoderId;
                    break;
                case ".bmp":
                    encoderId = BitmapDecoder.BmpDecoderId;
                    break;
                case ".png":
                default:
                    encoderId = BitmapDecoder.PngDecoderId;
                    break;
            }
            return encoderId;
        }

        /// <summary>
        /// 最大支持的图片大小
        /// </summary>
        public static Size MaxSizeSupported = new Size(1000, 1000);

        /// <summary>
        /// 读取照片流 转为WriteableBitmap给二维码解码器
        /// </summary>
        /// <param name="fileStream"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        public async static Task<WriteableBitmap> ReadBitmap(IRandomAccessStream fileStream, string type)
        {
            WriteableBitmap bitmap = null;
            try
            {
                var decoderId = DecoderIdFromFileExtension(type);
                var decoder = await BitmapDecoder.CreateAsync(decoderId, fileStream);
                var tf = new BitmapTransform();
                uint width = decoder.OrientedPixelWidth;
                uint height = decoder.OrientedPixelHeight;
                double dScale = 1;
                if (decoder.OrientedPixelWidth > MaxSizeSupported.Width || decoder.OrientedPixelHeight > MaxSizeSupported.Height)
                {
                    dScale = Math.Min(MaxSizeSupported.Width / decoder.OrientedPixelWidth, MaxSizeSupported.Height / decoder.OrientedPixelHeight);
                    width = (uint)(decoder.OrientedPixelWidth * dScale);
                    height = (uint)(decoder.OrientedPixelHeight * dScale);
                    tf.ScaledWidth = (uint)(decoder.PixelWidth * dScale);
                    tf.ScaledHeight = (uint)(decoder.PixelHeight * dScale);
                }
                bitmap = new WriteableBitmap((int)width, (int)height);
                PixelDataProvider dataprovider = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, tf,
                    ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
                byte[] pixels = dataprovider.DetachPixelData();
                using (Stream pixelStream2 = bitmap.PixelBuffer.AsStream())
                {
                    pixelStream2.Write(pixels, 0, pixels.Length);
                }
                //bitmap.SetSource(fileStream);
            }
            catch
            {
            }
            return bitmap;
        }
         

        /// <summary>
        /// 二维码解析器设置
        /// </summary>
        private readonly BarcodeReader _barcodeReader = new BarcodeReader
        {
            AutoRotate = true,
            Options = new ZXing.Common.DecodingOptions { TryHarder = true }
        };
        /// <summary>
        /// 解析二维码图片
        /// </summary>
        /// <param name="writeableBmp">图片</param>
        /// <returns></returns>
        private async Task ScanBitmap(WriteableBitmap writeableBmp)
        {
            try
            {
                _result = _barcodeReader.Decode(writeableBmp);
                if (_result != null)
                {
                    Debug.WriteLine(@"[INFO]扫描到二维码:{result}   ->" + _result.Text);
                    var resultText = _result.Text;
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        tbkResult.Text = _result.Text;
                    });
                    Uri launchUri = null;
                    if (Uri.TryCreate(resultText, UriKind.Absolute, out launchUri))
                    {
                        await Launcher.LaunchUriAsync(launchUri); 
                        await _mediaCapture.StopPreviewAsync();
                    }
                    else
                    {
                        _result = null;
                        Debug.WriteLine("未知的二维码: " + resultText); 
                    }
                }
            }
            catch (Exception)
            {
                // ignored
            }
        }

        /// <summary>
        /// 初始化摄像头设置
        /// </summary>
        private async Task InitVideoCaptureAsync()
        {
            try
            {
                var settings = new MediaCaptureInitializationSettings
                {
                    StreamingCaptureMode = StreamingCaptureMode.Video,
                    PhotoCaptureSource = PhotoCaptureSource.VideoPreview,
                    AudioDeviceId = string.Empty,
                };
                //摄像头的检测--优先后置摄像头 
                var cameraDevice = await FindCameraDeviceByPanelAsync(Windows.Devices.Enumeration.Panel.Back);
                if (cameraDevice != null)
                {
                    settings.VideoDeviceId = cameraDevice.Id;
                }
                await _mediaCapture.InitializeAsync(settings); 
                VideoCapture.Source = _mediaCapture;
                _mediaCapture.SetPreviewRotation(VideoRotation.Clockwise90Degrees);
                //设置焦距为2倍变焦
                _mediaCapture.VideoDeviceController.ZoomControl.Value = 2f;
                if (_mediaCapture.VideoDeviceController.FocusControl.Supported && _mediaCapture.VideoDeviceController.FocusControl.WaitForFocusSupported)
                {
                    //设置自动对焦
                    var focusSettings = new FocusSettings
                    { 
                        Mode = FocusMode.Continuous
                    };
                    _mediaCapture.VideoDeviceController.FocusControl.Configure(focusSettings);
                }
                await _mediaCapture.StartPreviewAsync();
                if (_mediaCapture.VideoDeviceController.FocusControl.Supported && _mediaCapture.VideoDeviceController.FocusControl.WaitForFocusSupported)
                {
                    //进行对焦
                    await _mediaCapture.VideoDeviceController.FocusControl.FocusAsync();
                }
                while (_result == null)
                { 
                    Debug.WriteLine(@"[INFO]开始扫描 -> " + DateTime.Now.ToString(CultureInfo.InvariantCulture));
                    try
                    {
                        var previewProperties = _mediaCapture.VideoDeviceController.GetMediaStreamProperties(MediaStreamType.VideoPreview) as VideoEncodingProperties;
                        if (previewProperties != null)
                        {
                            var videoFrame = new VideoFrame(BitmapPixelFormat.Bgra8, (int)previewProperties.Width, (int)previewProperties.Height);
                            using (var currentFrame = await _mediaCapture.GetPreviewFrameAsync(videoFrame))
                            {
                                var frameBitmap = currentFrame.SoftwareBitmap;
                                if (frameBitmap != null)
                                {
                                    var bitmap = new WriteableBitmap(frameBitmap.PixelWidth, frameBitmap.PixelHeight);
                                    try
                                    {
                                        frameBitmap.CopyToBuffer(bitmap.PixelBuffer);
                                        await ScanBitmap(bitmap);
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.WriteLine("扫码失败：" + ex.Message);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("扫码失败：" + ex.Message); 
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("初始化摄像头失败，请检查是否具有相机权限?\n" + ex.Message); 
            }
        }
         
        /// <summary>
        /// 获取摄像头
        /// </summary>
        /// <param name="desiredPanel"></param>
        /// <returns></returns>
        private static async Task<DeviceInformation> FindCameraDeviceByPanelAsync(Windows.Devices.Enumeration.Panel desiredPanel)
        {
            try
            {
                var allVideoDevices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
                var desiredDevice = allVideoDevices.FirstOrDefault(x => x.EnclosureLocation != null && x.EnclosureLocation.Panel == desiredPanel);
                return desiredDevice ?? allVideoDevices.FirstOrDefault();
            }
            catch (Exception)
            {
               
            }
            return null;
        }
    }
}
