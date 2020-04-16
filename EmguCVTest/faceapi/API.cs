using EmguCVTest.Entity;
using EmguCVTest.SDKModels;
using EmguCVTest.SDKUtil;
using EmguCVTest.Utils;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EmguCVTest.faceapi
{
    /// <summary>
    /// 四线程
    /// </summary>
    public static class API
    {
        [DllImport("kernel32.dll")]
        private static extern void CopyMemory(IntPtr Destination, IntPtr Source, int Length);
        const int FeatureSize = 22020;
        const int TaskNum = 4;
        /// <summary>
        /// 人脸识别结果集
        /// </summary>
        public static FaceResults CacheFaceResults { get; set; }
        /// <summary>
        /// 人脸检测的缓存
        /// </summary>
        private static IntPtr _DBuffer = IntPtr.Zero;
        /// <summary>
        /// 人脸比对的缓存
        /// </summary>
        private static IntPtr[] _RBuffer = new IntPtr[TaskNum];
        /// <summary>
        /// 人脸检测的引擎
        /// </summary>
        private static IntPtr _DEnginer = IntPtr.Zero;
        /// <summary>
        /// 人脸比对的引擎
        /// </summary>
        private static IntPtr[] _REngine = new IntPtr[TaskNum];
        private static int _MaxFaceNumber;
        private static string _FaceDataPath, _FaceImagePath;

        private static readonly FaceLib[] _FaceLib = new FaceLib[TaskNum];
        private static readonly ReaderWriterLockSlim _RWL = new ReaderWriterLockSlim();

        /// <summary>
        /// 初始化，主要用于视频，取消人脸方向参数
        /// </summary>
        /// <param name="appId">虹软SDK的AppId</param>
        /// <param name="dKey">虹软SDK人脸检测的Key</param>
        /// <param name="rKey">虹软SDK人脸比对的Key</param>
        /// <param name="orientPriority">脸部角度，毋宁说是鼻子方向，上下为0或180度，左右为90或270度</param>
        /// <param name="scale">最小人脸尺寸有效值范围[2,50] 推荐值 16。该尺寸是人脸相对于所在图片的长边的占比。例如，如果用户想检测到的最小人脸尺寸是图片长度的 1/8，那么这个 scale 就应该设置为8</param>
        /// <param name="maxFaceNumber">用户期望引擎最多能检测出的人脸数有效值范围[1,100]</param>
        /// <param name="faceDataPath">人脸数据文件夹</param>
        /// <param name="message"></param>
        /// <returns></returns>
        public static bool Init(out string message, string appId, string dKey, string rKey, int scale = 2, int maxFaceNumber = 50, string faceDataPath = "d:\\FeatureData")
        {
            if (scale < 2 || scale > 50)
            {
                message = "scale的值必须在2-50之间";
                return false;
            }
            if (maxFaceNumber < 1 || maxFaceNumber > 100)
            {
                message = "人脸数量必须在1-100之间";
                return false;
            }
            _DBuffer = Marshal.AllocCoTaskMem(20 * 1024 * 1024);
            #region 初始化脚本
            //判断CPU位数
            var is64CPU = Environment.Is64BitProcess;

            //在线激活引擎    如出现错误，1.请先确认从官网下载的sdk库已放到对应的bin中，2.当前选择的CPU为x86或者x64
            int retCode = 0;
            Console.WriteLine("Activate Result:" + retCode);

            //初始化引擎
            uint detectMode = DetectionMode.ASF_DETECT_MODE_IMAGE;
            //Video模式下检测脸部的角度优先值
            int videoDetectFaceOrientPriority = ASF_OrientPriority.ASF_OP_0_HIGHER_EXT;
            //Image模式下检测脸部的角度优先值
            int imageDetectFaceOrientPriority = ASF_OrientPriority.ASF_OP_0_ONLY;
            //引擎初始化时需要初始化的检测功能组合
            int combinedMask = FaceEngineMask.ASF_FACE_DETECT | FaceEngineMask.ASF_FACERECOGNITION | FaceEngineMask.ASF_AGE | FaceEngineMask.ASF_GENDER | FaceEngineMask.ASF_FACE3DANGLE;
            //初始化引擎，正常值为0，其他返回值请参考http://ai.arcsoft.com.cn/bbs/forum.php?mod=viewthread&tid=19&_dsign=dbad527e
            retCode = ASFFunctions.ASFInitEngine(detectMode, imageDetectFaceOrientPriority, scale, maxFaceNumber, combinedMask, ref _DEnginer);
            Console.WriteLine("InitEngine Result:" + retCode);
            //  AppendText((retCode == 0) ? "引擎初始化成功!\n" : string.Format("引擎初始化失败!错误码为:{0}\n", retCode));
            message = $"引擎初始化成功";
            if (retCode != 0)
            {
                message = $"引擎初始化失败，错误代码:{(int)retCode}";
                return false;
                //禁用相关功能按钮
                // ControlsEnable(false, chooseMultiImgBtn, matchBtn, btnClearFaceList, chooseImgBtn);
            }
            for (int i = 0; i < TaskNum; i++)
            {
                //初始化视频模式下人脸检测引擎
                uint detectModeVideo = DetectionMode.ASF_DETECT_MODE_VIDEO;
                int combinedMaskVideo = FaceEngineMask.ASF_FACE_DETECT | FaceEngineMask.ASF_FACERECOGNITION;
                retCode = ASFFunctions.ASFInitEngine(detectModeVideo, videoDetectFaceOrientPriority, scale, maxFaceNumber, combinedMaskVideo, ref _REngine[i]);
                if (retCode != 0)
                {
                    message = $"初始化视频模式下人脸检测引擎，错误代码:{(int)retCode}";
                    return false;
                }
                _FaceLib[i] = new FaceLib();
            }
            ////RGB视频专用FR引擎
            //combinedMask = FaceEngineMask.ASF_FACE_DETECT | FaceEngineMask.ASF_FACERECOGNITION | FaceEngineMask.ASF_LIVENESS;
            //retCode = ASFFunctions.ASFInitEngine(detectMode, imageDetectFaceOrientPriority, scale, maxFaceNumber, combinedMask, ref pVideoRGBImageEngine);
            //if (retCode != 0)
            //{
            //    message = $"初始化RGB视频专用FR引擎，错误代码:{(int)retCode}";
            //    return false;
            //}
            ////IR视频专用FR引擎
            //combinedMask = FaceEngineMask.ASF_FACE_DETECT | FaceEngineMask.ASF_FACERECOGNITION | FaceEngineMask.ASF_IR_LIVENESS;
            //retCode = ASFFunctions.ASFInitEngine(detectMode, imageDetectFaceOrientPriority, scale, maxFaceNumber, combinedMask, ref pVideoIRImageEngine);
            //if (retCode != 0)
            //{
            //    message = $"初始化IR视频专用FR引擎，错误代码:{(int)retCode}";
            //    return false;
            //}
            //Console.WriteLine("InitVideoEngine Result:" + retCode);
            #endregion
            //var initResult = (ErrorCode)ArcWrapper.DInit(appId, dKey, _DBuffer, 20 * 1024 * 1024, out _DEnginer, (int)ArcFace.EOrientPriority.Only0, scale, maxFaceNumber);
            //if (initResult != ErrorCode.Ok)
            //{
            //    message = $"初始化人脸检测引擎失败，错误代码:{(int)initResult}，错误描述：{ initResult.GetType().GetMember(initResult.ToString()).FirstOrDefault()?.GetCustomAttribute<DescriptionAttribute>().Description ?? initResult.ToString()}";
            //    return false;
            //}
            //for (int i = 0; i < TaskNum; i++)
            //{
            //    _RBuffer[i] = Marshal.AllocCoTaskMem(40 * 1024 * 1024);
            //    initResult = (ErrorCode)ArcWrapper.RInit(appId, rKey, _RBuffer[i], 40 * 1024 * 1024, out _REngine[i]);
            //    if (initResult != ErrorCode.Ok)
            //    {
            //        message = $"初始化人脸比对引擎失败，错误代码:{(int)initResult}，错误描述：{ initResult.GetType().GetMember(initResult.ToString()).FirstOrDefault()?.GetCustomAttribute<DescriptionAttribute>().Description ?? initResult.ToString()}";
            //        return false;
            //    }

            //    _FaceLib[i] = new FaceLib();
            //}

            CacheFaceResults = new FaceResults(maxFaceNumber);
            _MaxFaceNumber = maxFaceNumber;

            _FaceDataPath = faceDataPath;
            _FaceImagePath = Path.Combine(_FaceDataPath, "Image");
            if (!Directory.Exists(faceDataPath))
                Directory.CreateDirectory(faceDataPath);
            if (!Directory.Exists(_FaceImagePath))
                Directory.CreateDirectory(_FaceImagePath);

            int index = 0;
            for (int i = 0; i < 100; i++)

                foreach (var file in Directory.GetFiles(faceDataPath))
                {
                    var info = new FileInfo(file);
                    var data = File.ReadAllBytes(file);
                    var pFeature = Marshal.AllocCoTaskMem(data.Length);
                    Marshal.Copy(data, 0, pFeature, data.Length);
                    _FaceLib[index % TaskNum].Items.Add(new FaceLib.Item() { OrderId = 0, ID = info.Name.Replace(info.Extension, ""), FaceModel = new FaceModel { Size = FeatureSize, PFeature = pFeature } });
                    index++;

                }

            message = "初始化成功";
            return true;
        }
        public static void Close()
        {
            if (_DEnginer != IntPtr.Zero)
            {
                ASFFunctions.ASFUninitEngine(_DEnginer);
                _DEnginer = IntPtr.Zero;
            }
            if (_DBuffer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(_DBuffer);
                _DBuffer = IntPtr.Zero;
            }
            for (int i = 0; i < TaskNum; i++)
            {
                if (_REngine[i] != IntPtr.Zero)
                {
                    ASFFunctions.ASFUninitEngine(_REngine[i]);
                    _REngine[i] = IntPtr.Zero;
                }
                if (_RBuffer[i] != IntPtr.Zero)
                {

                    Marshal.FreeCoTaskMem(_RBuffer[i]);
                    _RBuffer[i] = IntPtr.Zero;
                }
                foreach (var item in _FaceLib[i].Items)
                {
                    Marshal.FreeCoTaskMem(item.FaceModel.PFeature);
                }
            }
            foreach (var item in CacheFaceResults.Items)
                Marshal.FreeCoTaskMem(item.FaceModel.PFeature);


        }


        /// <summary>
        /// 人脸比对
        /// </summary>
        /// <param name="bitmap">输入图片</param>
        public static void FaceMatch(Bitmap bitmap)
        {

            var bmpData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            var imageData = new ImageData
            {
                PixelArrayFormat = 513,//Rgb24,
                Width = bitmap.Width,
                Height = bitmap.Height,
                Pitch = new int[4] { bmpData.Stride, 0, 0, 0 },
                ppu8Plane = new IntPtr[4] { bmpData.Scan0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero }
            };
            ASF_MultiFaceInfo multiFaceInfo = FaceUtil.DetectFace(_DEnginer, bitmap);


            var detectResult = multiFaceInfo;

            CacheFaceResults.FaceNumber = detectResult.faceNum;
            for (int i = detectResult.faceNum; i < _MaxFaceNumber; i++)
            {
                CacheFaceResults[i].ID = "";
            }
            if (detectResult.faceNum == 0)
            {
                bitmap.UnlockBits(bmpData);
                return;
            }

            if (detectResult.faceNum == 1)
            {
                CacheFaceResults[0].FFI.FaceRect = Marshal.PtrToStructure<FaceRect>(detectResult.faceRects);
                var aa= Marshal.PtrToStructure<ASF_MultiFaceInfo>(detectResult.faceRects);
                // if (ArcWrapper.ExtractFeature(_REngine[0], ref imageData, ref CacheFaceResults[0].FFI, out var fm) == (int)ErrorCode.Ok)
                //FaceUtil.ExtractFeature(_REngine[0], bitmap, detectResult.faceRects);
                CopyMemory(CacheFaceResults.Items[0].FaceModel.PFeature, detectResult.faceOrients, FeatureSize);
            }
            else
            {
                Task[] ts = new Task[TaskNum < detectResult.faceNum ? TaskNum : detectResult.faceNum];
                int faceOffset = -1;
                for (int i = 0; i < ts.Length; i++)
                {
                    var rEngine = _REngine[i];
                    ts[i] = Task.Factory.StartNew(() =>
                    {
                        int faceIndex = 0;
                        while ((faceIndex = Interlocked.Increment(ref faceOffset)) < detectResult.faceNum)
                        {
                            CacheFaceResults[faceIndex].FFI.FaceRect = Marshal.PtrToStructure<FaceRect>(IntPtr.Add(detectResult.faceRects, faceIndex * Marshal.SizeOf<FaceRect>()));
                            //if (ArcWrapper.ExtractFeature(rEngine, ref imageData, ref CacheFaceResults[faceIndex].FFI, out var fm) == (int)ErrorCode.Ok)
                                CopyMemory(CacheFaceResults.Items[faceIndex].FaceModel.PFeature, detectResult.faceOrients, FeatureSize);

                        }
                    });
                }
                Task.WaitAll(ts);
            }

            bitmap.UnlockBits(bmpData);

            var tsr = new Task[TaskNum];
            List<int> noMatchFaces = Enumerable.Range(0, detectResult.faceNum).ToList();
            for (int i = 0; i < TaskNum; i++)
            {
                var taskIndex = i;
                tsr[i] = Task.Factory.StartNew(() =>
                {
                    var rEngine = _REngine[taskIndex];

                    foreach (var item in _FaceLib[taskIndex].Items.OrderByDescending(ii => ii.OrderId))
                    {
                        _RWL.EnterReadLock();
                        if (noMatchFaces.Count == 0)
                        {
                            _RWL.ExitReadLock();
                            break;
                        }
                        var faceIndexs = noMatchFaces.ToList();
                        _RWL.ExitReadLock();

                        foreach (var faceIndex in faceIndexs)
                        {
                            var score = 0f;
                            GCHandle handle = GCHandle.Alloc(CacheFaceResults.Items[faceIndex].FaceModel);
                            IntPtr soreFace = GCHandle.ToIntPtr(handle);
                            GCHandle handle1 = GCHandle.Alloc(item.FaceModel);
                            IntPtr face = GCHandle.ToIntPtr(handle1); 
                            ASFFunctions.ASFFaceFeatureCompare(rEngine, soreFace, face, ref score);
                            if (score > 0.15)
                            {
                                _RWL.EnterWriteLock();
                                noMatchFaces.Remove(faceIndex);
                                _RWL.ExitWriteLock();

                                CacheFaceResults[faceIndex].ID = item.ID;
                                CacheFaceResults[faceIndex].Score = score;
                                item.OrderId = DateTime.Now.Ticks;
                            }
                        }
                    }
                });

            }

            Task.WaitAll(tsr);
            foreach (var faceIndex in noMatchFaces)
            {
                CacheFaceResults[faceIndex].ID = "";
                CacheFaceResults[faceIndex].Score = 0;
            }



        } 

        public static bool CheckID(string id)
        {
            int count = 0;
            for (int i = 0; i < TaskNum; i++)
                count += _FaceLib[i].Items.Count(ii => ii.ID == id);
            return count > 0;
        }
        public static void AddFace(string id, byte[] featureData, Image img)
        {
            var fileName = Path.Combine(_FaceDataPath, id + ".dat");
            System.IO.File.WriteAllBytes(fileName, featureData);
            fileName = Path.Combine(_FaceImagePath, id + ".bmp");
            img.Save(fileName);
            var faceModel = new FaceModel
            {
                Size = featureData.Length,
                PFeature = Marshal.AllocHGlobal(featureData.Length)
            };

            Marshal.Copy(featureData, 0, faceModel.PFeature, featureData.Length);
            _FaceLib[0].Items.Add(new FaceLib.Item() { OrderId = DateTime.Now.Ticks, ID = id, FaceModel = faceModel });
        }
    }

    class MemLock : IDisposable
    {
        GCHandle IndicesHandle;


        public MemLock(object obj)
        {
            IndicesHandle = GCHandle.Alloc(obj, GCHandleType.Pinned);
        }

        public IntPtr Addr()
        {
            return IndicesHandle.AddrOfPinnedObject();
        }

        public void Dispose()
        {
            IndicesHandle.Free();
        }
    }
}
