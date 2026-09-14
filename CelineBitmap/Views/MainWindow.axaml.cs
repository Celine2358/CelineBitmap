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
    // 현재 Celine Bitmap에서 편집 중인 OpenCV 이미지
    Mat? _currentImage;

    // Avalonia UI에 표시하고 있는 비트맵
    WriteableBitmap? _previewBitmap;

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
            _currentImage?.Dispose();

            // OpenCV로 이미지 읽기
            _currentImage = ImageProcessor.Load(path);

            // 이미지 원본 크기를 UI에 표시
            WidthInput.Value = _currentImage.Width;
            HeightInput.Value = _currentImage.Height;

            // Avalonia 화면에 표시
            UpdatePreview();

            // 안내 문구 숨기기
            EmptyImageMessage.IsVisible = false;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }


    /// <summary>
    /// 현재 설정된 Width / Height와 보간법을 이용하여 이미지를 Resize한다
    /// </summary>
    private void ResizeButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentImage is null || _currentImage.Empty())
        {
            return;
        }

        // NumericUpDown.Value는 decimal? 형태이므로 int로 변환한다
        int width = (int)(WidthInput.Value ?? 1);
        int height = (int)(HeightInput.Value ?? 1);


        // ComboBox에서 선택된 보간법을 얻는다
        InterpolationFlags interpolation = GetSelectedInterpolation();

        // Resize 결과는 새로운 Mat으로 생성된다
        Mat resized = ImageProcessor.Resize(_currentImage, width, height, interpolation);

        // 기존 Mat은 더 이상 필요 없으므로 해제
        _currentImage.Dispose();

        // Resize된 Mat을 현재 이미지로 교체
        _currentImage = resized;

        // 화면도 새 이미지로 갱신
        UpdatePreview();
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
    /// OpenCV Mat을 Avalonia Bitmap으로 변환하여 화면에 표시한다
    /// </summary>
    void UpdatePreview()
    {
        if (_currentImage is null) return;

        // 새로운 Bitmap 생성
        WriteableBitmap newBitmap = ImageProcessor.ToBitmap(_currentImage);

        // Image 컨트롤에 표시
        PreviewImage.Source = newBitmap;

        // 이전 Bitmap의 메모리를 해제한다.
        _previewBitmap?.Dispose();

        _previewBitmap = newBitmap;
    }


    /// <summary>
    /// 현재 이미지를 PNG/JPG로 저장한다
    /// </summary>
    private async void SaveImageButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentImage is null || _currentImage.Empty())
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
            ImageProcessor.Save(_currentImage, path);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }


    /// <summary>
    /// 프로그램을 닫을 때
    /// OpenCV / Avalonia Native 리소스를 정리한다
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        _currentImage?.Dispose();
        _previewBitmap?.Dispose();

        base.OnClosed(e);
    }
}