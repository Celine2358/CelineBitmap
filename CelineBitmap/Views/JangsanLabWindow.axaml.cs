using System;
using System.IO;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CelineBitmap.Models;
using CelineBitmap.Services;
using OpenCvSharp;
using OpenCvSharp.AvaloniaExtensions;

namespace CelineBitmap.Views;

public partial class JangsanLabWindow : Avalonia.Controls.Window
{
    // 실제 Webcam
    VideoCapture? _capture;

    // Webcam 화면 갱신 Timer
    readonly DispatcherTimer _cameraTimer;

    // 가장 최근 Camera Frame
    Mat? _latestFrame;

    // 잘라낸 장산이 얼굴
    Mat? _jangsanFace;

    // 추출된 Vector
    JangsanVector? _jangsanVector;

    // Avalonia에 표시 중인 Bitmap
    WriteableBitmap? _cameraBitmap;

    // 사용자가 선택한 저장 폴더
    string? _saveFolder;

    public JangsanLabWindow()
    {
        InitializeComponent();

        // 약 20 FPS
        _cameraTimer =
            new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };

        _cameraTimer.Tick += CameraTimer_Tick;
    }

    // 카메라 시작
    void StartCameraButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_capture is not null) return;

        /* 0 = 첫 번째 Webcam
         * APC850 하나만 연결되어 있다면
         * 일반적으로 0번일 가능성이 높다
         */
        _capture = new VideoCapture(0, VideoCaptureAPIs.ANY);

        // 웹캠이 켜져 있지 않다면
        if (!_capture.IsOpened())
        {
            _capture.Dispose();
            _capture = null;

            CameraStatusText.Text = "Camera Open Failed";
            StatusText.Text = "웹캠을 열 수 없습니다.";

            return;
        }

        // 기본 목표 해상도 1280 x 720
        _capture.Set(VideoCaptureProperties.FrameWidth, 1280);
        _capture.Set(VideoCaptureProperties.FrameHeight, 720);
        _cameraTimer.Start();

        CameraStatusText.Text = "Live";
        StatusText.Text = "장산이 얼굴을 중앙 박스에 맞춰!";
    }

    // 카메라 프리뷰
    void CameraTimer_Tick(object? sender, EventArgs e)
    {
        if (_capture is null) return;

        using var frame = new Mat();

        // Webcam의 한 Frame을 Mat으로 읽는다
        if (!_capture.Read(frame)) return;
        if (frame.Empty()) return;

        // 최신 Frame 보관
        _latestFrame?.Dispose();
        _latestFrame = frame.Clone();

        // Preview용 이미지
        using Mat display = frame.Clone();

        // 실제 분석에 사용될 중앙 ROI
        Rect roi = GetCenterFaceRoi(display);

        // OpenCV로 Rectangle을 직접 그려준다
        Cv2.Rectangle(display, roi, new Scalar(255, 200, 100), 2);

        WriteableBitmap newBitmap = display.ToWriteableBitmap();
        CameraPreview.Source = newBitmap;

        _cameraBitmap?.Dispose();
        _cameraBitmap = newBitmap;
    }

    // ROI: Region Of Interest
    // 이 사각형 공간만 집중해서 분석하겠다
    // 중앙 Face ROI 계산
    static Rect GetCenterFaceRoi(Mat frame)
    {
        // 가로/세로 중 짧은 쪽의
        // 약 45% 크기로 정사각형 영역 생성
        int size = (int)(Math.Min(frame.Width, frame.Height) * 0.45);

        int x = (frame.Width - size) / 2;
        int y = (frame.Height - size) / 2;

        return new Rect(x, y, size, size);
    }


    // 장산이의 Vector 생성
    void ExtractVectorButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_latestFrame is null || _latestFrame.Empty())
        {
            StatusText.Text = "먼저 카메라를 시작하세요.";
            return;
        }

        Rect roi = GetCenterFaceRoi(_latestFrame);

        // 전체 Camera Mat 중에서
        // 얼굴 영역만 바라보는 Mat View
        using Mat faceView = new Mat(_latestFrame, roi);

        // 독립된 이미지로 복제
        _jangsanFace?.Dispose();
        _jangsanFace = faceView.Clone();

        // 얼굴 -> 숫자 Vector
        _jangsanVector = JangsanVectorExtractor.Extract(_jangsanFace);

        VectorStatusText.Text =
            $"Vector Ready\n" +
            $"{_jangsanVector.Values.Length} dimensions\n" +
            $"{_jangsanVector.Method}";


        UpdateSaveButton();

        StatusText.Text = "장산이 얼굴 Vector를 생성했습니다.";
    }


    // 저장 폴더 선택
    async void SelectFolderButton_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(
                    new FolderPickerOpenOptions
                    {
                        Title = "장산이 데이터 저장 폴더",
                        AllowMultiple = false
                    });

        if (folders.Count == 0) return;

        string? path = folders[0].TryGetLocalPath();

        if (string.IsNullOrWhiteSpace(path)) return;

        _saveFolder = path;
        FolderText.Text = path;

        UpdateSaveButton();
    }

    // 데이터 저장
    async void SaveDataButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_jangsanVector is null ||
            _jangsanFace is null ||
            _latestFrame is null ||
            string.IsNullOrWhiteSpace(_saveFolder))
        {
            return;
        }

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string name = $"Jangsan_{timestamp}";
        string framePath = Path.Combine(_saveFolder, $"{name}_frame.png");
        string facePath = Path.Combine(_saveFolder, $"{name}_face.png");
        string vectorPath = Path.Combine(_saveFolder, $"{name}_vector.json");

        // 전체 Camera Frame
        Cv2.ImWrite(framePath, _latestFrame);

        // 얼굴 ROI
        Cv2.ImWrite(facePath, _jangsanFace);

        // Vector
        string json = JsonSerializer.Serialize (_jangsanVector,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });

        await File.WriteAllTextAsync(vectorPath, json);

        StatusText.Text = $"데이터 저장 완료: {name}";
    }


    // Vector와 Folder가 모두 준비되면
    // Save 버튼 활성화
    void UpdateSaveButton()
    {
        SaveDataButton.IsEnabled =
            _jangsanVector is not null &&
            _jangsanFace is not null &&
            !string.IsNullOrWhiteSpace(_saveFolder);
    }

    // Camera Stop
    void StopCameraButton_Click(object? sender, RoutedEventArgs e)
    {
        StopCamera();
    }


    void StopCamera()
    {
        _cameraTimer.Stop();
        _capture?.Release();
        _capture?.Dispose();
        _capture = null;

        CameraStatusText.Text = "Stopped";
    }

    protected override void OnClosed(EventArgs e)
    {
        StopCamera();

        _latestFrame?.Dispose();
        _jangsanFace?.Dispose();
        _cameraBitmap?.Dispose();

        base.OnClosed(e);
    }
}