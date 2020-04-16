using AForge.Video.DirectShow;
using Emgu.CV;
using Emgu.CV.Structure;
using EmguCVTest.Entity;
using EmguCVTest.SDKModels;
using EmguCVTest.SDKUtil;
using EmguCVTest.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace EmguCVTest
{
    public partial class EmguCVTest : Form
    {
        #region 基础配置
        [DllImport("kernel32.dll")]
        private static extern void CopyMemory(IntPtr Destination, IntPtr Source, int Length);




        /// <summary>
        /// 人脸在图片中的位置
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct ASF_FaceRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
            public Rectangle Rectangle
            {
                get
                {
                    return new Rectangle(Left, Top, Right - Left, Bottom - Top);
                }
            }
        }

        #region 参数定义
        /// <summary>
        /// 引擎Handle
        /// </summary>
        private IntPtr pImageEngine = IntPtr.Zero;

        /// <summary>
        /// 保存右侧图片路径
        /// </summary>
        private string image1Path;

        /// <summary>
        /// 图片最大大小
        /// </summary>
        private long maxSize = 1024 * 1024 * 2;

        /// <summary>
        /// 右侧图片人脸特征
        /// </summary>
        private IntPtr image1Feature;

        /// <summary>
        /// 保存对比图片的列表
        /// </summary>
        private List<string> imagePathList = new List<string>();

        /// <summary>
        /// 左侧图库人脸特征列表
        /// </summary>
        private List<IntPtr> imagesFeatureList = new List<IntPtr>();

        /// <summary>
        /// 相似度
        /// </summary>
        private float threshold = 0.1f;

        /// <summary>
        /// 用于标记是否需要清除比对结果
        /// </summary>
        private bool isCompare = false;

        /// <summary>
        /// 是否是双目摄像
        /// </summary>
        private bool isDoubleShot = false;

        /// <summary>
        /// 允许误差范围
        /// </summary>
        private int allowAbleErrorRange = 40;

        /// <summary>
        /// RGB 摄像头索引
        /// </summary>
        private int rgbCameraIndex = 0;
        /// <summary>
        /// IR 摄像头索引
        /// </summary>
        private int irCameraIndex = 0;

        #region 视频模式下相关
        /// <summary>
        /// 视频引擎Handle
        /// </summary>
        private IntPtr pVideoEngine = IntPtr.Zero;
        /// <summary>
        /// RGB视频引擎 FR Handle 处理   FR和图片引擎分开，减少强占引擎的问题
        /// </summary>
        private IntPtr pVideoRGBImageEngine = IntPtr.Zero;
        /// <summary>
        /// IR视频引擎 FR Handle 处理   FR和图片引擎分开，减少强占引擎的问题
        /// </summary>
        private IntPtr pVideoIRImageEngine = IntPtr.Zero;
        /// <summary>
        /// 视频输入设备信息
        /// </summary>
        private FilterInfoCollection filterInfoCollection;
        /// <summary>
        /// RGB摄像头设备
        /// </summary>
        private VideoCaptureDevice rgbDeviceVideo;
        /// <summary>
        /// IR摄像头设备
        /// </summary>
        private VideoCaptureDevice irDeviceVideo;
        #endregion

        System.Threading.CancellationTokenSource _CancellationTokenSource = new System.Threading.CancellationTokenSource();
        long _TS = 0;

        #endregion
        class FaceData
        {
            public string Id { get; set; }
            public ASF_FaceFeature Feature { get; set; }
        }
        List<FaceData> _FaceLib = new List<FaceData>();

        class FaceResult
        {
            public Rectangle FaceRectangle { get; set; }
            public string Id { get; set; }
            public float Score { get; set; }
            public override string ToString()
            {
                return $"{Id},{Score}";
            }
        }
        ConcurrentDictionary<int, FaceResult> _FaceResults = new ConcurrentDictionary<int, FaceResult>();
        int _FaceNum = 0;

        SynchronizationContext _SyncContext;


        //VideoCapture _VideoCapture = new VideoCapture();
        //Mat _Frame = new Mat();


        IntPtr _PEngine = IntPtr.Zero;
        int _SizeOfASF_FaceRect = 0;

        bool _IsBusy = false;
        bool _NeedSave = false;
        #endregion

        #region 私有定义
        private FaceTrackUnit trackRGBUnit = new FaceTrackUnit();
        private FaceTrackUnit trackIRUnit = new FaceTrackUnit();
        private Font font = new Font(FontFamily.GenericSerif, 40f, FontStyle.Bold);
        private SolidBrush yellowBrush = new SolidBrush(Color.Yellow);
        private SolidBrush blueBrush = new SolidBrush(Color.Blue);
        private bool isRGBLock = false;
        private bool isIRLock = false;
        private MRECT allRect = new MRECT();
        private object rectLock = new object();
        #endregion


        /// <summary>
        /// EmguCV中获取视频信息的类
        /// </summary>
        Capture capture;

        Task _MatchTask;
        /// <summary>
        /// 构造方法
        /// </summary>
        public EmguCVTest()
        {
            InitializeComponent();
            _SyncContext = SynchronizationContext.Current;
        }
        /// <summary>
        /// 播放本地摄像头事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void TSBTPlayCamera_Click(object sender, EventArgs e)
        {
            //将capture实例化，没有任何参数的实例化将会读取本地摄像头
            capture = new Capture();
            //捕获图像时要调用的事件
            capture.ImageGrabbed += Capture_ImageGrabbed;
            //IBShow.Image = capture.QueryFrame();
        }
        /// <summary>
        /// 捕获图像的事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Capture_ImageGrabbed(object sender, EventArgs e)
        {
            //_MatchTask = Task.Factory.StartNew(() =>
            //{
            //    AppendText(_TS.ToString());
            //    Task.Delay(1000).Wait();
            //    while (!_CancellationTokenSource.IsCancellationRequested)
            //    {
            try
            {
                //Stopwatch sw = new Stopwatch();
                //sw.Start();



                // IBShow.Image = capture.QueryFrame();
                //capture.Retrieve(IBShow.Image, 0);
                //新建一个Mat
                Mat frame = new Mat();
                AppendText("获取一个Mat");
                if (capture == null || capture.Ptr == IntPtr.Zero)
                    return;
                //将得到的图像检索到frame中
                capture.Retrieve(frame, 0);
                double fsp = capture.GetCaptureProperty(Emgu.CV.CvEnum.CapProp.Fps);
                #region
                var bitmap = (Bitmap)frame.Bitmap.Clone();
                AppendText("得到当前RGB摄像头下的图片");
                //得到当前RGB摄像头下的图片
                // Bitmap bitmap = rgbVideoSource.GetCurrentVideoFrame();
                if (bitmap == null)
                {
                    return;
                }

                //检测人脸，得到Rect框
                AppendText("检测人脸，得到Rect框");
                ASF_MultiFaceInfo multiFaceInfo = FaceUtil.DetectFace(pVideoEngine, bitmap);
                List<ASF_SingleFaceInfo> alls = FaceUtil.GetAllFaces(multiFaceInfo);
                ASF_SingleFaceInfo maxFace = FaceUtil.GetMaxFace(multiFaceInfo);
                if (multiFaceInfo.faceNum > 2)
                    AppendText("获取所有人脸：" + alls.Count + "\n 总数量：" + multiFaceInfo.faceNum);
                ImageInfo imageInfo = new ImageInfo();
                // imageInfo = ImageUtil.ReadBMP(bitmap);

                Image<Bgr, byte> my_Image = new Image<Bgr, byte>(bitmap);
                imageInfo.format = ASF_ImagePixelFormat.ASVL_PAF_RGB24_B8G8R8;
                imageInfo.width = my_Image.Width;
                imageInfo.height = my_Image.Height;

                imageInfo.imgData = MemoryUtil.Malloc(my_Image.Bytes.Length);
                MemoryUtil.Copy(my_Image.Bytes, 0, imageInfo.imgData, my_Image.Bytes.Length);
                if (multiFaceInfo.faceNum > 0)
                {
                    foreach (ASF_SingleFaceInfo sing in alls)
                    {
                        //得到Rect
                        MRECT rect = sing.faceRect;
                       
                      //  my_Image.Dispose();
                        //调整图片数据，非常重要 
                        if (imageInfo == null) return;
                        float offsetX = my_Image.Width * 1f / frame.Width;//.Width;
                        float offsetY = my_Image.Height * 1f / frame.Height;
                        float x = rect.left * offsetX;
                        float width = rect.right * offsetX - x;
                        float y = rect.top * offsetY;
                        float height = rect.bottom * offsetY - y;
                        //检测RGB摄像头下最大人脸
                        using (Graphics g = Graphics.FromImage(frame.Bitmap))
                        {
                            //g.DrawString(_TS.ToString(), this.Font, Brushes.Red, 0, 0);
                            //for (int i = 0; i < _FaceNum; i++)
                            //{
                            //    if (_FaceResults.TryGetValue(i, out var faceResult))
                            //    {
                            //        g.DrawRectangle(Pens.Red, faceResult.FaceRectangle);
                            //        g.DrawString(faceResult.ToString(), new Font("宋体", 16), Brushes.Red, faceResult.FaceRectangle.Location);
                            //    }
                            //}
                            g.DrawRectangle(Pens.Red, x, y, width, height);
                            if (trackRGBUnit.message != "" && x > 0 && y > 0)
                            {
                                //将上一帧检测结果显示到页面上
                                g.DrawString(_TS + trackRGBUnit.message, font, trackRGBUnit.message.Contains("活体") ? blueBrush : yellowBrush, x, y - 15);
                            }
                        }
                        //保证只检测一帧，防止页面卡顿以及出现其他内存被占用情况
                        if (isRGBLock == false)
                        {
                            isRGBLock = true;
                            //异步处理提取特征值和比对，不然页面会比较卡
                            ThreadPool.QueueUserWorkItem(new WaitCallback(delegate
                            {
                                if (rect.left != 0 && rect.right != 0 && rect.top != 0 && rect.bottom != 0)
                                {
                                    try
                                    {
                                        lock (rectLock)
                                        {
                                            allRect.left = (int)(rect.left * offsetX);
                                            allRect.top = (int)(rect.top * offsetY);
                                            allRect.right = (int)(rect.right * offsetX);
                                            allRect.bottom = (int)(rect.bottom * offsetY);
                                        }

                                        bool isLiveness = false;


                                        if (imageInfo == null)
                                        {
                                            return;
                                        }
                                        int retCode_Liveness = -1;
                                                //RGB活体检测
                                                ASF_LivenessInfo liveInfo = FaceUtil.LivenessInfo_RGB(pVideoRGBImageEngine, imageInfo, multiFaceInfo, out retCode_Liveness);
                                                //判断检测结果
                                                if (retCode_Liveness == 0 && liveInfo.num > 0)
                                        {
                                            int isLive = MemoryUtil.PtrToStructure<int>(liveInfo.isLive);
                                            isLiveness = (isLive == 1) ? true : false;
                                        }
                                        if (imageInfo != null)
                                        {
                                            MemoryUtil.Free(imageInfo.imgData);
                                        }
                                        if (isLiveness)
                                        {
                                                    //提取人脸特征
                                                    IntPtr feature = FaceUtil.ExtractFeature(pVideoRGBImageEngine, bitmap, sing);
                                            float similarity = 0f;
                                                    //得到比对结果
                                                    //AppendText("进行对比");
                                                    int result = compareFeature(feature, out similarity);
                                            MemoryUtil.Free(feature);
                                            if (result > -1)
                                            {
                                                        //将比对结果放到显示消息中，用于最新显示
                                                        trackRGBUnit.message = string.Format(" {0}号 {1},{2}", result, similarity, string.Format("RGB{0}", isLiveness ? "活体" : "假体"));
                                            }
                                            else
                                            {
                                                        //显示消息
                                                        trackRGBUnit.message = string.Format("RGB{0}", isLiveness ? "活体" : "假体");
                                            }
                                        }
                                        else
                                        {
                                                    //显示消息
                                                    trackRGBUnit.message = string.Format("RGB{0}", isLiveness ? "活体" : "假体");
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine(ex.Message);
                                    }
                                    finally
                                    {
                                        if (bitmap != null)
                                        {
                                            bitmap.Dispose();
                                        }
                                       
                                        isRGBLock = false;
                                    }
                                }
                                else
                                {
                                    lock (rectLock)
                                    {
                                        allRect.left = 0;
                                        allRect.top = 0;
                                        allRect.right = 0;
                                        allRect.bottom = 0;
                                    }
                                }
                                isRGBLock = false;
                            }));
                        }
                        if (my_Image != null)
                        {
                            my_Image.Dispose();
                        }

                    }
                }
                //得到最大人脸
                // ASF_SingleFaceInfo maxFace = FaceUtil.GetMaxFace(multiFaceInfo);
                //_SyncContext.Send(o =>
                //{
                System.Threading.Thread.Sleep((int)(1000/fsp-6));
                this.IBShow.Image = frame;
                //  }, null);
                //sw.Stop();
                //_TS = sw.ElapsedMilliseconds;
            }
            catch (System.Exception ex)
            {
                string s = ex.Message;
            }

            //    }
            //}, _CancellationTokenSource.Token);
            #region  提取特征值



            #endregion


            #endregion



            //将图像赋值到IBShow的Image中完成显示
            // IBShow.Image = frame;
        }
        /// <summary>
        /// 控制视频的播放暂停
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void TSBTPlay_Click(object sender, EventArgs e)
        {
            if (capture != null)
            {
                if (tSBTPlay.Text == "Pause")
                {
                    //stop the capture
                    tSBTPlay.Text = "Play";
                    capture.Pause();
                }
                else
                {
                    //start the capture
                    tSBTPlay.Text = "Pause";
                    capture.Start();
                }
            }
        }
        /// <summary>
        /// 播放RTSP视频流事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void tSBTPlayRTSP_Click(object sender, EventArgs e)
        {
            try
            {
                aa();



            }
            catch (Exception ex)
            {
                AppendText("链接获取超时:" + ex.Message.ToString());
            }
        }

        public async Task aa()
        {
            string RTSPStreamText = tSTBRTSPStream.Text.Trim();
            var timeouttask = Task.Delay(5000); //设置超时时间
            var completedTask = await Task.WhenAny(new Task(async () =>
            {
                 capture =  new Capture(RTSPStreamText);//执行的方法示例这里用延迟代替
            }), timeouttask);
            if (completedTask == timeouttask)
            {
                capture.ImageGrabbed += Capture_ImageGrabbed;
            }
        }

        /// <summary>
        /// 播放本地视频
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void tSBTPlayLocal_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.RestoreDirectory = true;
            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                var fName = openFileDialog.FileName;
                capture = new Capture(fName);
                capture.ImageGrabbed += Capture_ImageGrabbed;
            }
        }

        private void EmguCVTest_Load(object sender, EventArgs e)
        {
            CheckForIllegalCrossThreadCalls = false;
            #region 初始化脚本
            //读取配置文件
            AppSettingsReader reader = new AppSettingsReader();
            string appId = (string)reader.GetValue("APP_ID", typeof(string));
            string sdkKey64 = (string)reader.GetValue("SDKKEY64", typeof(string));
            string sdkKey32 = (string)reader.GetValue("SDKKEY32", typeof(string));
            rgbCameraIndex = (int)reader.GetValue("RGB_CAMERA_INDEX", typeof(int));
            irCameraIndex = (int)reader.GetValue("IR_CAMERA_INDEX", typeof(int));
            //判断CPU位数
            var is64CPU = Environment.Is64BitProcess;
            if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(is64CPU ? sdkKey64 : sdkKey32))
            {
                //禁用相关功能按钮
                // ControlsEnable(false, chooseMultiImgBtn, matchBtn, btnClearFaceList, chooseImgBtn);
                MessageBox.Show(string.Format("请在App.config配置文件中先配置APP_ID和SDKKEY{0}!", is64CPU ? "64" : "32"));
                return;
            }

            //在线激活引擎    如出现错误，1.请先确认从官网下载的sdk库已放到对应的bin中，2.当前选择的CPU为x86或者x64
            int retCode = 0;
            try
            {
                retCode = ASFFunctions.ASFActivation(appId, is64CPU ? sdkKey64 : sdkKey32);
            }
            catch (Exception ex)
            {
                //禁用相关功能按钮
                // ControlsEnable(false, chooseMultiImgBtn, matchBtn, btnClearFaceList, chooseImgBtn);
                if (ex.Message.Contains("无法加载 DLL"))
                {
                    MessageBox.Show("请将sdk相关DLL放入bin对应的x86或x64下的文件夹中!");
                }
                else
                {
                    MessageBox.Show("激活引擎失败!");
                }
                return;
            }
            Console.WriteLine("Activate Result:" + retCode);

            //初始化引擎
            uint detectMode = DetectionMode.ASF_DETECT_MODE_IMAGE;
            //Video模式下检测脸部的角度优先值
            int videoDetectFaceOrientPriority = ASF_OrientPriority.ASF_OP_0_HIGHER_EXT;
            //Image模式下检测脸部的角度优先值
            int imageDetectFaceOrientPriority = ASF_OrientPriority.ASF_OP_0_ONLY;
            //人脸在图片中所占比例，如果需要调整检测人脸尺寸请修改此值，有效数值为2-32
            int detectFaceScaleVal = 16;
            //最大需要检测的人脸个数
            int detectFaceMaxNum = 10;
            //引擎初始化时需要初始化的检测功能组合
            int combinedMask = FaceEngineMask.ASF_FACE_DETECT | FaceEngineMask.ASF_FACERECOGNITION | FaceEngineMask.ASF_AGE | FaceEngineMask.ASF_GENDER | FaceEngineMask.ASF_FACE3DANGLE;
            //初始化引擎，正常值为0，其他返回值请参考http://ai.arcsoft.com.cn/bbs/forum.php?mod=viewthread&tid=19&_dsign=dbad527e
            retCode = ASFFunctions.ASFInitEngine(detectMode, imageDetectFaceOrientPriority, detectFaceScaleVal, detectFaceMaxNum, combinedMask, ref pImageEngine);
            Console.WriteLine("InitEngine Result:" + retCode);
            AppendText((retCode == 0) ? "引擎初始化成功!\n" : string.Format("引擎初始化失败!错误码为:{0}\n", retCode));
            if (retCode != 0)
            {
                AppendText("启动失败");
                //禁用相关功能按钮
                // ControlsEnable(false, chooseMultiImgBtn, matchBtn, btnClearFaceList, chooseImgBtn);
            }

            //初始化视频模式下人脸检测引擎
            uint detectModeVideo = DetectionMode.ASF_DETECT_MODE_VIDEO;
            int combinedMaskVideo = FaceEngineMask.ASF_FACE_DETECT | FaceEngineMask.ASF_FACERECOGNITION | FaceEngineMask.ASF_FACE3DANGLE;
            retCode = ASFFunctions.ASFInitEngine(detectModeVideo, videoDetectFaceOrientPriority, detectFaceScaleVal, detectFaceMaxNum, combinedMaskVideo, ref pVideoEngine);
            //RGB视频专用FR引擎
            detectFaceMaxNum = 10;
            combinedMask = FaceEngineMask.ASF_FACE_DETECT | FaceEngineMask.ASF_FACERECOGNITION | FaceEngineMask.ASF_LIVENESS;
            retCode = ASFFunctions.ASFInitEngine(detectMode, imageDetectFaceOrientPriority, detectFaceScaleVal, detectFaceMaxNum, combinedMask, ref pVideoRGBImageEngine);

            ////IR视频专用FR引擎
            //combinedMask = FaceEngineMask.ASF_FACE_DETECT | FaceEngineMask.ASF_FACERECOGNITION | FaceEngineMask.ASF_IR_LIVENESS;
            //retCode = ASFFunctions.ASFInitEngine(detectMode, imageDetectFaceOrientPriority, detectFaceScaleVal, detectFaceMaxNum, combinedMask, ref pVideoIRImageEngine);

            Console.WriteLine("InitVideoEngine Result:" + retCode);
            #endregion
        }


        /// <summary>
        /// 追加公用方法
        /// </summary>
        /// <param name="message"></param>
        private void AppendText(string message)
        {
            this.listBox1.Items.Add(message);
        }

        /// <summary>
        /// 得到feature比较结果
        /// </summary>
        /// <param name="feature"></param>
        /// <returns></returns>
        private int compareFeature(IntPtr feature, out float similarity)
        {
            int result = -1;
            similarity = 0f;
            //如果人脸库不为空，则进行人脸匹配
            if (imagesFeatureList != null && imagesFeatureList.Count > 0)
            {
                for (int i = 0; i < imagesFeatureList.Count; i++)
                {
                    //调用人脸匹配方法，进行匹配
                    ASFFunctions.ASFFaceFeatureCompare(pVideoRGBImageEngine, feature, imagesFeatureList[i], ref similarity);
                    if (similarity >= threshold)
                    {
                        result = i;
                        break;
                    }
                }
            }
            return result;
        }

        private Mat analyData(ASF_SingleFaceInfo maxFace, Mat frame, Bitmap bitmap, ASF_MultiFaceInfo multiFaceInfo)
        {
            //得到Rect
            MRECT rect = maxFace.faceRect;
            ImageInfo imageInfo = ImageUtil.ReadBMP(bitmap);
            //调整图片数据，非常重要 
            if (imageInfo == null) return frame;
            float offsetX = imageInfo.width * 1f / bitmap.Width;
            float offsetY = imageInfo.height * 1f / bitmap.Height;
            float x = rect.left * offsetX;
            float width = rect.right * offsetX - x;
            float y = rect.top * offsetY;
            float height = rect.bottom * offsetY - y;
            //检测RGB摄像头下最大人脸
            using (Graphics g = Graphics.FromImage(frame.Bitmap))
            {
                //g.DrawString(_TS.ToString(), this.Font, Brushes.Red, 0, 0);
                //for (int i = 0; i < _FaceNum; i++)
                //{
                //    if (_FaceResults.TryGetValue(i, out var faceResult))
                //    {
                //        g.DrawRectangle(Pens.Red, faceResult.FaceRectangle);
                //        g.DrawString(faceResult.ToString(), new Font("宋体", 16), Brushes.Red, faceResult.FaceRectangle.Location);
                //    }
                //}
                g.DrawRectangle(Pens.Red, x, y, width, height);
                if (trackRGBUnit.message != "" && x > 0 && y > 0)
                {
                    //将上一帧检测结果显示到页面上
                    g.DrawString(trackRGBUnit.message, font, trackRGBUnit.message.Contains("活体") ? blueBrush : yellowBrush, x, y - 15);
                }
            }
            //保证只检测一帧，防止页面卡顿以及出现其他内存被占用情况
            if (isRGBLock == false)
            {
                isRGBLock = true;
                //异步处理提取特征值和比对，不然页面会比较卡
                ThreadPool.QueueUserWorkItem(new WaitCallback(delegate
                {
                    if (rect.left != 0 && rect.right != 0 && rect.top != 0 && rect.bottom != 0)
                    {
                        try
                        {
                            lock (rectLock)
                            {
                                allRect.left = (int)(rect.left * offsetX);
                                allRect.top = (int)(rect.top * offsetY);
                                allRect.right = (int)(rect.right * offsetX);
                                allRect.bottom = (int)(rect.bottom * offsetY);
                            }

                            bool isLiveness = false;


                            if (imageInfo == null)
                            {
                                return;
                            }
                            int retCode_Liveness = -1;
                            //RGB活体检测
                            ASF_LivenessInfo liveInfo = FaceUtil.LivenessInfo_RGB(pVideoRGBImageEngine, imageInfo, multiFaceInfo, out retCode_Liveness);
                            //判断检测结果
                            if (retCode_Liveness == 0 && liveInfo.num > 0)
                            {
                                int isLive = MemoryUtil.PtrToStructure<int>(liveInfo.isLive);
                                isLiveness = (isLive == 1) ? true : false;
                            }
                            if (imageInfo != null)
                            {
                                MemoryUtil.Free(imageInfo.imgData);
                            }
                            if (isLiveness)
                            {
                                //提取人脸特征
                                IntPtr feature = FaceUtil.ExtractFeature(pVideoRGBImageEngine, bitmap, maxFace);
                                float similarity = 0f;
                                //得到比对结果
                                //AppendText("进行对比");
                                int result = compareFeature(feature, out similarity);
                                MemoryUtil.Free(feature);
                                if (result > -1)
                                {
                                    //将比对结果放到显示消息中，用于最新显示
                                    trackRGBUnit.message = string.Format(" {0}号 {1},{2}", result, similarity, string.Format("RGB{0}", isLiveness ? "活体" : "假体"));
                                }
                                else
                                {
                                    //显示消息
                                    trackRGBUnit.message = string.Format("RGB{0}", isLiveness ? "活体" : "假体");
                                }
                            }
                            else
                            {
                                //显示消息
                                trackRGBUnit.message = string.Format("RGB{0}", isLiveness ? "活体" : "假体");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.Message);
                        }
                        finally
                        {
                            if (bitmap != null)
                            {
                                bitmap.Dispose();
                            }
                            isRGBLock = false;
                        }
                    }
                    else
                    {
                        lock (rectLock)
                        {
                            allRect.left = 0;
                            allRect.top = 0;
                            allRect.right = 0;
                            allRect.bottom = 0;
                        }
                    }
                    isRGBLock = false;
                }));
            }
            return frame;
        }
    }
}
