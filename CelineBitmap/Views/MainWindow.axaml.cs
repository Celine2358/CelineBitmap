using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CelineBitmap.Services;
using OpenCvSharp;

namespace CelineBitmap.Views;

public partial class MainWindow : Avalonia.Controls.Window
{
    // 원본 이미지 Mat 데이터
    Mat? _originalImage;
    // 변환된 이미지 Mat 데이터
    Mat? _workingImage;

    WriteableBitmap? _beforeBitmap;
    WriteableBitmap? _afterBitmap;

    // Width를 바꾸면서 Height가 자동으로 바뀔 때
    // 다시 Width 이벤트가 발생하는 무한 반복 방지
    bool _updatingSize;

    // 여러 UI 값을 한 번에 변경할 때
    // 슬라이더 이벤트가 계속 실행되는 것을 방지
    bool _updatingControls;

    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Open Image 버튼
    /// PNG / JPG 등의 이미지를 선택한다
    /// </summary>
    private async void OpenImageButton_Click(object? sender, RoutedEventArgs e)
    {
        // Avalonia의 파일 선택 창을 연다
        var files = await StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "Open Image",
                    AllowMultiple = false,
                    FileTypeFilter =
                    [
                        new FilePickerFileType("Image Files")
                        {
                            Patterns =
                            [
                                "*.png",
                                "*.jpg",
                                "*.jpeg",
                                "*.bmp",
                                "*.webp"
                            ]
                        }
                    ]
                });

        // 사용자가 취소했다면 종료
        if (files.Count == 0) return;

        // Windows의 실제 파일 경로 가져오기
        string? path = files[0].TryGetLocalPath();

        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            // 기존 Mat이 존재하면 Native 메모리를 먼저 정리한다
            _originalImage?.Dispose();
            _workingImage?.Dispose();

            // OpenCV로 이미지 읽기
            _originalImage = ImageProcessor.Load(path);

            // 작업용 이미지는 원본을 복제해서 시작
            _workingImage = _originalImage.Clone();

            // 여러 UI 값을 변경하는 동안
            // ValueChanged 이벤트 실행 방지
            _updatingControls = true;
            _updatingSize = true;

            // 원본 해상도 표시
            OriginalSizeText.Text = $"{_originalImage.Width} × {_originalImage.Height}";

            // Target Size도 처음에는 원본 크기로 설정
            WidthInput.Value = _originalImage.Width;
            HeightInput.Value = _originalImage.Height;

            // 이미지 조정값 초기화
            BrightnessSlider.Value = 0;
            ContrastSlider.Value = 100;
            SaturationSlider.Value = 100;

            // 필터 초기화
            GrayscaleToggle.IsChecked = false;
            BlurToggle.IsChecked = false;
            SharpenToggle.IsChecked = false;
            EdgeToggle.IsChecked = false;

            _updatingSize = false;
            _updatingControls = false;

            // Before / After 화면 표시
            UpdateBeforePreview();
            UpdateAfterPreview();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }

    /// <summary>
    /// ComboBox의 선택값을 OpenCV의
    /// InterpolationFlags로 변환한다.
    /// </summary>
    InterpolationFlags GetSelectedInterpolation()
    {
        return InterpolationComboBox.SelectedIndex switch
        {
            0 => InterpolationFlags.Lanczos4,
            1 => InterpolationFlags.Cubic,
            2 => InterpolationFlags.Area,
            3 => InterpolationFlags.Nearest,

            _ => InterpolationFlags.Lanczos4
        };
    }

    /// <summary>
    /// 현재 이미지를 PNG/JPG로 저장한다
    /// </summary>
    private async void SaveImageButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_workingImage is null || _workingImage.Empty())
        {
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(
                new FilePickerSaveOptions
                {
                    Title = "Save Image",
                    SuggestedFileName = "CelineBitmap_Output.png",
                    DefaultExtension = "png",

                    FileTypeChoices =
                    [
                        new FilePickerFileType("PNG Image")
                        {
                            Patterns = ["*.png"]
                        },

                        new FilePickerFileType("JPEG Image")
                        {
                            Patterns =
                            [
                                "*.jpg",
                                "*.jpeg"
                            ]
                        }
                    ]
                });

        // 저장 취소
        if (file is null) return;

        string? path = file.TryGetLocalPath();

        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            ImageProcessor.Save(_workingImage, path);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }

    // 너비 변경
    void WidthInput_ValueChanged(object? sender, Avalonia.Controls.NumericUpDownValueChangedEventArgs e)
    {
        if (_updatingSize) return;
        if (_originalImage is null) return;
        if (KeepAspectRatioCheckBox.IsChecked != true) return;
        if (WidthInput.Value is null) return;

        double ratio = (double)_originalImage.Width / _originalImage.Height;
        int newWidth = (int)WidthInput.Value.Value;
        int newHeight = (int)Math.Round(newWidth / ratio);

        _updatingSize = true;
        HeightInput.Value = newHeight;
        _updatingSize = false;
    }

    // 높이 변경
    void HeightInput_ValueChanged(object? sender, Avalonia.Controls.NumericUpDownValueChangedEventArgs e)
    {
        if (_updatingSize) return;
        if (_originalImage is null) return;
        if (KeepAspectRatioCheckBox.IsChecked != true) return;
        if (HeightInput.Value is null) return;

        double ratio = (double)_originalImage.Width / _originalImage.Height;
        int newHeight = (int)HeightInput.Value.Value;
        int newWidth = (int)Math.Round(newHeight * ratio);

        _updatingSize = true;
        WidthInput.Value = newWidth;
        _updatingSize = false;
    }

    // 배율 정하기
    void ScaleButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_originalImage is null) return;
        if (sender is not Button button) return;

        double scale = button.Tag switch
        {
            "0.25" => 0.25,
            "0.5" => 0.5,
            "1" => 1.0,
            "2" => 2.0,
            "4" => 4.0,

            _ => 1.0
        };

        int width = (int)Math.Round(_originalImage.Width * scale);
        int height = (int)Math.Round(_originalImage.Height * scale);

        _updatingSize = true;

        WidthInput.Value = width;
        HeightInput.Value = height;

        _updatingSize = false;

        RebuildWorkingImage();
    }

    // 모든 옵션을 원본에서 다시 계산한다
    void RebuildWorkingImage()
    {
        if (_originalImage is null || _originalImage.Empty()) return;

        // 너비나 높이 선택값이 null이면 원본 이미지의 원래 너비와 높이를 쓴다
        int width = (int)(WidthInput.Value ?? _originalImage.Width);
        int height = (int)(HeightInput.Value ?? _originalImage.Height);

        InterpolationFlags interpolation = GetSelectedInterpolation();

        /* 항상 Original에서 시작한다
         *
         * 이전 결과에 계속 필터를 누적하지 않기 때문에
         * 비파괴 방식으로 동작한다
         */

        Mat result = ImageProcessor.Resize(_originalImage, width, height, interpolation);

        // Brightness / Contrast
        double brightness = BrightnessSlider.Value;
        double contrast = ContrastSlider.Value / 100.0;

        Mat next = ImageProcessor.AdjustBrightnessContrast(result, brightness, contrast);

        result.Dispose();
        result = next;

        // Saturation
        double saturation = SaturationSlider.Value / 100.0;

        next = ImageProcessor.AdjustSaturation(result, saturation);

        result.Dispose();
        result = next;

        // Blur
        if (BlurToggle.IsChecked == true)
        {
            next = ImageProcessor.GaussianBlur(result);

            result.Dispose();
            result = next;
        }

        // Sharpen
        if (SharpenToggle.IsChecked == true)
        {
            next = ImageProcessor.Sharpen(result);

            result.Dispose();
            result = next;
        }

        // Grayscale
        if (GrayscaleToggle.IsChecked == true)
        {
            next = ImageProcessor.Grayscale(result);

            result.Dispose();
            result = next;
        }

        // Edge Detection
        if (EdgeToggle.IsChecked == true)
        {
            next = ImageProcessor.Edge(result);

            result.Dispose();
            result = next;
        }

        // 이전 Working Mat 메모리 정리
        _workingImage?.Dispose();
        _workingImage = result;

        UpdateAfterPreview();
    }

    // 슬라이더 연결
    void ColorSlider_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_updatingControls) return;
        if (_originalImage is null) return;

        RebuildWorkingImage();
    }

    // 필터 버튼
    void FilterToggle_Click(object? sender, RoutedEventArgs e)
    {
        if (_originalImage is null) return;

        RebuildWorkingImage();
    }

    void ResizeButton_Click(object? sender, RoutedEventArgs e)
    {
        RebuildWorkingImage();
    }

    // 이미지 Before
    void UpdateBeforePreview()
    {
        if (_originalImage is null) return;

        WriteableBitmap bitmap = ImageProcessor.ToBitmap(_originalImage);
        BeforeImage.Source = bitmap;

        _beforeBitmap?.Dispose();
        _beforeBitmap = bitmap;
    }

    // 이미지 After
    void UpdateAfterPreview()
    {
        if (_workingImage is null) return;

        WriteableBitmap bitmap = ImageProcessor.ToBitmap(_workingImage);
        AfterImage.Source = bitmap;

        _afterBitmap?.Dispose();
        _afterBitmap = bitmap;
    }

    // 리셋 버튼
    void ResetButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_originalImage is null) return;

        _updatingControls = true;
        _updatingSize = true;

        WidthInput.Value = _originalImage.Width;
        HeightInput.Value = _originalImage.Height;

        BrightnessSlider.Value = 0;
        ContrastSlider.Value = 100;
        SaturationSlider.Value = 100;

        GrayscaleToggle.IsChecked = false;
        BlurToggle.IsChecked = false;
        SharpenToggle.IsChecked = false;
        EdgeToggle.IsChecked = false;

        InterpolationComboBox.SelectedIndex = 0;

        _updatingSize = false;
        _updatingControls = false;

        RebuildWorkingImage();
    }

    /// <summary>
    /// 프로그램을 닫을 때
    /// OpenCV / Avalonia Native 리소스를 정리한다
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        _originalImage?.Dispose();
        _workingImage?.Dispose();

        _beforeBitmap?.Dispose();
        _afterBitmap?.Dispose();

        base.OnClosed(e);
    }
}